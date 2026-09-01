"""CloudLight Blizzard's direct-only Bilibili Drops Worker.

The process owns all Bilibili network activity and speaks the shared JSONL
Worker protocol on stdout.  Human-readable logs go to the log file only.
"""

from __future__ import annotations

import asyncio
import json
import logging
import os
import re
import sys
import threading
import time
from dataclasses import dataclass, field
from datetime import datetime, timezone
from pathlib import Path
from typing import Any, Callable
from urllib.parse import urlsplit

HERE = Path(__file__).resolve().parent
if str(HERE.parent) not in sys.path:
    sys.path.insert(0, str(HERE.parent))
if str(HERE) not in sys.path:
    sys.path.insert(0, str(HERE))
VENDOR = HERE / "vendor"
if str(VENDOR) not in sys.path:
    sys.path.insert(0, str(VENDOR))

# This must happen before importing httpx, anyio, qrcode, or the vendored core.
from direct_network import (  # noqa: E402
    direct_httpx_kwargs,
    proxy_environment_is_clean,
    scrub_proxy_environment,
)

_REMOVED_PROXY_VARIABLES = scrub_proxy_environment()

from dpapi import read_protected, write_protected  # noqa: E402
from Shared.protocol import (  # noqa: E402
    PROTOCOL_VERSION,
    WorkerBase,
    atomic_write_json,
    event,
    read_json,
    redact,
    run_worker,
)

from bilibili_drops_miner.client import BilibiliClient  # noqa: E402
from bilibili_drops_miner.client_parts.models import (  # noqa: E402
    MissionRewardClaimResult,
    TaskProgress,
)
from bilibili_drops_miner.client_parts.qr_login import (  # noqa: E402
    QrLoginApi,
    QrLoginError,
    QrLoginStatus,
)
from bilibili_drops_miner.client_parts.task_discovery import (  # noqa: E402
    fetch_live_task_groups,
)
from bilibili_drops_miner.config import MinerConfig  # noqa: E402
from bilibili_drops_miner.notifier import MultiPlatformNotifier  # noqa: E402
from bilibili_drops_miner.x25kn_worker import X25KnWorker  # noqa: E402


LOGGER = logging.getLogger(__name__)
MAX_SESSIONS_PER_ROOM = 128
MIN_TASK_INTERVAL_SECONDS = 10
DEFAULT_TASK_INTERVAL_SECONDS = 30
DEFAULT_RECONNECT_DELAY_SECONDS = 8
LOGIN_WATCHDOG_SECONDS = 60
ROOM_ID_PATTERN = re.compile(r"^\d+$")


def utc_now() -> str:
    return datetime.now(timezone.utc).isoformat()


def safe_message(value: object) -> str:
    return redact(value).replace("\x00", "")


def positive_int(value: object, name: str, *, maximum: int | None = None) -> int:
    try:
        number = int(value)
    except (TypeError, ValueError) as exc:
        raise ValueError(f"{name} 必须是正整数") from exc
    if number <= 0:
        raise ValueError(f"{name} 必须大于 0")
    if maximum is not None and number > maximum:
        raise ValueError(f"{name} 不能大于 {maximum}")
    return number


def parse_room_reference(value: object) -> int:
    """Accept a room number or a live.bilibili.com URL and return room ID."""

    raw = str(value or "").strip()
    if not raw:
        raise ValueError("直播间不能为空")
    if ROOM_ID_PATTERN.fullmatch(raw):
        room_id = int(raw)
    else:
        candidate = raw if "://" in raw else f"https://{raw}"
        parsed = urlsplit(candidate)
        if parsed.hostname not in {"live.bilibili.com", "www.live.bilibili.com"}:
            raise ValueError("直播间 URL 必须来自 live.bilibili.com")
        parts = [part for part in parsed.path.split("/") if part]
        numeric = next((part for part in parts if ROOM_ID_PATTERN.fullmatch(part)), "")
        if not numeric:
            raise ValueError("直播间 URL 中没有有效房间号")
        room_id = int(numeric)
    if room_id <= 0:
        raise ValueError("房间号必须大于 0")
    return room_id


def room_record(room_id: int, *, name: str = "", enabled: bool = True, **extra: Any) -> dict[str, Any]:
    return {
        "id": int(room_id),
        "name": name.strip() or f"直播间 {room_id}",
        "url": f"https://live.bilibili.com/{room_id}",
        "enabled": bool(enabled),
        "liveStatus": int(extra.get("liveStatus", 0) or 0),
        "lastError": safe_message(extra.get("lastError", "")),
    }


def task_record(task: TaskProgress, claimed_ids: set[str]) -> dict[str, Any]:
    checkpoints = []
    for point in task.check_points or []:
        point_id = str(point.sid or "").strip()
        point_claimed = point.status == 6 or point_id in claimed_ids
        point_completed = bool(point.is_completed)
        checkpoints.append({
            "id": point_id,
            "name": safe_message(point.alias),
            "statusCode": point.status,
            "current": point.cur_value,
            "limit": point.limit_value,
            "percent": progress_percent(point.cur_value, point.limit_value),
            "completed": point_completed,
            "claimed": point_claimed,
            "claimable": point_completed and not point_claimed,
            "reward": safe_message(point.award_name),
            "rewardCount": point.award_count,
        })
    task_id = str(task.task_id or "").strip()
    claimed = task.status == 6 or task_id in claimed_ids
    completed = bool(task.is_completed)
    return {
        "id": task_id,
        "name": safe_message(task.task_name),
        "statusCode": task.status,
        "current": task.cur_value,
        "limit": task.limit_value,
        "percent": progress_percent(task.cur_value, task.limit_value),
        "completed": completed,
        "claimed": claimed,
        "claimable": completed and not claimed,
        "status": "已领取" if claimed else "可领取" if completed else "进行中",
        "checkpoints": checkpoints,
    }


def progress_percent(current: object, limit: object) -> float:
    try:
        current_number = float(current)
        limit_number = float(limit)
    except (TypeError, ValueError):
        return 0.0
    if limit_number <= 0:
        return 0.0
    return max(0.0, min(100.0, current_number / limit_number * 100.0))


def classify_qr_status(status: QrLoginStatus) -> tuple[str, str]:
    return {
        QrLoginStatus.UNSCANNED: ("waiting_scan", "等待扫码"),
        QrLoginStatus.CONFIRMED_PENDING: ("scanned_pending", "已扫码，等待手机确认"),
        QrLoginStatus.EXPIRED: ("expired", "二维码已过期"),
        QrLoginStatus.SUCCESS: ("success", "登录成功"),
    }[status]


