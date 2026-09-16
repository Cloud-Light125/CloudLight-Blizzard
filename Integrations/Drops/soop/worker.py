from __future__ import annotations

import asyncio
import dataclasses
import importlib
import importlib.util
import json
import logging
import math
import os
import re
import sys
import threading
import time
from datetime import datetime, timedelta, timezone
from pathlib import Path
from typing import Any

try:
    from zoneinfo import ZoneInfo
except ImportError:  # Python 3.8 compatibility for source-level contract tests
    ZoneInfo = None  # type: ignore[assignment,misc]

sys.path.insert(0, str(Path(__file__).resolve().parents[1]))
from Shared.protocol import (
    TransientNetworkError,
    WorkerBase,
    atomic_write_json,
    event,
    read_json,
    run_worker,
)
from credentials import read_credentials, write_credentials


DEFAULT_SETTINGS: dict[str, Any] = {
    "settings_version": 5,
    "auto_claim_enabled": False,
    "low_bandwidth_mode": True,
    "proxy_enabled": False,
    "proxy_url": "",
    "proxy_fallback_direct": False,
    "auto_start_enabled": False,
    "start_minimized_to_tray": False,
    "close_to_tray": True,
    "appearance_mode": "system",
    "mission_poll_interval": 90,
    "inventory_poll_interval": 300,
    "channel_refresh_interval": 300,
    "reauth_retry_interval": 300,
    "channel_mode": "smart",
    "manual_input": "",
    "preferred_bjid": "owesports",
    "priority_mission_id": "auto",
    "hang_without_missions": True,
    "primary_account_uid": "",
}


try:
    if ZoneInfo is None:
        raise KeyError("zoneinfo unavailable")
    SOOP_TIMEZONE = ZoneInfo("Asia/Seoul")
except KeyError:  # pragma: no cover - minimal Python images can omit the IANA database
    # Korea has no daylight-saving transitions. Keep SOOP wall-clock
    # semantics explicit even when the host does not provide tzdata.
    SOOP_TIMEZONE = timezone(timedelta(hours=9), "Asia/Seoul")


_START_TIME_FIELDS = ("startTime", "startDate", "beginTime", "openTime")
_END_TIME_FIELDS = ("endTime", "endDate", "expireTime", "closeTime")
_TIME_FIELDS = _START_TIME_FIELDS + _END_TIME_FIELDS


def _soop_now(value: datetime | None = None) -> datetime:
    if value is None:
        return datetime.now(SOOP_TIMEZONE)
    if value.tzinfo is None:
        return value.replace(tzinfo=SOOP_TIMEZONE)
    return value.astimezone(SOOP_TIMEZONE)


def _parse_soop_datetime(value: Any) -> datetime | None:
    """Parse a SOOP timestamp without consulting the machine-local timezone."""
    if value is None or isinstance(value, bool):
        return None
    if isinstance(value, datetime):
        return _soop_now(value)
    if isinstance(value, (int, float)):
        numeric = float(value)
        if not math.isfinite(numeric):
            return None
        seconds = numeric / 1000 if abs(numeric) >= 100_000_000_000 else numeric
        try:
            return datetime.fromtimestamp(seconds, timezone.utc).astimezone(SOOP_TIMEZONE)
        except (OverflowError, OSError, ValueError):
            return None

    text = str(value).strip()
    if not text or text.casefold() in {"none", "null"}:
        return None
    if re.fullmatch(r"[+-]?\d{9,17}(?:\.\d+)?", text):
        return _parse_soop_datetime(float(text))
    normalized = text[:-1] + "+00:00" if text.endswith(("Z", "z")) else text
    try:
        parsed = datetime.fromisoformat(normalized)
    except ValueError:
        parsed = None
        for fmt in (
            "%Y-%m-%d %H:%M:%S",
            "%Y-%m-%d %H:%M",
            "%Y-%m-%d",
            "%Y/%m/%d",
            "%Y.%m.%d",
        ):
            try:
                parsed = datetime.strptime(text, fmt)
                break
            except ValueError:
                continue
    return _soop_now(parsed) if parsed is not None else None


def _is_date_only(value: Any) -> bool:
    return bool(re.fullmatch(r"\d{4}[-/.]\d{1,2}[-/.]\d{1,2}", str(value or "").strip()))


def _task_time_value(task: dict[str, Any], names: tuple[str, ...]) -> Any:
    raw = task.get("raw") if isinstance(task.get("raw"), dict) else {}
    for owner in (task, raw):
        for name in names:
            value = owner.get(name)
            if value is not None and str(value).strip() and str(value).strip().casefold() not in {"none", "null"}:
                return value
    return None


def _api_bool(value: Any) -> bool:
    if isinstance(value, bool):
        return value
    if isinstance(value, (int, float)):
        return value != 0
    if isinstance(value, str):
        return value.strip().casefold() in {"1", "true", "yes", "y", "on"}
    return bool(value)


def normalize_task_status(
    task: dict[str, Any],
    *,
    now: datetime | None = None,
) -> dict[str, Any]:
    """Return one canonical SOOP lifecycle for UI, selection, and execution guards.

    SOOP's ``active``/``live``-style flags are not authoritative when a time
    window is present. Naive API timestamps are Korean wall-clock values. The
    raw flags are used only as a compatibility fallback when no usable time
    boundary exists.
    """
    normalized = dict(task)
    completed = _api_bool(task.get("completed"))
    start_value = _task_time_value(task, _START_TIME_FIELDS)
    end_value = _task_time_value(task, _END_TIME_FIELDS)
    start_at = _parse_soop_datetime(start_value)
    end_at = _parse_soop_datetime(end_value)
    if end_at is not None and _is_date_only(end_value):
        end_at += timedelta(days=1)
    current = _soop_now(now)

    if completed:
        lifecycle = "completed"
    elif end_at is not None and current >= end_at:
        lifecycle = "ended"
    elif start_at is not None and current < start_at:
        lifecycle = "notYetOpen"
    elif start_at is not None or end_at is not None:
        lifecycle = "active"
    elif _api_bool(task.get("ended")):
        lifecycle = "ended"
    elif _api_bool(task.get("active")):
        lifecycle = "active"
    elif _api_bool(task.get("notYetOpen")):
        lifecycle = "notYetOpen"
    else:
        lifecycle = "unknown"

    normalized.update({
        "completed": lifecycle == "completed",
        "ended": lifecycle == "ended",
        "active": lifecycle == "active",
        "notYetOpen": lifecycle == "notYetOpen",
        "lifecycle": lifecycle,
    })
    return normalized


