from __future__ import annotations

import asyncio
import dataclasses
import importlib
import importlib.util
import json
import logging
import os
import sys
import threading
from pathlib import Path
from typing import Any

sys.path.insert(0, str(Path(__file__).resolve().parents[1]))
from Shared.protocol import (
    TransientNetworkError,
    WorkerBase,
    atomic_write_json,
    event,
    read_json,
    run_worker,
)


DEFAULT_SETTINGS: dict[str, Any] = {
    "settings_version": 4,
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
    "channel_mode": "smart",
    "manual_input": "",
    "preferred_bjid": "owesports",
    "priority_mission_id": "auto",
    "hang_without_missions": True,
    "primary_account_uid": "",
}


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
            network = importlib.import_module("cloudlight_soop_core.network")
            self._core = {
                "config": config, "auth": auth, "channel": channel,
                "drops": drops, "multi": multi, "network": network,
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

    def _state_callback(self, state: Any) -> None:
        with self._state_changed:
            self._states[state.uid] = state
            self._state_changed.notify_all()
        event("account_status", self._state_to_dict(state))
        active = sum(1 for item in self._states.values() if item.running)
        if state.uid == self._auto_start_uid:
            self.status("正在刷新掉宝信息…", "正在读取直播间、任务与奖励背包")
        else:
            self.status("运行中" if active else "已停止", f"{active}/{len(self.get_accounts({}))} 个账号运行")

    @staticmethod
    def _mission_to_dict(mission: Any) -> dict[str, Any]:
        return {
            "id": mission.drops_idx, "title": mission.title, "type": mission.type_label,
            "startDate": mission.start_date, "endDate": mission.end_date,
            "categoryName": mission.category_name, "categoryNo": mission.category_no,
            "active": mission.is_event_active,
            "items": [
                {"name": item.item_name, "requiredMinutes": item.give_term, "viewMinutes": item.view_time,
                 "percent": item.percent, "completed": item.mission_success}
                for item in mission.items
            ],
        }

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
        return {
            "uid": state.uid, "running": state.running, "status": state.status,
            "primary": state.uid == str(self.settings.get("primary_account_uid", "")),
            "channelId": state.channel_id, "channelName": state.channel_nick, "broadcastNo": state.broad_no,
            "connectionHealthy": state.connection_healthy, "bridgeConnected": state.bridge_connected,
            "heartbeatStatus": state.heartbeat_status, "heartbeatLastSuccess": state.heartbeat_last_success,
            "networkUploaded": state.network_uploaded, "networkDownloaded": state.network_downloaded,
            "networkBps": state.network_last_minute_bps,
            "currentProgress": self._current_progress_for_state(state),
            "missions": [self._mission_to_dict(mission) for mission in state.missions],
            "inventory": [self._inventory_to_dict(item) for item in state.inventory],
            "channels": [
                {"id": channel.user_id, "name": channel.user_nick, "broadcastNo": channel.broad_no,
                 "onAir": channel.on_air, "categories": channel.category_names, "hasDrops": channel.has_drops}
                for channel in state.available_channels
            ],
        }

    def load_state(self, payload: dict[str, Any]) -> dict[str, Any]:
        return {
            "running": self.running,
            "settings": self._effective_settings(),
            "accounts": self.get_accounts({}),
            "tasks": self.get_tasks({}),
            "inventory": self.get_inventory({}),
            "currentProgress": self.get_current_progress({}),
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
        if self.running:
            self._restart_manager()
        return self._effective_settings()

    def _validate_settings(self) -> None:
        self.settings["settings_version"] = 4
        self.settings["primary_account_uid"] = str(self.settings.get("primary_account_uid", "")).strip()
        for name, minimum, maximum in (
            ("mission_poll_interval", 30, 600), ("inventory_poll_interval", 60, 1800),
            ("channel_refresh_interval", 60, 1800),
        ):
            self.settings[name] = max(minimum, min(maximum, int(self.settings.get(name, DEFAULT_SETTINGS[name]))))
        if self.settings.get("channel_mode") not in {"smart", "manual", "owesports"}:
            self.settings["channel_mode"] = "smart"

    def start(self, payload: dict[str, Any]) -> dict[str, Any]:
        self._require_core()
        if self.running:
            return self.load_state({})
        self._manager = self._core["multi"].MultiMinerManager(
            on_state=self._state_callback, channel_config=self._channel_config(), app_config=self._app_config()
        )
        started = self._submit(self._manager.start_all())
        self.running = bool(started)
        self.status("运行中" if self.running else "没有可启动的账号", f"{len(started)} 个账号")
        return self.load_state({})

    def stop(self, payload: dict[str, Any]) -> dict[str, Any]:
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
        state = self.load_state({})
        state["refreshStatus"] = "success"
        state["refreshCompleted"] = True
        return state

    def login(self, payload: dict[str, Any]) -> dict[str, Any]:
        self._require_core()
        userid = str(payload.get("userid", "")).strip()
        password = str(payload.get("password", ""))
        if not userid or not password:
            raise ValueError("账号和密码不能为空")
        cookies = self._submit(self._core["auth"].login(userid, password, config=self._app_config()))
        return {"userid": userid, "saved": bool(cookies)}

    def logout(self, payload: dict[str, Any]) -> dict[str, Any]:
        return self.delete_account(payload)

    def add_account(self, payload: dict[str, Any]) -> dict[str, Any]:
        return self.login(payload)

    def delete_account(self, payload: dict[str, Any]) -> dict[str, Any]:
        self._require_core()
        uid = str(payload.get("userid", payload.get("uid", ""))).strip()
        if not uid:
            raise ValueError("账号不能为空")
        if self._manager is not None:
            self._submit(self._manager.stop_account_and_wait(uid), timeout=20)
        removed = self._core["auth"].remove_account(uid)
        self._states.pop(uid, None)
        if uid == str(self.settings.get("primary_account_uid", "")):
            self.settings["primary_account_uid"] = ""
            atomic_write_json(self.settings_path, self.settings)
        return {"userid": uid, "removed": removed}

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
        if self._manager is not None:
            self._submit(self._manager.stop_account_and_wait(uid), timeout=20)
        return {"userid": uid, "stopped": True}

    def get_tasks(self, payload: dict[str, Any]) -> list[dict[str, Any]]:
        uid = str(payload.get("userid", payload.get("uid", ""))).strip()
        states = [self._states[uid]] if uid and uid in self._states else list(self._states.values())
        return [{"uid": state.uid, **task} for state in states for task in map(self._mission_to_dict, state.missions)]

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
        result = super().shutdown(payload)
        self._loop.call_soon_threadsafe(self._loop.stop)
        return result


if __name__ == "__main__":
    raise SystemExit(run_worker(SoopWorker))