@dataclass
class SessionRecord:
    key: str
    room_id: int
    session_no: int
    start_delay: float
    state: str = "connecting"
    detail: str = ""
    failures: int = 0
    reconnect_count: int = 0
    last_state_at: str = field(default_factory=utc_now)
    stop_event: threading.Event = field(default_factory=threading.Event)
    thread: threading.Thread | None = None


class BilibiliSessionPool:
    """One OS thread and one asyncio loop/HTTP client per upstream session."""

    def __init__(
        self,
        get_cookie: Callable[[], str],
        get_reconnect_delay: Callable[[], int],
        get_reconnect_enabled: Callable[[], bool],
        get_uid: Callable[[], int],
        on_change: Callable[[], None],
    ) -> None:
        self._get_cookie = get_cookie
        self._get_reconnect_delay = get_reconnect_delay
        self._get_reconnect_enabled = get_reconnect_enabled
        self._get_uid = get_uid
        self._on_change = on_change
        self._lock = threading.RLock()
        self._records: dict[str, SessionRecord] = {}
        self._stopping = False

    def snapshot(self) -> dict[str, Any]:
        with self._lock:
            records = list(self._records.values())
        configured = len(records)
        active = sum(item.state == "active" for item in records)
        connecting = sum(item.state == "connecting" for item in records)
        retrying = sum(item.state == "retrying" for item in records)
        failed = sum(item.state == "failed" for item in records)
        return {
            "configuredSessions": configured,
            "activeSessions": active,
            "connectingSessions": connecting,
            "retryingSessions": retrying,
            "failedSessions": failed,
            "sessions": [
                {
                    "id": item.key,
                    "roomId": item.room_id,
                    "sessionNo": item.session_no,
                    "state": item.state,
                    "detail": safe_message(item.detail),
                    "failures": item.failures,
                    "reconnectCount": item.reconnect_count,
                    "lastStateAt": item.last_state_at,
                }
                for item in sorted(records, key=lambda value: value.key)
            ],
        }

    def reconcile(self, room_ids: list[int], sessions_per_room: int, uid: int) -> None:
        desired: list[tuple[str, int, int]] = []
        for room_id in room_ids:
            for session_no in range(1, sessions_per_room + 1):
                desired.append((f"r{room_id}-s{session_no}", room_id, session_no))
        desired_keys = {item[0] for item in desired}
        with self._lock:
            removed = [record for key, record in self._records.items() if key not in desired_keys]
            for record in removed:
                record.stop_event.set()
                del self._records[record.key]
            if self._stopping:
                removed_threads = [record.thread for record in removed if record.thread is not None]
                for thread in removed_threads:
                    thread.join(timeout=0.4)
                self._on_change()
                return
            additions: list[SessionRecord] = []
            for index, (key, room_id, session_no) in enumerate(desired):
                if key in self._records:
                    continue
                record = SessionRecord(key, room_id, session_no, float(index))
                self._records[key] = record
                additions.append(record)
            for record in additions:
                record.thread = threading.Thread(
                    target=self._thread_entry,
                    args=(record, uid),
                    name=f"bilibili-{record.key}",
                    daemon=True,
                )
            removed_threads = [record.thread for record in removed if record.thread is not None]
        # A settings change must not orphan sessions that were removed from the
        # desired set.  Join outside the pool lock so callbacks can publish a
        # fresh aggregate while the old event loop is winding down.
        for thread in removed_threads:
            thread.join(timeout=0.4)
        for record in additions:
            assert record.thread is not None
            record.thread.start()
        self._on_change()

    def revive(self) -> None:
        with self._lock:
            self._stopping = False

    def stop(self, timeout: float = 12.0) -> None:
        with self._lock:
            self._stopping = True
            records = list(self._records.values())
            for record in records:
                record.stop_event.set()
        deadline = time.monotonic() + timeout
        for record in records:
            thread = record.thread
            if thread is None:
                continue
            remaining = max(0.0, deadline - time.monotonic())
            thread.join(min(0.4, remaining))
        with self._lock:
            self._records.clear()
        self._on_change()

    def _set_state(self, record: SessionRecord, state: str, detail: str = "") -> None:
        with self._lock:
            if record.key not in self._records:
                return
            record.state = state
            record.detail = safe_message(detail)
            record.last_state_at = utc_now()
            if state == "retrying":
                record.failures += 1
            elif state == "active":
                record.failures = 0
        self._on_change()

    def _thread_entry(self, record: SessionRecord, uid: int) -> None:
        if record.start_delay and record.stop_event.wait(record.start_delay):
            self._set_state(record, "stopped")
            return
        while not record.stop_event.is_set():
            self._set_state(record, "connecting")
            try:
                asyncio.run(self._run_session(record, uid))
            except Exception as exc:
                self._set_state(record, "failed", str(exc))
                LOGGER.warning("session %s exited: %s", record.key, safe_message(exc))
            if record.stop_event.is_set():
                break
            if not self._get_reconnect_enabled():
                self._set_state(record, "failed", "自动重连已关闭")
                break
            record.reconnect_count += 1
            self._set_state(record, "retrying", "会话已断开，等待重连")
            if record.stop_event.wait(self._get_reconnect_delay()):
                break
        self._set_state(record, "stopped")

    async def _run_session(self, record: SessionRecord, uid: int) -> None:
        client = BilibiliClient(self._get_cookie())
        stop_event = asyncio.Event()
        config = MinerConfig(
            cookie=self._get_cookie(),
            room_ids=[record.room_id],
            thread_count=1,
            reconnect_delay_seconds=self._get_reconnect_delay(),
            task_ids=[],
            task_query_interval_seconds=DEFAULT_TASK_INTERVAL_SECONDS,
        )
        notifier = MultiPlatformNotifier([])

        def state_callback(state: str, detail: str) -> None:
            self._set_state(record, state, detail)
            # X25KnWorker owns its own transient-error loop.  Stop that loop
            # at the boundary when the user has explicitly disabled automatic
            # reconnect; the outer pool loop then records the session as
            # failed without silently overriding the setting.
            if state == "retrying" and not self._get_reconnect_enabled():
                stop_event.set()

        upstream_worker = X25KnWorker(
            client=client,
            notifier=notifier,
            config=config,
            uid=uid,
            room_id=record.room_id,
            session_id=f"s{record.session_no}",
            primary_session=False,
            stop_event=stop_event,
            state_callback=state_callback,
        )
        run_task = asyncio.create_task(upstream_worker.run_forever(), name=record.key)
        bridge_task = asyncio.create_task(asyncio.to_thread(record.stop_event.wait))
        try:
            done, _ = await asyncio.wait(
                (run_task, bridge_task), return_when=asyncio.FIRST_COMPLETED
            )
            if bridge_task in done:
                await upstream_worker.stop()
            elif run_task in done and not run_task.cancelled():
                exception = run_task.exception()
                if exception is not None:
                    raise exception
                raise RuntimeError("上游会话意外结束")
        finally:
            await upstream_worker.stop()
            if not run_task.done():
                run_task.cancel()
            if not bridge_task.done():
                bridge_task.cancel()
            await asyncio.gather(run_task, bridge_task, return_exceptions=True)
            await client.close()