def _source_time_fields(source: Any) -> dict[str, Any]:
    raw = getattr(source, "raw", {}) or {}
    values = {
        "startDate": getattr(source, "start_date", ""),
        "endDate": getattr(source, "end_date", ""),
    }
    if isinstance(raw, dict):
        for name in _TIME_FIELDS:
            value = raw.get(name)
            if value is not None and str(value).strip():
                values[name] = value
    return values


def is_actionable_task(task: dict[str, Any], *, allow_upcoming: bool = False) -> bool:
    """Shared Worker task policy for display selection and execution inputs."""
    normalized = normalize_task_status(task)
    if _api_bool(normalized.get("eventOnly")) or normalized["ended"] or normalized["completed"]:
        return False
    if normalized["active"]:
        return True
    return allow_upcoming and normalized["notYetOpen"]


class SoopWorker(WorkerBase):
    platform = "soop"

    def __init__(self, data_dir: Path, log_file: Path) -> None:
        super().__init__(data_dir, log_file)
        self.settings_path = self.data_dir / "settings.json"
        self.accounts_dir = self.data_dir / "accounts"
        self.accounts_dir.mkdir(parents=True, exist_ok=True)
        self.settings = read_json(self.settings_path, DEFAULT_SETTINGS)
        self._core_error = ""
        self._core: dict[str, Any] = {}
        self._manager: Any = None
        self._states: dict[str, Any] = {}
        self._state_changed = threading.Condition()
        self._auto_start_uid = ""
        self._reauth_guard = threading.Lock()
        self._reauth_cancel: dict[str, threading.Event] = {}
        self._reauth_threads: dict[str, threading.Thread] = {}
        self._reauth_last_attempt: dict[str, float] = {}
        self._loop = asyncio.new_event_loop()
        self._loop_thread = threading.Thread(target=self._run_loop, name="soop-asyncio", daemon=True)
        self._loop_thread.start()
        if self.runtime_available:
            self._load_core()
        else:
            self._core_error = self.runtime_error.public_message if self.runtime_error else ""
        self.commands.update({
            "add_account": self.add_account,
            "delete_account": self.delete_account,
            "start_account": self.start_account,
            "stop_account": self.stop_account,
            "get_channels": self.get_channels,
            "set_channel": self.set_channel,
            "claim_reward": self.claim_reward,
            "copy_redeem_code": self.copy_redeem_code,
            "set_primary_account": self.set_primary_account,
            "auto_start": self.auto_start,
        })
        self._migrate_legacy_proxy()

    def _run_loop(self) -> None:
        asyncio.set_event_loop(self._loop)
        self._loop.run_forever()

    @staticmethod
    def _core_candidates() -> list[Path]:
        candidates: list[Path] = []
        configured = os.environ.get("CLOUDLIGHT_SOOP_CORE", "").strip()
        if configured:
            candidates.append(Path(configured))
        here = Path(__file__).resolve()
        candidates.extend([
            here.with_name("core"),
            here.parents[4] / "cloudlight soop drops miner",
        ])
        if getattr(sys, "_MEIPASS", None):
            candidates.insert(0, Path(getattr(sys, "_MEIPASS")) / "core")
        return candidates

    def _load_core(self) -> None:
        root = next((path.resolve() for path in self._core_candidates() if (path / "__init__.py").is_file()), None)
        if root is None:
            self._core_error = "未找到 SOOP 功能组件，请重新安装当前版本。"
            self.logger.warning(self._core_error)
            return
        try:
            spec = importlib.util.spec_from_file_location(
                "cloudlight_soop_core", root / "__init__.py", submodule_search_locations=[str(root)]
            )
            if spec is None or spec.loader is None:
                raise ImportError("无法加载 SOOP 功能组件")
            package = importlib.util.module_from_spec(spec)
            sys.modules["cloudlight_soop_core"] = package
            spec.loader.exec_module(package)
            constants = importlib.import_module("cloudlight_soop_core.constants")
            constants.DATA_DIR = self.data_dir
            constants.ACCOUNTS_DIR = self.accounts_dir
            constants.COOKIES_PATH = self.data_dir / "cookies.json"
            constants.DISCLAIMER_ACCEPTED_PATH = self.data_dir / ".disclaimer_accepted"
            config = importlib.import_module("cloudlight_soop_core.config")
            config.SETTINGS_PATH = self.settings_path
            config.CONFIG_PATH = self.settings_path
            config.LEGACY_CONFIG_PATH = self.data_dir / "config.json"
            auth = importlib.import_module("cloudlight_soop_core.auth")
            auth.ACCOUNTS_DIR = self.accounts_dir
            auth.COOKIES_PATH = self.data_dir / "cookies.json"
            channel = importlib.import_module("cloudlight_soop_core.channel")
            drops = importlib.import_module("cloudlight_soop_core.drops")
            multi = importlib.import_module("cloudlight_soop_core.multi_miner")
            miner = importlib.import_module("cloudlight_soop_core.miner")
            network = importlib.import_module("cloudlight_soop_core.network")
            self._core = {
                "config": config, "auth": auth, "channel": channel,
                "drops": drops, "multi": multi, "miner": miner, "network": network,
            }
            root_logger = logging.getLogger()
            root_logger.setLevel(logging.INFO)
            if self.logger.handlers and self.logger.handlers[0] not in root_logger.handlers:
                root_logger.addHandler(self.logger.handlers[0])
            self.logger.info("SOOP integration loaded from %s", root)
        except Exception as exc:
            self._core_error = f"SOOP 功能组件加载失败: {exc}"
            self.logger.exception(self._core_error)

    def _require_core(self) -> None:
        self.require_runtime()
        if not self._core:
            raise RuntimeError(self._core_error or "SOOP 功能组件不可用")

    def _migrate_legacy_proxy(self) -> None:
        if self.settings.get("proxy_enabled") and self.settings.get("proxy_url"):
            event("legacy_proxy", {
                "enabled": True,
                "url": str(self.settings.get("proxy_url", "")),
                "fallbackDirect": bool(self.settings.get("proxy_fallback_direct", False)),
            })

    def _effective_settings(self) -> dict[str, Any]:
        value = dict(self.settings)
        value["proxy_enabled"] = bool(self.proxy["enableProxy"])
        value["proxy_url"] = str(self.proxy["proxyUrl"])
        value["proxy_fallback_direct"] = bool(self.proxy["fallbackDirect"])
        return value

    def _channel_config(self) -> Any:
        channel = self._core["channel"]
        return channel.ChannelConfig(
            mode=str(self.settings.get("channel_mode", "smart")),
            manual_input=str(self.settings.get("manual_input", "")),
            preferred_bjid=str(self.settings.get("preferred_bjid", "owesports")),
            priority_mission_id=str(self.settings.get("priority_mission_id", "auto")),
            hang_without_missions=bool(self.settings.get("hang_without_missions", True)),
        )

    def _app_config(self) -> Any:
        config = self._core["config"]
        return config.AppConfig.from_dict(self._effective_settings())

    def _submit(self, coroutine: Any, timeout: float = 35.0) -> Any:
        return asyncio.run_coroutine_threadsafe(coroutine, self._loop).result(timeout=timeout)

    def _credentials_path(self, uid: str) -> Path:
        return self.accounts_dir / uid / "credentials.dpapi"

    def _has_saved_credentials(self, uid: str) -> bool:
        return self._credentials_path(uid).is_file()

    @staticmethod
    def _is_authentication_error(exc: BaseException) -> bool:
        current: BaseException | None = exc
        visited: set[int] = set()
        while current is not None and id(current) not in visited:
            visited.add(id(current))
            name = current.__class__.__name__.casefold()
            message = str(current).casefold()
            if "authentication" in name or any(marker in message for marker in (
                "drops api 认证失败", "로그인 후 가능", "登录已失效", "not logged in",
            )):
                return True
            current = current.__cause__ or current.__context__
        return False

    @staticmethod
    def _is_auth_expired_state(state: Any) -> bool:
        return "登录已失效" in str(getattr(state, "status", ""))

    def _cancel_reauthentication(self, uid: str, *, wait: bool = False) -> bool:
        thread: threading.Thread | None
        with self._reauth_guard:
            cancel = self._reauth_cancel.get(uid)
            thread = self._reauth_threads.get(uid)
            if cancel is not None:
                cancel.set()
        if wait and thread is not None and thread.is_alive() and thread is not threading.current_thread():
            thread.join(timeout=160)
        return thread is None or not thread.is_alive()

    def _cancel_all_reauthentication(self) -> None:
        with self._reauth_guard:
            for cancel in self._reauth_cancel.values():
                cancel.set()

    def _allow_reauthentication(self, uid: str) -> None:
        with self._reauth_guard:
            current = self._reauth_threads.get(uid)
            if current is None or not current.is_alive():
                self._reauth_cancel.pop(uid, None)

    def _publish_reauthentication_status(self, uid: str, status: str) -> None:
        state = self._states.get(uid)
        if state is not None:
            state.running = False
            state.status = status
            event("account_status", self._state_to_dict(state))
        self.status(status, uid)

    async def _reauthenticate_once(
        self,
        uid: str,
        password: str,
        restart: bool,
        cancel: threading.Event,
    ) -> None:
        manager = self._manager
        if manager is not None:
            await manager.stop_account_and_wait(uid)
        if cancel.is_set() or self.shutdown_requested:
            return
        cookies = await self._core["auth"].login(uid, password, config=self._app_config())
        if cancel.is_set() or self.shutdown_requested:
            return
        # The login endpoint returning cookies is not sufficient: validate the
        # new Session against the Drops API before considering recovery done.
        refreshed = await self._refresh_saved_account(cookies)
        self._state_callback(refreshed)
        if restart and not cancel.is_set() and not self.shutdown_requested:
            if self._manager is None:
                self._manager = self._core["multi"].MultiMinerManager(
                    on_state=self._state_callback,
                    channel_config=self._channel_config(),
                    app_config=self._app_config(),
                )
            await self._manager.start_account(cookies)

    def _schedule_reauthentication(self, uid: str, *, restart: bool) -> bool:
        with self._reauth_guard:
            current = self._reauth_threads.get(uid)
            current_cancel = self._reauth_cancel.get(uid)
            if current is not None and current.is_alive() and current_cancel is not None and not current_cancel.is_set():
                return True
            cancel = threading.Event()
            self._reauth_cancel[uid] = cancel
            thread = threading.Thread(
                target=self._reauthentication_loop,
                args=(uid, restart, cancel),
                name=f"soop-reauth-{uid}",
                daemon=True,
            )
            self._reauth_threads[uid] = thread
            thread.start()
        return True

    def _reauthentication_loop(self, uid: str, restart: bool, cancel: threading.Event) -> None:
        try:
            credentials = read_credentials(self._credentials_path(uid))
        except Exception as exc:
            self.logger.warning("[%s] 无法读取已保存的 SOOP 登录凭据: %s", uid, exc)
            self._publish_reauthentication_status(uid, "自动重新登录不可用，请重新添加账号")
            return
        if credentials is None or credentials[0] != uid:
            self.logger.warning("[%s] SOOP Session 已失效，但没有可用的加密登录凭据。", uid)
            self._publish_reauthentication_status(uid, "未保存登录密码，请重新添加账号")
            return

        _, password = credentials
        attempt = 0
        while not cancel.is_set() and not self.shutdown_requested:
            interval = int(self.settings.get("reauth_retry_interval", 300))
            last_attempt = self._reauth_last_attempt.get(uid, 0.0)
            remaining = max(0.0, interval - (time.monotonic() - last_attempt))
            if remaining and cancel.wait(remaining):
                return
            attempt += 1
            self._reauth_last_attempt[uid] = time.monotonic()
            self.logger.info("[%s] SOOP Session 已失效，正在进行第 %d 次自动重新登录。", uid, attempt)
            self._publish_reauthentication_status(uid, "正在自动重新登录")
            try:
                self._submit(self._reauthenticate_once(uid, password, restart, cancel), timeout=150)
                if cancel.is_set() or self.shutdown_requested:
                    return
                if restart:
                    self.running = True
                self.logger.info("[%s] SOOP 自动重新登录成功。", uid)
                event("account_reauthenticated", {"userid": uid, "restarted": restart})
                return
            except Exception as exc:
                if cancel.is_set() or self.shutdown_requested:
                    return
                self.logger.warning("[%s] SOOP 自动重新登录失败，将在 %d 秒后重试: %s", uid, interval, exc)
                self._publish_reauthentication_status(uid, f"自动重新登录失败，{interval} 秒后重试")

    def _handle_authentication_failure(self, uid: str, *, restart: bool) -> bool:
        if not self._has_saved_credentials(uid):
            self._publish_reauthentication_status(uid, "未保存登录密码，请重新添加账号")
            return False
        self._schedule_reauthentication(uid, restart=restart)
        return True

    @staticmethod
    def _is_transient_network_error(exc: BaseException) -> bool:
        current: BaseException | None = exc
        visited: set[int] = set()
        while current is not None and id(current) not in visited:
            visited.add(id(current))
            name = current.__class__.__name__.casefold()
            message = str(current).casefold()
            if (
                "timeout" in name
                or any(marker in name for marker in (
                    "connectionerror", "clientconnection", "clientconnector", "gaierror"
                ))
                or any(marker in message for marker in (
                    "connection refused", "connection reset", "connection aborted",
                    "temporary failure", "temporarily unavailable", "getaddrinfo",
                    "name or service not known", "proxy connection", "websocket",
                    "cannot connect", "network is unreachable", "server disconnected",
                ))
            ):
                return True
            current = current.__cause__ or current.__context__
        return False

    def _normalize_priority_mission_id(self, *, fallback_if_missing: bool) -> bool:
        """Keep a saved priority only while it remains selectable.

        An upcoming incomplete mission is valid saved configuration even
        though it is not an execution candidate yet.  A completed/ended
        mission is never valid, and refreshes may clear a missing ID once the
        complete snapshot is available.
        """
        selected = str(self.settings.get("priority_mission_id", "auto")).strip() or "auto"
        if selected == "auto":
            return False

        tasks = [
            task
            for state in self._states.values()
            for task in self._tasks_for_state(state, include_ended=True)
            if str(task.get("id", "")) == selected and not bool(task.get("eventOnly"))
        ]
        if not tasks and not fallback_if_missing:
            return False
        if any(is_actionable_task(task, allow_upcoming=True) for task in tasks):
            return False

        self.settings["priority_mission_id"] = "auto"
        atomic_write_json(self.settings_path, self.settings)
        return True

    def _state_callback(self, state: Any) -> None:
        with self._state_changed:
            self._states[state.uid] = state
            self._state_changed.notify_all()
        self._normalize_priority_mission_id(fallback_if_missing=False)
        event("account_status", self._state_to_dict(state))
        active = sum(1 for item in self._states.values() if item.running)
        if state.uid == self._auto_start_uid:
            self.status("正在刷新掉宝信息…", "正在读取直播间、任务与奖励背包")
        else:
            self.status("运行中" if active else "已停止", f"{active}/{len(self.get_accounts({}))} 个账号运行")
        if self._is_auth_expired_state(state):
            self._handle_authentication_failure(str(state.uid), restart=True)

    @staticmethod
    def _mission_is_completed(mission: Any, items: list[Any] | None = None) -> bool:
        reward_items = items if items is not None else list(getattr(mission, "items", []))
        completed = bool(getattr(mission, "completed", getattr(mission, "is_completed", False)))
        return completed or (bool(reward_items) and all(item.mission_success for item in reward_items))

    @staticmethod
    def _mission_to_dict(mission: Any) -> dict[str, Any]:
        active = bool(getattr(mission, "is_event_active", False))
        ended = bool(getattr(mission, "is_truly_ended", not active))
        not_yet_open = bool(getattr(mission, "is_not_yet_open", False))
        items = list(getattr(mission, "items", []))
        completed = SoopWorker._mission_is_completed(mission, items)
        row = {
            "id": mission.drops_idx, "title": mission.title, "type": mission.type_label,
            "categoryName": mission.category_name, "categoryNo": mission.category_no,
            "active": active, "ended": ended, "notYetOpen": not_yet_open,
            "completed": completed,
            "source": "mission", "eventOnly": False,
            "items": [
                {"name": item.item_name, "requiredMinutes": item.give_term, "viewMinutes": item.view_time,
                 "percent": item.percent, "completed": item.mission_success}
                for item in items
            ],
        }
        row.update(_source_time_fields(mission))
        return normalize_task_status(row)

    @staticmethod
    def _event_to_dict(activity: Any) -> dict[str, Any]:
        raw = getattr(activity, "raw", {}) or {}
        active = bool(getattr(activity, "is_event_active", False))
        ended = bool(getattr(activity, "is_truly_ended", not active))
        not_yet_open = bool(getattr(activity, "is_not_yet_open", False))
        row = {
            "id": activity.drops_idx, "title": activity.title, "type": activity.type_label,
            "categoryName": str(raw.get("cateName") or raw.get("categoryName") or ""),
            "categoryNo": str(raw.get("cateNo") or raw.get("categoryNo") or ""),
            "active": active, "ended": ended, "notYetOpen": not_yet_open,
            "completed": False,
            "source": "event", "eventOnly": True, "joined": bool(getattr(activity, "acct_conn", False)),
            "items": [],
        }
        row.update(_source_time_fields(activity))
        return normalize_task_status(row)

    @staticmethod
    def _inventory_to_dict(item: Any) -> dict[str, Any]:
        return {
            "id": item.item_code_idx, "name": item.item_name, "claimed": item.claimed,
            "expiresAt": item.exp_date, "receivedAt": item.receive_date,
            "redeemCode": item.redeem_code or "", "description": item.description or "",
        }

    def _current_progress_for_state(self, state: Any) -> list[dict[str, Any]]:
        """Return the tiers that the Core says can progress on the current channel."""
        if not state.running or not state.channel_id or self._manager is None:
            return []
        get_miner = getattr(self._manager, "get_miner", None)
        miner = get_miner(state.uid) if callable(get_miner) else None
        current_channel = getattr(miner, "_current", None)
        if current_channel is None:
            return []

        missions_for_channel = getattr(self._core.get("channel"), "missions_for_channel", None)
        if not callable(missions_for_channel):
            return []

        rows: list[dict[str, Any]] = []
        for mission in missions_for_channel(state.missions, current_channel):
            if self._mission_is_completed(mission):
                continue
            item = mission.active_item()
            if item is None:
                continue
            rows.append({
                "id": f"{state.uid}:{mission.drops_idx}",
                "account": state.uid,
                "channel": state.channel_nick or state.channel_id,
                "campaign": mission.title,
                "reward": item.item_name,
                "currentMinutes": item.view_time,
                "requiredMinutes": item.give_term,
                "percent": max(0, min(100, int(item.percent))),
            })
        return rows

    def _state_to_dict(self, state: Any) -> dict[str, Any]:
        tasks = self._tasks_for_state(state)
        all_tasks = self.get_tasks({})
        return {
            "uid": state.uid, "running": state.running, "status": state.status,
            "primary": state.uid == str(self.settings.get("primary_account_uid", "")),
            "channelId": state.channel_id, "channelName": state.channel_nick, "broadcastNo": state.broad_no,
            "connectionHealthy": state.connection_healthy, "bridgeConnected": state.bridge_connected,
            "heartbeatStatus": state.heartbeat_status, "heartbeatLastSuccess": state.heartbeat_last_success,
            "networkUploaded": state.network_uploaded, "networkDownloaded": state.network_downloaded,
            "networkBps": state.network_last_minute_bps,
            "settings": {"priority_mission_id": str(self.settings.get("priority_mission_id", "auto"))},
            "currentProgress": self._current_progress_for_state(state),
            "missions": [self._mission_to_dict(mission) for mission in state.missions],
            "events": [self._event_to_dict(activity) for activity in getattr(state, "events", [])],
            "tasks": tasks,
            "displayTasks": tasks,
            "selectableTasks": [task for task in tasks if is_actionable_task(task, allow_upcoming=True)],
            "allTasks": all_tasks,
            "allSelectableTasks": [task for task in all_tasks if is_actionable_task(task, allow_upcoming=True)],
            "inventory": [self._inventory_to_dict(item) for item in state.inventory],
            "channels": [
                {"id": channel.user_id, "name": channel.user_nick, "broadcastNo": channel.broad_no,
                 "onAir": channel.on_air, "categories": channel.category_names, "hasDrops": channel.has_drops}
                for channel in state.available_channels
            ],
        }

    def load_state(self, payload: dict[str, Any]) -> dict[str, Any]:
        tasks = self.get_tasks({})
        return {
            "running": self.running,
            "settings": self._effective_settings(),
            "accounts": self.get_accounts({}),
            # ``tasks`` is the backwards-compatible display/history collection.
            # Selection consumers must use the explicit actionable collection.
            "tasks": tasks,
            "displayTasks": tasks,
            "selectableTasks": [task for task in tasks if is_actionable_task(task, allow_upcoming=True)],
            "events": self.get_events({}),
            "inventory": self.get_inventory({}),
            "currentProgress": self.get_current_progress({}),
            "channels": self.get_channels({}),
            "coreAvailable": bool(self._core),
            "coreError": self._core_error,
            "proxy": self.proxy,
            "runtime": self.runtime_state(),
        }

    def save_settings(self, payload: dict[str, Any]) -> dict[str, Any]:
        changes = payload.get("settings", payload)
        if not isinstance(changes, dict):
            raise ValueError("settings 必须是对象")
        allowed = set(DEFAULT_SETTINGS) - {"proxy_enabled", "proxy_url", "proxy_fallback_direct"}
        for key in allowed:
            if key in changes:
                self.settings[key] = changes[key]
        self._validate_settings()
        atomic_write_json(self.settings_path, self.settings)
        self._normalize_priority_mission_id(fallback_if_missing=False)
        if self.running:
            self._restart_manager()
        return self._effective_settings()

    def _validate_settings(self) -> None:
        self.settings["settings_version"] = 5
        self.settings["primary_account_uid"] = str(self.settings.get("primary_account_uid", "")).strip()
        for name, minimum, maximum in (
            ("mission_poll_interval", 30, 600), ("inventory_poll_interval", 60, 1800),
            ("channel_refresh_interval", 60, 1800), ("reauth_retry_interval", 30, 3600),
        ):
            self.settings[name] = max(minimum, min(maximum, int(self.settings.get(name, DEFAULT_SETTINGS[name]))))
        if self.settings.get("channel_mode") not in {"smart", "manual", "owesports"}:
            self.settings["channel_mode"] = "smart"

    def start(self, payload: dict[str, Any]) -> dict[str, Any]:
        self._require_core()
        if self.running:
            return self.load_state({})
        for uid in self._core["auth"].list_accounts():
            self._allow_reauthentication(str(uid))
        self._manager = self._core["multi"].MultiMinerManager(
            on_state=self._state_callback, channel_config=self._channel_config(), app_config=self._app_config()
        )
        started = self._submit(self._manager.start_all())
        self.running = bool(started)
        self.status("运行中" if self.running else "没有可启动的账号", f"{len(started)} 个账号")
        return self.load_state({})

    def stop(self, payload: dict[str, Any]) -> dict[str, Any]:
        self._cancel_all_reauthentication()
        if self._manager is not None:
            try:
                self._submit(self._manager.shutdown(), timeout=20)
            finally:
                self._manager = None
        self.running = False
        self.status("已停止", f"{len(self.get_accounts({}))} 个账号")
        return self.load_state({})

    def _restart_manager(self) -> None:
        self.stop({})
        self.start({})

    def on_proxy_changed(self) -> None:
        if self.running and self._core:
            self._restart_manager()

    def refresh(self, payload: dict[str, Any]) -> dict[str, Any]:
        if self._core:
            accounts = [str(uid) for uid in self._core["auth"].list_accounts()]
        else:
            accounts = sorted(
                path.name for path in self.accounts_dir.iterdir()
                if (path / "cookies.json").is_file()
            )
        if accounts:
            self._require_core()

        self.logger.info("SOOP mission refresh started accounts=%d", len(accounts))
        self.status("正在刷新掉宝信息…", "正在重新获取 SOOP 活动、奖励和直播间")
        errors: list[str] = []
        manager = self._manager
        running_uids = {
            str(uid) for uid in getattr(manager, "running_uids", [])
        } if manager is not None else set()
        get_miner = getattr(manager, "get_miner", None) if manager is not None else None

        for uid in accounts:
            try:
                is_running = uid in running_uids or (
                    callable(get_miner) and get_miner(uid) is not None
                )
                if is_running:
                    force_refresh = getattr(manager, "force_refresh_account", None)
                    if not callable(force_refresh):
                        raise RuntimeError("SOOP Core 不支持运行中账号的即时刷新。")
                    state = self._submit(force_refresh(uid), timeout=120)
                else:
                    cookies = self._core["auth"].load_cookies(uid)
                    if not cookies:
                        raise RuntimeError(f"未找到 SOOP 账号 {uid} 的 Session。")
                    state = self._submit(
                        self._refresh_saved_account(cookies),
                        timeout=120,
                    )
                if state is None:
                    raise RuntimeError("SOOP 账号刷新未返回状态。")
                self._state_callback(state)
            except Exception as exc:
                if self._is_authentication_error(exc):
                    self._handle_authentication_failure(uid, restart=is_running)
                errors.append(f"{uid}: {exc}")
                self.logger.warning("SOOP mission refresh failed uid=%s: %s", uid, exc)

        persisted = set(accounts)
        for uid in list(self._states):
            if uid not in persisted:
                self._states.pop(uid, None)

        state = self.load_state({})
        tasks = state["tasks"]
        if self._normalize_priority_mission_id(fallback_if_missing=True):
            state["settings"] = self._effective_settings()
        channels = state["channels"]
        mission_ids = {
            str(task.get("id", "")) for task in tasks
            if task.get("id") and is_actionable_task(task, allow_upcoming=True)
        }
        event_ids = {str(item.get("id", "")) for item in state["events"] if item.get("id")}
        active_mission_ids = {
            str(task.get("id", "")) for task in tasks
            if task.get("id") and is_actionable_task(task)
        }
        active = len(active_mission_ids)
        self.logger.info(
            "SOOP mission refresh %s accounts=%d missions=%d events=%d active=%d channels=%d",
            "failed" if errors else "completed",
            len(accounts),
            len(mission_ids),
            len(event_ids),
            active,
            len(channels),
        )
        state["refreshStatus"] = "failed" if errors else "success"
        state["refreshCompleted"] = not errors
        if errors:
            state["refreshError"] = "；".join(errors)
            self.status("SOOP 刷新失败", "当前显示上次成功数据")
        else:
            state["refreshActiveMissions"] = active
            state["refreshChannels"] = len(channels)
            self.status("SOOP 掉宝信息已刷新", f"{active} 个当前活动 · {len(channels)} 个可用直播间")
        return state

    async def _refresh_saved_account(self, cookies: dict[str, str]) -> Any:
        """Query a saved Session without starting its background miner."""
        miner_type = getattr(self._core.get("miner"), "SoopMiner", None)
        if not callable(miner_type):
            raise RuntimeError("SOOP Core 不支持未运行账号的即时刷新。")
        miner = miner_type(
            cookies,
            channel_config=self._channel_config(),
            app_config=self._app_config(),
        )
        await miner.__aenter__()
        try:
            return await miner.force_refresh()
        finally:
            await miner.__aexit__()

    def login(self, payload: dict[str, Any]) -> dict[str, Any]:
        self._require_core()
        userid = str(payload.get("userid", "")).strip()
        password = str(payload.get("password", ""))
        if not userid or not password:
            raise ValueError("账号和密码不能为空")
        if not self._cancel_reauthentication(userid, wait=True):
            raise RuntimeError("SOOP 自动重登仍在结束中，请稍后重试。")
        cookies = self._submit(self._core["auth"].login(userid, password, config=self._app_config()))
        try:
            write_credentials(self._credentials_path(userid), userid, password)
        except Exception as exc:
            # Do not leave an account presented as auto-recoverable when its
            # password could not be persisted securely.
            try:
                self._core["auth"].remove_account(userid)
            except Exception:
                pass
            raise RuntimeError("登录成功，但无法使用 Windows DPAPI 安全保存凭据。") from exc
        self._allow_reauthentication(userid)
        self._reauth_last_attempt.pop(userid, None)
        return {"userid": userid, "saved": bool(cookies), "credentialSaved": True}

    def logout(self, payload: dict[str, Any]) -> dict[str, Any]:
        return self.delete_account(payload)

    def add_account(self, payload: dict[str, Any]) -> dict[str, Any]:
        return self.login(payload)

    def delete_account(self, payload: dict[str, Any]) -> dict[str, Any]:
        self._require_core()
        uid = str(payload.get("userid", payload.get("uid", ""))).strip()
        if not uid:
            raise ValueError("账号不能为空")
        if not self._cancel_reauthentication(uid, wait=True):
            raise RuntimeError("SOOP 自动重登仍在结束中，请稍后再删除账号。")
        if self._manager is not None:
            try:
                stopped = self._submit(self._manager.stop_account_and_wait(uid), timeout=20)
            except Exception as exc:
                raise RuntimeError("停止 SOOP 账号失败，未删除本地登录信息。") from exc
            if stopped is False:
                raise RuntimeError("停止 SOOP 账号失败，未删除本地登录信息。")
            if uid in {str(item) for item in getattr(self._manager, "running_uids", [])}:
                raise RuntimeError("停止 SOOP 账号失败，未删除本地登录信息。")

            self.running = bool(getattr(self._manager, "running_uids", []))

        try:
            removed = bool(self._core["auth"].remove_account(uid))
        except Exception as exc:
            raise RuntimeError("SOOP 账号删除失败，本地登录信息仍然存在。") from exc
        if not removed:
            raise RuntimeError("SOOP 账号删除失败，本地登录信息仍然存在。")

        try:
            accounts = [str(item) for item in self._core["auth"].list_accounts()]
        except Exception as exc:
            raise RuntimeError("SOOP 账号删除失败，本地登录信息仍然存在。") from exc
        account_dir = self.accounts_dir / uid
        cookies_path = account_dir / "cookies.json"
        if uid in accounts or account_dir.exists() or cookies_path.exists():
            raise RuntimeError("SOOP 账号删除失败，本地登录信息仍然存在。")

        self._states.pop(uid, None)
        if uid == str(self.settings.get("primary_account_uid", "")):
            self.settings["primary_account_uid"] = ""
            atomic_write_json(self.settings_path, self.settings)
        self.logger.info("SOOP account deleted userid=%s", uid)
        return {"userid": uid, "removed": True}

    def get_accounts(self, payload: dict[str, Any]) -> list[dict[str, Any]]:
        if self._core:
            accounts = self._core["auth"].list_accounts()
        else:
            accounts = sorted(path.name for path in self.accounts_dir.iterdir() if (path / "cookies.json").is_file())
        result = []
        primary_uid = str(self.settings.get("primary_account_uid", ""))
        for uid in accounts:
            state = self._states.get(uid)
            result.append(self._state_to_dict(state) if state else {
                "uid": uid, "running": False, "status": "已保存",
                "channelId": None, "channelName": None, "primary": uid == primary_uid,
            })
        return result

    def set_primary_account(self, payload: dict[str, Any]) -> dict[str, Any]:
        uid = str(payload.get("userid", payload.get("uid", ""))).strip()
        if not uid:
            raise ValueError("请先选择一个 SOOP 账号")
        if uid not in {str(item.get("uid", "")) for item in self.get_accounts({})}:
            raise ValueError("未找到要设为主账号的 SOOP 账号")
        self.settings["primary_account_uid"] = uid
        atomic_write_json(self.settings_path, self.settings)
        event("primary_account_changed", {"userid": uid})
        return {"userid": uid, "primary": True}

    def auto_start(self, payload: dict[str, Any]) -> dict[str, Any]:
        self._require_core()
        uid = str(self.settings.get("primary_account_uid", "")).strip()
        if not uid:
            return {**self.load_state({}), "autoStartCompleted": False, "missingPrimary": True}
        self._allow_reauthentication(uid)

        self.status("正在恢复 SOOP 账号…", uid)
        cookies = self._core["auth"].load_cookies(uid)
        if not cookies:
            raise RuntimeError("SOOP 自动登录失败")
        if self._manager is None:
            self._manager = self._core["multi"].MultiMinerManager(
                on_state=self._state_callback,
                channel_config=self._channel_config(),
                app_config=self._app_config(),
            )
        self._auto_start_uid = uid
        self.status("正在刷新掉宝信息…", "正在读取直播间、任务与奖励背包")
        try:
            self._submit(self._manager.start_account(cookies))
        except Exception as exc:
            self._auto_start_uid = ""
            raise RuntimeError("SOOP 自动登录失败") from exc
        self.running = True

        # State callbacks are emitted by the real miner after its network,
        # channel, mission and inventory work. Wait for that response instead
        # of using a guessed startup delay.
        import time
        expires = time.monotonic() + 90.0
        terminal = {"登录已失效，请重新添加账号", "无可用直播间", "进房失败"}
        failure = ""
        with self._state_changed:
            while True:
                state = self._states.get(uid)
                if state is not None and state.status == "挂机中" and state.running:
                    break
                if state is not None and state.status in terminal:
                    if state.status.startswith("登录已失效") and self._handle_authentication_failure(uid, restart=True):
                        self._state_changed.wait(timeout=1.0)
                        continue
                    failure = ("SOOP 自动登录失败" if state.status.startswith("登录已失效")
                               else f"SOOP 自动启动失败：{state.status}")
                    break
                remaining = expires - time.monotonic()
                if remaining <= 0:
                    failure = "SOOP 自动启动超时，请检查网络或账号状态。"
                    break
                self._state_changed.wait(timeout=remaining)

        if failure:
            self._submit(self._manager.stop_account_and_wait(uid), timeout=20)
            self.running = bool(self._manager.running_uids)
            self._auto_start_uid = ""
            raise RuntimeError(failure)

        self.status("正在启动主账号…", uid)
        self._auto_start_uid = ""
        self.status("SOOP 正在运行", uid)
        return {**self.load_state({}), "autoStartCompleted": True, "missingPrimary": False}

    def start_account(self, payload: dict[str, Any]) -> dict[str, Any]:
        self._require_core()
        uid = str(payload.get("userid", payload.get("uid", ""))).strip()
        self._allow_reauthentication(uid)
        retry_attempt = int(payload.get("retryAttempt", 0) or 0)
        if retry_attempt > 0:
            self.logger.debug("SOOP 正在进行第 %d 次网络恢复重试。", retry_attempt)
        cookies = self._core["auth"].load_cookies(uid)
        if not cookies:
            raise ValueError("未找到该账号的 Session")
        if self._manager is None:
            self._manager = self._core["multi"].MultiMinerManager(
                on_state=self._state_callback, channel_config=self._channel_config(), app_config=self._app_config()
            )
        try:
            self._submit(self._manager.start_account(cookies))
        except Exception as exc:
            if self._is_transient_network_error(exc):
                if retry_attempt == 0:
                    self.logger.warning("SOOP 网络连接异常，正在自动重试。")
                else:
                    self.logger.debug("SOOP 仍无法连接，后台会继续重试。")
                raise TransientNetworkError(
                    "network_unavailable", "SOOP 暂时无法连接网络。"
                ) from exc
            raise
        self.running = True
        if retry_attempt > 0:
            self.logger.info("SOOP 网络连接已恢复。")
        return {"userid": uid, "started": True}

    def stop_account(self, payload: dict[str, Any]) -> dict[str, Any]:
        uid = str(payload.get("userid", payload.get("uid", ""))).strip()
        self._cancel_reauthentication(uid)
        if self._manager is not None:
            self._submit(self._manager.stop_account_and_wait(uid), timeout=20)
        return {"userid": uid, "stopped": True}

    def _tasks_for_state(self, state: Any, *, include_ended: bool = False) -> list[dict[str, Any]]:
        rows = [{"uid": state.uid, **self._mission_to_dict(mission)} for mission in state.missions]
        by_id = {
            str(row["id"]): row for row in rows if str(row.get("id", ""))
        }
        for activity in getattr(state, "events", []):
            activity_row = self._event_to_dict(activity)
            activity_id = str(activity_row.get("id", ""))
            if not activity_id:
                continue
            existing = by_id.get(activity_id)
            if existing is None:
                existing = {"uid": state.uid, **activity_row}
                rows.append(existing)
                by_id[activity_id] = existing
                continue
            # The activity catalog is the authoritative activity-window status;
            # the mission row remains the source of account progress and
            # completion.  Never let catalog ``live``/activity data overwrite
            # the structured completed flag from mission items.
            existing["active"] = activity_row["active"]
            existing["ended"] = activity_row["ended"]
            existing["notYetOpen"] = activity_row["notYetOpen"]
            existing["eventActive"] = activity_row["active"]
            existing["activityLifecycle"] = activity_row["lifecycle"]
            existing["source"] = "mission+event"
            for name in _TIME_FIELDS:
                if name in activity_row:
                    existing[name] = activity_row[name]
            existing.update(normalize_task_status(existing))
        # Keep fully completed missions in the display/history snapshot even
        # after the event window closes.  Other ended activities remain in the
        # Core state for diagnostics but are not current WPF tasks.
        if include_ended:
            return rows
        return [
            row for row in rows
            if not bool(row.get("ended", False)) or bool(row.get("completed", False))
        ]

    def get_tasks(self, payload: dict[str, Any]) -> list[dict[str, Any]]:
        uid = str(payload.get("userid", payload.get("uid", ""))).strip()
        states = [self._states[uid]] if uid and uid in self._states else list(self._states.values())
        return [task for state in states for task in self._tasks_for_state(state)]

    def get_events(self, payload: dict[str, Any]) -> list[dict[str, Any]]:
        uid = str(payload.get("userid", payload.get("uid", ""))).strip()
        states = [self._states[uid]] if uid and uid in self._states else list(self._states.values())
        return [
            {"uid": state.uid, **self._event_to_dict(activity)}
            for state in states
            for activity in getattr(state, "events", [])
        ]

    def get_inventory(self, payload: dict[str, Any]) -> list[dict[str, Any]]:
        uid = str(payload.get("userid", payload.get("uid", ""))).strip()
        states = [self._states[uid]] if uid and uid in self._states else list(self._states.values())
        return [{"uid": state.uid, **item} for state in states for item in map(self._inventory_to_dict, state.inventory)]

    def get_current_progress(self, payload: dict[str, Any]) -> list[dict[str, Any]]:
        uid = str(payload.get("userid", payload.get("uid", ""))).strip()
        states = [self._states[uid]] if uid and uid in self._states else list(self._states.values())
        return [row for state in states for row in self._current_progress_for_state(state)]

    def get_channels(self, payload: dict[str, Any]) -> list[dict[str, Any]]:
        channels: dict[str, dict[str, Any]] = {}
        for state in self._states.values():
            for item in self._state_to_dict(state)["channels"]:
                channels[item["id"]] = item
        return list(channels.values())

    def set_channel(self, payload: dict[str, Any]) -> dict[str, Any]:
        updates = {
            "channel_mode": payload.get("mode", self.settings.get("channel_mode", "smart")),
            "manual_input": payload.get("manualInput", self.settings.get("manual_input", "")),
            "priority_mission_id": payload.get("priorityMissionId", self.settings.get("priority_mission_id", "auto")),
        }
        return self.save_settings({"settings": updates})

    def claim_reward(self, payload: dict[str, Any]) -> dict[str, Any]:
        self._require_core()
        uid = str(payload.get("userid", payload.get("uid", ""))).strip()
        item_id = str(payload.get("id", "")).strip()
        if not uid or not item_id:
            raise ValueError("请先选择要领取的 SOOP 奖励")

        selected = next((item for item in self.get_inventory({"uid": uid}) if item.get("id") == item_id), None)
        if selected is None:
            raise ValueError("未找到要领取的 SOOP 奖励")
        if selected.get("claimed"):
            return {"id": item_id, "status": "already_claimed", "success": True}

        cookies = self._core["auth"].load_cookies(uid)
        if not cookies:
            raise ValueError("SOOP 登录已失效，请重新添加账号")

        async def claim_and_refresh() -> tuple[Any, list[Any] | None]:
            context = self._core["network"].AccountNetworkContext(uid, cookies, self._app_config())
            session = await context.open()
            try:
                client = self._core["drops"].DropsClient(session)
                result = await client.claim_and_verify(item_id, max_attempts=2)
                try:
                    refreshed = await client.get_inventory(with_codes=True)
                except Exception:
                    refreshed = None
                return result, refreshed
            finally:
                await context.close()

        result, refreshed = self._submit(claim_and_refresh(), timeout=80)
        if refreshed is not None and uid in self._states:
            state = self._states[uid]
            state.inventory = list(refreshed)
            self._state_callback(state)
        status = result.status.value
        self.logger.info("[%s] 手动领取结果：%s", uid, status)
        return {
            "id": item_id,
            "status": status,
            "success": bool(result.success),
            "redeemCode": result.redeem_code or "",
        }

    def copy_redeem_code(self, payload: dict[str, Any]) -> dict[str, Any]:
        item_id = str(payload.get("id", ""))
        for item in self.get_inventory({}):
            if item.get("id") == item_id:
                return {"id": item_id, "redeemCode": item.get("redeemCode", "")}
        raise ValueError("未找到奖励")

    def shutdown(self, payload: dict[str, Any]) -> dict[str, Any]:
        if self.shutdown_requested:
            return {"shutdown": True}
        self._cancel_all_reauthentication()
        result = super().shutdown(payload)
        self._loop.call_soon_threadsafe(self._loop.stop)
        return result


if __name__ == "__main__":
    raise SystemExit(run_worker(SoopWorker))