class BilibiliProgressPoller:
    def __init__(self, worker: "BilibiliWorker") -> None:
        self._worker = worker
        self._stop_event = threading.Event()
        self._thread: threading.Thread | None = None

    def start(self) -> None:
        if self._thread is not None and self._thread.is_alive():
            return
        self._stop_event.clear()
        self._thread = threading.Thread(
            target=self._run, name="bilibili-task-progress", daemon=True
        )
        self._thread.start()

    def stop(self) -> None:
        self._stop_event.set()
        thread = self._thread
        if thread is not None and thread is not threading.current_thread():
            thread.join(5.0)
        self._thread = None

    def _run(self) -> None:
        first = True
        while not self._stop_event.is_set():
            if not self._worker.auto_task_progress:
                self._stop_event.wait(1.0)
                continue
            if not first:
                self._stop_event.wait(self._worker.task_interval)
                if self._stop_event.is_set():
                    return
                if not self._worker.auto_task_progress:
                    continue
            first = False
            try:
                asyncio.run(self._worker.poll_progress_once())
            except Exception as exc:
                self._worker.report_warning("task_progress_failed", "任务进度查询失败", exc)


class BilibiliLoginWatchdog:
    def __init__(self, worker: "BilibiliWorker") -> None:
        self._worker = worker
        self._stop_event = threading.Event()
        self._thread: threading.Thread | None = None

    def start(self) -> None:
        if self._thread is not None and self._thread.is_alive():
            return
        self._stop_event.clear()
        self._thread = threading.Thread(target=self._run, name="bilibili-login-watchdog", daemon=True)
        self._thread.start()

    def stop(self) -> None:
        self._stop_event.set()
        thread = self._thread
        if thread is not None and thread is not threading.current_thread():
            thread.join(5.0)
        self._thread = None

    def _run(self) -> None:
        while not self._stop_event.wait(LOGIN_WATCHDOG_SECONDS):
            if not self._worker.running or not self._worker.cookie:
                continue
            try:
                uid, _ = asyncio.run(self._worker.probe_account())
            except Exception as exc:
                self._worker.report_warning("account_probe_failed", "账号状态复检失败，将稍后重试", exc)
                continue
            if uid is None or uid != self._worker.uid:
                self._worker.invalidate_login("Bilibili 登录已失效，请重新扫码登录")
                return


class BilibiliWorker(WorkerBase):
    platform = "bilibili"
    needs_https = True

    def __init__(self, data_dir: Path, log_file: Path) -> None:
        super().__init__(data_dir, log_file)
        self._state_path = self.data_dir / "state.json"
        self._credential_path = self.data_dir / "credential.dpapi"
        self._notifier_path = self.data_dir / "notifier.dpapi"
        self._qr_image_path = self.data_dir / "qr-login.png"
        self._state_lock = threading.RLock()
        self._claim_lock = threading.Lock()
        self._cookie = ""
        self._uid = 0
        self._uname = ""
        self._qr_api: QrLoginApi | None = None
        self._qr_key = ""
        self._claimed_ids: set[str] = set()
        self._claim_inflight: set[str] = set()
        self._notified_completed: set[str] = set()
        self._notifier = MultiPlatformNotifier([])
        self._pool = BilibiliSessionPool(
            lambda: self.cookie,
            lambda: self.reconnect_delay,
            lambda: self.reconnect_enabled,
            lambda: self.uid,
            self._publish_session_event,
        )
        self._progress_poller = BilibiliProgressPoller(self)
        self._login_watchdog = BilibiliLoginWatchdog(self)
        loaded = read_json(self._state_path, self._default_state())
        self._state = self._normalize_state(loaded)
        self._load_protected_credentials()
        self._load_protected_notifier()
        self.commands.update({
            "set_credentials": self.set_credentials,
            "qr_generate": self.qr_generate,
            "qr_poll": self.qr_poll,
            "qr_cancel": self.qr_cancel,
            "get_rooms": self.get_rooms,
            "add_room": self.add_room,
            "remove_room": self.remove_room,
            "set_room_enabled": self.set_room_enabled,
            "discover": self.discover,
            "claim_reward": self.claim_reward,
            "get_session_details": self.get_session_details,
            "auto_start": self.start,
            # Keep the shared command name useful for generic Drops tooling;
            # the product UI uses the explicit qr_* commands.
            "login": self.qr_generate,
        })
        # Vendor loggers share the redacting Worker handler and never use stderr.
        for logger_name in ("bilibili_drops_miner", "bilibili_drops_miner.client_parts"):
            vendor_logger = logging.getLogger(logger_name)
            vendor_logger.handlers.clear()
            for handler in self.logger.handlers:
                vendor_logger.addHandler(handler)
            vendor_logger.setLevel(logging.INFO)
            vendor_logger.propagate = False
        self.logger.info(
            "Bilibili Worker version=cloudlight-1 upstream_commit=a0d8bd51728aabaef66c651613324adba15d9ce8 network=DIRECT proxy_env_scrubbed=%s",
            len(_REMOVED_PROXY_VARIABLES),
        )

    def _default_state(self) -> dict[str, Any]:
        return {
            "rooms": [],
            "taskIds": [],
            "activities": [],
            "tasks": [],
            "rewards": [],
            "settings": {
                "enabled": False,
                "autoRestore": False,
                "autoResumeDrops": False,
                "watchMode": "standard",
                "sessionsPerRoom": 1,
                "reconnectDelay": DEFAULT_RECONNECT_DELAY_SECONDS,
                "taskInterval": DEFAULT_TASK_INTERVAL_SECONDS,
                "autoTaskProgress": True,
                "autoClaim": False,
                "taskNotifications": True,
                "autoDiscover": True,
                "reconnectEnabled": True,
                "notifyUrlsConfigured": False,
            },
            "lastProgressAt": "",
            "lastApiSuccessAt": "",
            "lastRecoveryAt": "",
            "lastError": "",
            "claimedTaskIds": [],
            "notifiedTaskIds": [],
        }

    def _normalize_state(self, state: dict[str, Any]) -> dict[str, Any]:
        default = self._default_state()
        normalized = json.loads(json.dumps(default, ensure_ascii=False))
        normalized.update({key: value for key, value in state.items() if key in normalized})
        normalized["rooms"] = [
            room_record(parse_room_reference(item.get("id")), name=str(item.get("name") or ""),
                        enabled=bool(item.get("enabled", True)), liveStatus=item.get("liveStatus", 0),
                        lastError=item.get("lastError", ""))
            for item in state.get("rooms", [])
            if isinstance(item, dict) and str(item.get("id", "")).isdigit() and int(item.get("id", 0)) > 0
        ]
        normalized["taskIds"] = [str(item).strip() for item in state.get("taskIds", []) if str(item).strip()]
        self._claimed_ids = {str(item).strip() for item in state.get("claimedTaskIds", []) if str(item).strip()}
        self._notified_completed = {str(item).strip() for item in state.get("notifiedTaskIds", []) if str(item).strip()}
        settings = normalized["settings"]
        if isinstance(state.get("settings"), dict):
            settings.update(state["settings"])
        settings["sessionsPerRoom"] = max(1, min(MAX_SESSIONS_PER_ROOM, int(settings.get("sessionsPerRoom", 1) or 1)))
        settings["reconnectDelay"] = max(1, int(settings.get("reconnectDelay", DEFAULT_RECONNECT_DELAY_SECONDS) or DEFAULT_RECONNECT_DELAY_SECONDS))
        settings["taskInterval"] = max(MIN_TASK_INTERVAL_SECONDS, int(settings.get("taskInterval", DEFAULT_TASK_INTERVAL_SECONDS) or DEFAULT_TASK_INTERVAL_SECONDS))
        normalized["activities"] = state.get("activities", []) if isinstance(state.get("activities"), list) else []
        normalized["tasks"] = state.get("tasks", []) if isinstance(state.get("tasks"), list) else []
        normalized["rewards"] = state.get("rewards", []) if isinstance(state.get("rewards"), list) else []
        return normalized

    @property
    def cookie(self) -> str:
        with self._state_lock:
            return self._cookie

    @property
    def uid(self) -> int:
        with self._state_lock:
            return self._uid

    @property
    def reconnect_delay(self) -> int:
        with self._state_lock:
            return int(self._state["settings"].get("reconnectDelay", DEFAULT_RECONNECT_DELAY_SECONDS))

    @property
    def task_interval(self) -> int:
        with self._state_lock:
            return int(self._state["settings"].get("taskInterval", DEFAULT_TASK_INTERVAL_SECONDS))

    @property
    def auto_task_progress(self) -> bool:
        with self._state_lock:
            return bool(self._state["settings"].get("autoTaskProgress", True))

    @property
    def reconnect_enabled(self) -> bool:
        with self._state_lock:
            return bool(self._state["settings"].get("reconnectEnabled", True))

    def _load_protected_credentials(self) -> None:
        try:
            value = read_protected(self._credential_path)
        except Exception as exc:
            self.logger.warning("读取加密登录凭据失败：%s", safe_message(exc))
            value = None
        if value:
            self._cookie = value
            try:
                uid, uname = asyncio.run(self.probe_account())
            except Exception:
                uid, uname = None, ""
            if uid:
                self._uid, self._uname = uid, uname

    def _load_protected_notifier(self) -> None:
        try:
            value = read_protected(self._notifier_path)
            urls = json.loads(value) if value else []
            if isinstance(urls, list):
                self._notifier.update_urls([str(url) for url in urls if str(url).strip()])
                self._state["settings"]["notifyUrlsConfigured"] = bool(urls)
        except Exception as exc:
            self.logger.warning("读取加密通知配置失败：%s", safe_message(exc))

    def _save_state(self) -> None:
        with self._state_lock:
            state = json.loads(json.dumps(self._state, ensure_ascii=False))
            state["account"] = {"loggedIn": bool(self._cookie and self._uid), "uid": self._uid, "userName": safe_message(self._uname)}
            state["credentialAvailable"] = bool(self._cookie)
            state["claimedTaskIds"] = sorted(self._claimed_ids)
            state["notifiedTaskIds"] = sorted(self._notified_completed)
        atomic_write_json(self._state_path, state)

    def _account_state(self) -> dict[str, Any]:
        return {
            "loggedIn": bool(self.cookie and self.uid),
            "uid": self.uid,
            "userName": safe_message(self._uname),
        }

    def _settings_state(self) -> dict[str, Any]:
        with self._state_lock:
            return dict(self._state["settings"])

    def _status_text(self) -> str:
        snapshot = self._pool.snapshot()
        configured = snapshot["configuredSessions"]
        active = snapshot["activeSessions"]
        retrying = snapshot["retryingSessions"]
        if not self.running:
            return "已停止"
        if configured and active == configured:
            return "已连接"
        if active > 0:
            return "连接质量下降"
        if retrying > 0:
            return "等待重试"
        return "连接中"

    def _summary_text(self) -> str:
        snapshot = self._pool.snapshot()
        account = self._uname or (f"UID {self._uid}" if self._uid else "未登录")
        return f"{account} · 会话 {snapshot['activeSessions']}/{snapshot['configuredSessions']} · 任务 {len(self._state.get('tasks', []))}"

    def _emit_status(self) -> None:
        snapshot = self._pool.snapshot()
        event("status", {
            "status": self._status_text(),
            "summary": self._summary_text(),
            "running": self.running,
            "connectionState": self._connection_state(snapshot),
            "networkMode": "DIRECT",
            "proxyPolicy": "ignored",
            "sessions": snapshot,
        })

    @staticmethod
    def _connection_state(snapshot: dict[str, Any]) -> str:
        configured = snapshot["configuredSessions"]
        active = snapshot["activeSessions"]
        retrying = snapshot["retryingSessions"]
        failed = snapshot["failedSessions"]
        if not configured:
            return "Stopped"
        if active == configured:
            return "Connected"
        if active > 0:
            return "Degraded"
        if retrying > 0:
            return "WaitingRetry"
        if failed == configured:
            return "Failed"
        return "Connecting"

    def _publish_session_event(self) -> None:
        snapshot = self._pool.snapshot()
        event("session", {
            "networkMode": "DIRECT",
            "status": self._status_text(),
            "summary": self._summary_text(),
            "running": self.running,
            "connectionState": self._connection_state(snapshot),
            **snapshot,
        })
        self._emit_status()

    def report_warning(self, code: str, title: str, error: object = "") -> None:
        detail = safe_message(error) if error else title
        with self._state_lock:
            self._state["lastError"] = detail
        self.logger.warning("%s: %s", code, detail)
        event("warning", {"code": code, "message": detail, "retryable": True})

    def report_error(self, code: str, message: str, *, retryable: bool = False) -> None:
        safe = safe_message(message)
        with self._state_lock:
            self._state["lastError"] = safe
        self.logger.error("%s: %s", code, safe)
        event("error", {"code": code, "message": safe, "retryable": retryable})

    def hello(self, payload: dict[str, Any]) -> dict[str, Any]:
        result = super().hello(payload)
        result.update({
            "networkMode": "DIRECT",
            "proxyPolicy": "ignored",
            "upstreamCommit": "a0d8bd51728aabaef66c651613324adba15d9ce8",
            "credentialAvailable": bool(self.cookie),
        })
        return result

    def load_state(self, payload: dict[str, Any]) -> dict[str, Any]:
        with self._state_lock:
            state = json.loads(json.dumps(self._state, ensure_ascii=False))
        state.update({
            "platform": self.platform,
            "running": self.running,
            "status": self._status_text(),
            "summary": self._summary_text(),
            "connectionState": self._connection_state(self._pool.snapshot()),
            "networkMode": "DIRECT",
            "proxyPolicy": "ignored",
            "account": self._account_state(),
            "credentialAvailable": bool(self.cookie),
            "sessions": self._pool.snapshot(),
            "lastProgressAt": state.get("lastProgressAt", ""),
            "lastApiSuccessAt": state.get("lastApiSuccessAt", ""),
            "lastRecoveryAt": state.get("lastRecoveryAt", ""),
        })
        return state

    def set_proxy(self, payload: dict[str, Any]) -> dict[str, Any]:
        # DropsHostService sends this to every platform.  Bilibili explicitly
        # ignores it and never returns the configured proxy URL.
        self.logger.info("忽略全局代理设置：Bilibili 网络策略为 DIRECT")
        return {"enableProxy": False, "proxyUrl": "", "fallbackDirect": False, "networkMode": "DIRECT", "ignored": True}

    def set_credentials(self, payload: dict[str, Any]) -> dict[str, Any]:
        raw = str(payload.get("cookie", "")).strip()
        if not raw:
            raise ValueError("登录凭据不能为空")
        # build_cookie_state validates the three required login values without
        # logging the incoming secret.
        from bilibili_drops_miner.client_parts.cookies import build_cookie_state

        cookie_map, _, _ = build_cookie_state(raw)
        required = ("SESSDATA", "DedeUserID", "bili_jct")
        if any(not cookie_map.get(name) for name in required):
            raise ValueError("登录凭据缺少必要 Cookie")
        with self._state_lock:
            previous = (self._cookie, self._uid, self._uname)
            self._cookie = raw
            self._uid = 0
            self._uname = ""
        try:
            uid, uname = asyncio.run(self.probe_account())
        except Exception:
            with self._state_lock:
                self._cookie, self._uid, self._uname = previous
            raise ValueError("Bilibili 登录凭据验证失败，请检查网络或重新登录")
        if uid is None:
            with self._state_lock:
                self._cookie, self._uid, self._uname = previous
            raise ValueError("Bilibili 登录凭据已失效")
        try:
            # Do not replace an existing protected file until the candidate
            # has passed the account probe above.
            write_protected(self._credential_path, raw)
        except Exception:
            with self._state_lock:
                self._cookie, self._uid, self._uname = previous
            raise
        with self._state_lock:
            self._uid, self._uname = uid, uname
            self._state["lastError"] = ""
        self._save_state()
        event("account", self._account_state())
        self.logger.info("登录状态变化：已登录 uid=%s", uid)
        return {
            "account": self._account_state(),
            "credentialAvailable": True,
            "credentialBlob": self._credential_blob(),
        }

    def qr_generate(self, payload: dict[str, Any]) -> dict[str, Any]:
        self.require_runtime()
        try:
            import qrcode
        except ImportError as exc:
            raise RuntimeError("二维码组件未打包，请重新安装 CloudLight Blizzard") from exc
        self.qr_cancel({})
        self._qr_api = QrLoginApi()
        challenge = self._qr_api.generate()
        self._qr_key = challenge.key
        image = qrcode.make(challenge.url)
        image.save(self._qr_image_path)
        event("qr_login", {
            "state": "waiting_scan",
            "message": "等待扫码",
            "imagePath": str(self._qr_image_path),
            "expiresInSeconds": 180,
        })
        self.logger.info("已生成本地二维码，等待扫码")
        return {
            "state": "waiting_scan",
            "message": "等待扫码",
            "imagePath": str(self._qr_image_path),
            "expiresInSeconds": 180,
        }

    def qr_poll(self, payload: dict[str, Any]) -> dict[str, Any]:
        if self._qr_api is None or not self._qr_key:
            raise ValueError("当前没有待确认的二维码")
        result = self._qr_api.poll(self._qr_key)
        state, message = classify_qr_status(result.status)
        if result.status is QrLoginStatus.SUCCESS:
            try:
                self._qr_api.close()
            finally:
                self._qr_api = None
            with self._state_lock:
                previous = (self._cookie, self._uid, self._uname)
                self._cookie = result.cookie
                self._uid = 0
                self._uname = ""
            try:
                uid, uname = asyncio.run(self.probe_account())
                if uid is None:
                    raise QrLoginError("扫码登录成功，但无法读取账号信息")
                # The cookie exists only in memory until DPAPI encryption
                # completes; it is never part of the JSONL response.
                write_protected(self._credential_path, result.cookie)
            except QrLoginError:
                with self._state_lock:
                    self._cookie, self._uid, self._uname = previous
                self._qr_key = ""
                self._delete_qr_image()
                raise
            except Exception as exc:
                with self._state_lock:
                    self._cookie, self._uid, self._uname = previous
                self._qr_key = ""
                self._delete_qr_image()
                raise QrLoginError("扫码登录成功，但账号凭据保存失败") from exc
            with self._state_lock:
                self._uid, self._uname = uid, uname
                self._state["lastError"] = ""
            self._qr_key = ""
            self._delete_qr_image()
            self._save_state()
            event("account", self._account_state())
            event("qr_login", {"state": state, "message": message, "account": self._account_state()})
            self.logger.info("登录状态变化：扫码登录成功 uid=%s", uid)
            return {
                "state": state,
                "message": message,
                "account": self._account_state(),
                "credentialAvailable": True,
                "credentialBlob": self._credential_blob(),
            }
        if result.status is QrLoginStatus.EXPIRED:
            self._close_qr()
            self._delete_qr_image()
        event("qr_login", {"state": state, "message": message})
        return {"state": state, "message": message}

    def qr_cancel(self, payload: dict[str, Any]) -> dict[str, Any]:
        self._close_qr()
        self._qr_key = ""
        self._delete_qr_image()
        event("qr_login", {"state": "cancelled", "message": "扫码已取消"})
        return {"state": "cancelled", "message": "扫码已取消"}

    def _close_qr(self) -> None:
        if self._qr_api is not None:
            try:
                self._qr_api.close()
            except Exception:
                pass
            self._qr_api = None

    def _delete_qr_image(self) -> None:
        try:
            self._qr_image_path.unlink(missing_ok=True)
        except OSError:
            pass

    def _clear_credential_file(self) -> None:
        try:
            self._credential_path.unlink(missing_ok=True)
        except OSError:
            pass

    def _clear_login_memory(self) -> None:
        with self._state_lock:
            self._cookie = ""
            self._uid = 0
            self._uname = ""

    def _credential_blob(self) -> str:
        try:
            return self._credential_path.read_text(encoding="ascii").strip()
        except OSError:
            return ""

    def logout(self, payload: dict[str, Any]) -> dict[str, Any]:
        self.stop({})
        self.qr_cancel({})
        self._clear_credential_file()
        self._clear_login_memory()
        with self._state_lock:
            self._state["tasks"] = []
            self._state["activities"] = []
            self._state["taskIds"] = []
        self._save_state()
        event("account", self._account_state())
        self.logger.info("登录状态变化：已退出登录")
        return self.load_state({})

    def get_accounts(self, payload: dict[str, Any]) -> list[dict[str, Any]]:
        return [self._account_state()]

    def get_tasks(self, payload: dict[str, Any]) -> list[dict[str, Any]]:
        with self._state_lock:
            return json.loads(json.dumps(self._state.get("tasks", []), ensure_ascii=False))

    def get_inventory(self, payload: dict[str, Any]) -> list[dict[str, Any]]:
        with self._state_lock:
            return json.loads(json.dumps(self._state.get("rewards", []), ensure_ascii=False))

    def get_rooms(self, payload: dict[str, Any]) -> dict[str, Any]:
        with self._state_lock:
            rooms = json.loads(json.dumps(self._state.get("rooms", []), ensure_ascii=False))
        return {"rooms": rooms}

    def add_room(self, payload: dict[str, Any]) -> dict[str, Any]:
        room_id = parse_room_reference(payload.get("roomId", payload.get("url", "")))
        name = str(payload.get("name", "")).strip()
        enabled = bool(payload.get("enabled", True))
        with self._state_lock:
            rooms = self._state["rooms"]
            existing = next((room for room in rooms if int(room["id"]) == room_id), None)
            if existing is None:
                rooms.append(room_record(room_id, name=name, enabled=enabled))
            else:
                existing.update(room_record(room_id, name=name or existing.get("name", ""), enabled=enabled))
        self._save_state()
        event("room", {"rooms": self.get_rooms({})["rooms"]})
        return self.get_rooms({})

    def remove_room(self, payload: dict[str, Any]) -> dict[str, Any]:
        room_id = parse_room_reference(payload.get("roomId", payload.get("url", "")))
        with self._state_lock:
            self._state["rooms"] = [room for room in self._state["rooms"] if int(room["id"]) != room_id]
        self._reconcile_sessions()
        self._save_state()
        event("room", {"rooms": self.get_rooms({})["rooms"]})
        return self.get_rooms({})

    def set_room_enabled(self, payload: dict[str, Any]) -> dict[str, Any]:
        room_id = parse_room_reference(payload.get("roomId", ""))
        enabled = bool(payload.get("enabled", True))
        with self._state_lock:
            room = next((item for item in self._state["rooms"] if int(item["id"]) == room_id), None)
            if room is None:
                raise ValueError("找不到指定直播间")
            room["enabled"] = enabled
        self._reconcile_sessions()
        self._save_state()
        event("room", {"rooms": self.get_rooms({})["rooms"]})
        return self.get_rooms({})

    def save_settings(self, payload: dict[str, Any]) -> dict[str, Any]:
        incoming = payload.get("settings", payload)
        if not isinstance(incoming, dict):
            raise ValueError("settings 必须是对象")
        with self._state_lock:
            settings = self._state["settings"]
            if "enabled" in incoming:
                settings["enabled"] = bool(incoming["enabled"])
            if "autoRestore" in incoming:
                settings["autoRestore"] = bool(incoming["autoRestore"])
            if "autoResumeDrops" in incoming:
                settings["autoResumeDrops"] = bool(incoming["autoResumeDrops"])
            if "watchMode" in incoming:
                settings["watchMode"] = "multi" if str(incoming["watchMode"]).lower() in {"multi", "multithread", "多线程加速"} else "standard"
            if "sessionsPerRoom" in incoming or "threadCount" in incoming:
                requested = incoming.get("sessionsPerRoom", incoming.get("threadCount"))
                settings["sessionsPerRoom"] = positive_int(requested, "每房间并发 Session", maximum=MAX_SESSIONS_PER_ROOM)
            if settings["watchMode"] == "standard":
                settings["sessionsPerRoom"] = 1
            if "reconnectDelay" in incoming or "reconnect_delay_seconds" in incoming:
                settings["reconnectDelay"] = positive_int(incoming.get("reconnectDelay", incoming.get("reconnect_delay_seconds")), "重连延迟")
            if "taskInterval" in incoming or "task_query_interval_seconds" in incoming:
                settings["taskInterval"] = positive_int(incoming.get("taskInterval", incoming.get("task_query_interval_seconds")), "任务查询间隔")
                settings["taskInterval"] = max(MIN_TASK_INTERVAL_SECONDS, settings["taskInterval"])
            if "autoTaskProgress" in incoming:
                settings["autoTaskProgress"] = bool(incoming["autoTaskProgress"])
            if "autoClaim" in incoming:
                settings["autoClaim"] = bool(incoming["autoClaim"])
            if "taskNotifications" in incoming:
                settings["taskNotifications"] = bool(incoming["taskNotifications"])
            if "autoDiscover" in incoming:
                settings["autoDiscover"] = bool(incoming["autoDiscover"])
            if "reconnectEnabled" in incoming:
                settings["reconnectEnabled"] = bool(incoming["reconnectEnabled"])
            if "taskIds" in incoming:
                task_ids = incoming["taskIds"]
                if isinstance(task_ids, str):
                    task_ids = re.split(r"[\s,]+", task_ids)
                settings_task_ids = [str(item).strip() for item in task_ids if str(item).strip()]
                self._state["taskIds"] = list(dict.fromkeys(settings_task_ids))
            notify_urls = incoming.get("notifyUrls")
        if notify_urls is not None:
            urls = [str(url).strip() for url in notify_urls if str(url).strip()] if isinstance(notify_urls, list) else []
            write_protected(self._notifier_path, json.dumps(urls, ensure_ascii=False))
            self._notifier.update_urls(urls)
            with self._state_lock:
                self._state["settings"]["notifyUrlsConfigured"] = bool(urls)
        self._reconcile_sessions()
        self._save_state()
        event("status", {"settings": self._settings_state(), "networkMode": "DIRECT"})
        return self.load_state({})

    def _reconcile_sessions(self) -> None:
        with self._state_lock:
            rooms = [int(room["id"]) for room in self._state["rooms"] if room.get("enabled", True)]
            sessions = int(self._state["settings"].get("sessionsPerRoom", 1))
        if self.running:
            self._pool.reconcile(rooms, sessions, self.uid)

    def discover(self, payload: dict[str, Any]) -> dict[str, Any]:
        self.require_runtime()
        with self._state_lock:
            rooms = json.loads(json.dumps(self._state["rooms"], ensure_ascii=False))
        selected = []
        requested = payload.get("roomId")
        requested_id = parse_room_reference(requested) if requested else None
        for room in rooms:
            if requested_id is not None and int(room["id"]) != requested_id:
                continue
            if requested_id is None and not room.get("enabled", True):
                continue
            selected.append(room)
        if requested_id is not None and not selected:
            selected = [room_record(requested_id)]
        if not selected:
            raise ValueError("请先添加或指定直播间")
        room_statuses: dict[int, int] = {}
        try:
            room_statuses = asyncio.run(self._fetch_room_statuses([int(room["id"]) for room in selected]))
        except Exception as exc:
            # Task discovery remains useful when the optional room-status API
            # is temporarily unavailable.  The static page parser below is
            # still the source of activity/task discovery.
            self.logger.debug("直播间状态读取失败：%s", safe_message(exc))
        activities: list[dict[str, Any]] = []
        discovered_ids: list[str] = []
        errors: list[str] = []
        for room in selected:
            room_id = int(room["id"])
            try:
                groups = fetch_live_task_groups(room_id)
                for index, group in enumerate(groups, start=1):
                    ids = [str(item).strip() for item in group.get("task_ids", []) if str(item).strip()]
                    if not ids:
                        continue
                    discovered_ids.extend(ids)
                    activities.append({
                        "id": f"{room_id}-{index}",
                        "roomId": room_id,
                        "name": safe_message(group.get("label", f"任务组 {index}")),
                        "taskIds": ids,
                        "active": bool(group.get("active", False)),
                        "source": "live_room_static_http",
                    })
            except Exception as exc:
                errors.append(f"房间 {room_id}: {safe_message(exc)}")
        discovered_ids = list(dict.fromkeys(discovered_ids))
        selected_room_ids = {int(item["id"]) for item in selected}
        with self._state_lock:
            self._state["activities"] = activities
            if self._state["settings"].get("autoDiscover", True) and discovered_ids:
                self._state["taskIds"] = discovered_ids
            self._state["lastApiSuccessAt"] = utc_now() if activities or not errors else self._state.get("lastApiSuccessAt", "")
            for room in self._state["rooms"]:
                if int(room["id"]) in selected_room_ids:
                    room["lastError"] = ""
                    if int(room["id"]) in room_statuses:
                        room["liveStatus"] = room_statuses[int(room["id"])]
        self._save_state()
        event("activity", {"activities": activities, "taskIds": discovered_ids, "automatic": True, "errors": errors})
        event("room", {"rooms": self.get_rooms({})["rooms"]})
        self.logger.info("活动发现：rooms=%s groups=%s tasks=%s", len(selected), len(activities), len(discovered_ids))
        return {
            "activities": activities,
            "taskIds": discovered_ids,
            "rooms": self.get_rooms({})["rooms"],
            "message": "已从当前直播间检测到活动" if activities else "未自动发现活动，可手动指定直播间",
            "errors": errors,
        }

    async def _fetch_room_statuses(self, room_ids: list[int]) -> dict[int, int]:
        client = BilibiliClient(self.cookie)
        statuses: dict[int, int] = {}
        try:
            for room_id in room_ids:
                try:
                    info = await client.get_live_room_info(room_id)
                    statuses[room_id] = int(info.live_status)
                except Exception as exc:
                    self.logger.debug("直播间状态读取失败 room=%s：%s", room_id, safe_message(exc))
        finally:
            await client.close()
        return statuses

    async def probe_account(self) -> tuple[int | None, str]:
        if not self.cookie:
            return None, ""
        client = BilibiliClient(self.cookie)
        try:
            return await client.get_self_info()
        finally:
            await client.close()

    def start(self, payload: dict[str, Any]) -> dict[str, Any]:
        self.require_runtime()
        if not self.cookie:
            raise ValueError("请先扫码登录 Bilibili")
        rooms = self.get_rooms({})["rooms"]
        enabled_rooms = [room for room in rooms if room.get("enabled", True)]
        if not enabled_rooms:
            raise ValueError("请先添加并启用至少一个直播间")
        uid, uname = asyncio.run(self.probe_account())
        if uid is None:
            self.invalidate_login("Bilibili 登录已失效，请重新扫码登录")
            raise ValueError("Bilibili 登录已失效")
        with self._state_lock:
            self._uid, self._uname = uid, uname
            self._state["settings"]["enabled"] = True
            if payload.get("automatic") or payload.get("retryAttempt"):
                self._state["lastRecoveryAt"] = utc_now()
        self.running = True
        self._pool.revive()
        self._reconcile_sessions()
        self._progress_poller.start()
        self._login_watchdog.start()
        self._save_state()
        self._emit_status()
        event("account", self._account_state())
        event("recovery", {"state": "Connected", "message": "Bilibili Drops 已开始"})
        self.logger.info("启动 Drops：rooms=%s sessionsPerRoom=%s", len(enabled_rooms), self._settings_state()["sessionsPerRoom"])
        return self.load_state({})

    def stop(self, payload: dict[str, Any]) -> dict[str, Any]:
        self.running = False
        self._progress_poller.stop()
        self._login_watchdog.stop()
        self._pool.stop()
        with self._state_lock:
            self._state["settings"]["enabled"] = False
        self._save_state()
        self._emit_status()
        event("recovery", {"state": "Stopped", "message": "Bilibili Drops 已停止"})
        self.logger.info("停止 Drops：所有 Session 已请求 graceful shutdown")
        return self.load_state({})

    def refresh(self, payload: dict[str, Any]) -> dict[str, Any]:
        self.require_runtime()
        if payload.get("discover", True):
            try:
                self.discover(payload)
            except Exception as exc:
                self.report_warning("discovery_failed", "活动发现失败", exc)
        if self.cookie and self._state.get("taskIds"):
            try:
                asyncio.run(self.poll_progress_once())
            except Exception as exc:
                self.report_warning("task_progress_failed", "任务进度查询失败", exc)
        self._save_state()
        return self.load_state({})

    async def poll_progress_once(self) -> None:
        with self._state_lock:
            task_ids = list(self._state.get("taskIds", []))
        if not task_ids or not self.cookie:
            return
        client = BilibiliClient(self.cookie)
        try:
            progresses = await client.get_task_progress(task_ids)
            completed_to_notify: list[TaskProgress] = []
            with self._state_lock:
                self._state["tasks"] = [task_record(item, self._claimed_ids) for item in progresses]
                self._state["lastProgressAt"] = utc_now()
                self._state["lastApiSuccessAt"] = self._state["lastProgressAt"]
                for item in progresses:
                    task_id = str(item.task_id).strip()
                    if item.is_completed and task_id and task_id not in self._notified_completed:
                        self._notified_completed.add(task_id)
                        completed_to_notify.append(item)
            event("task", {"tasks": self.get_tasks({}), "official": True})
            event("progress", {
                "tasks": self.get_tasks({}),
                "official": True,
                "lastProgressAt": self._state["lastProgressAt"],
                "lastApiSuccessAt": self._state["lastApiSuccessAt"],
            })
            self.logger.info("任务进度变化：%s 个任务（官方接口）", len(progresses))
            if self._settings_state().get("taskNotifications", True):
                for item in completed_to_notify:
                    await asyncio.to_thread(
                        self._notify,
                        "Bilibili 任务完成",
                        f"{safe_message(item.task_name)} 已达到官方任务进度",
                    )
            if completed_to_notify:
                self._save_state()
            if self._settings_state().get("autoClaim", False):
                completed = [item.task_id for item in progresses if item.is_completed]
                if completed:
                    await self._claim_with_client(client, completed, automatic=True)
        finally:
            await client.close()

    async def _claim_with_client(self, client: BilibiliClient, task_ids: list[str], *, automatic: bool) -> list[dict[str, Any]]:
        results: list[dict[str, Any]] = []
        for task_id in dict.fromkeys(str(item).strip() for item in task_ids if str(item).strip()):
            with self._claim_lock:
                if task_id in self._claimed_ids or task_id in self._claim_inflight:
                    continue
                self._claim_inflight.add(task_id)
            try:
                claim_results: list[MissionRewardClaimResult] = await client.receive_all_mission_rewards([task_id])
                for result in claim_results:
                    item = self._claim_result_record(result)
                    results.append(item)
                    if result.success:
                        with self._claim_lock:
                            self._claimed_ids.add(task_id)
                            self._claimed_ids.add(str(result.task_id).strip())
                    event("reward", item)
                    if result.success:
                        self.logger.info("奖励领取成功：task=%s", safe_message(result.task_id))
                        if self._settings_state().get("taskNotifications", True):
                            await asyncio.to_thread(
                                self._notify,
                                "Bilibili 奖励领取成功",
                                f"{safe_message(result.task_name)}：{safe_message(result.reward_name) or '奖励已领取'}",
                            )
                    else:
                        self.logger.warning("奖励领取失败：task=%s reason=%s", safe_message(result.task_id), safe_message(result.message))
            finally:
                with self._claim_lock:
                    self._claim_inflight.discard(task_id)
        if results:
            with self._state_lock:
                rewards = self._state.setdefault("rewards", [])
                for item in results:
                    existing = next((row for row in rewards if row.get("taskId") == item.get("taskId")), None)
                    if existing is None:
                        rewards.append(item)
                    else:
                        existing.update(item)
                self._state["tasks"] = [
                    dict(item,
                         claimed=bool(item.get("claimed", False) or item["id"] in self._claimed_ids),
                         claimable=bool(item.get("claimable", False) and item["id"] not in self._claimed_ids),
                         status="已领取" if item["id"] in self._claimed_ids else item.get("status", "进行中"))
                    for item in self._state.get("tasks", [])
                ]
            self._save_state()
        return results

    @staticmethod
    def _claim_result_record(result: MissionRewardClaimResult) -> dict[str, Any]:
        return {
            "taskId": safe_message(result.task_id),
            "taskName": safe_message(result.task_name),
            "reward": safe_message(result.reward_name),
            "statusCode": result.status,
            "message": safe_message(result.message),
            "success": bool(result.success),
            "skipped": bool(result.skipped),
            "claimedAt": utc_now() if result.success else "",
            "state": "已领取" if result.success else "领取失败",
        }

    def _notify(self, title: str, body: str) -> None:
        try:
            if self._notifier.enabled:
                self._notifier.notify(safe_message(title), safe_message(body))
        except Exception as exc:
            self.logger.warning("第三方通知发送失败：%s", safe_message(exc))

    def claim_reward(self, payload: dict[str, Any]) -> dict[str, Any]:
        task_id = str(payload.get("taskId", payload.get("id", ""))).strip()
        if not task_id:
            raise ValueError("taskId 不能为空")
        self.require_runtime()
        if not self.cookie:
            raise ValueError("请先扫码登录 Bilibili")
        client = BilibiliClient(self.cookie)
        try:
            results = asyncio.run(self._claim_with_client(client, [task_id], automatic=False))
        finally:
            asyncio.run(client.close())
        return {"results": results, "tasks": self.get_tasks({})}

    def get_session_details(self, payload: dict[str, Any]) -> dict[str, Any]:
        return self._pool.snapshot()

    def invalidate_login(self, message: str) -> None:
        self.stop({})
        with self._state_lock:
            self._uid = 0
            self._uname = ""
            self._state["lastError"] = safe_message(message)
        self._save_state()
        event("account", {"loggedIn": False, "uid": 0, "userName": ""})
        event("error", {"code": "login_expired", "message": safe_message(message), "retryable": False})
        self.logger.warning("登录状态变化：已失效")


def create_worker(data_dir: Path, log_file: Path) -> BilibiliWorker:
    return BilibiliWorker(data_dir, log_file)


if __name__ == "__main__":
    raise SystemExit(run_worker(create_worker))
