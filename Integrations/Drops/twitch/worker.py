from __future__ import annotations

import asyncio
import importlib
import json
import logging
import os
import ssl
import sys
import threading
import time
from pathlib import Path
from types import SimpleNamespace
from typing import Any

sys.path.insert(0, str(Path(__file__).resolve().parents[1]))
from Shared.protocol import WorkerBase, event, run_worker


class TwitchWorker(WorkerBase):
    platform = "twitch"

    def __init__(self, data_dir: Path, log_file: Path) -> None:
        super().__init__(data_dir, log_file)
        self.settings_path = self.data_dir / "settings.json"
        self.cookies_path = self.data_dir / "cookies.jar"
        self.cache_dir = self.data_dir / "cache"
        self.cache_dir.mkdir(parents=True, exist_ok=True)
        self._core: dict[str, Any] = {}
        self._core_error = ""
        self._settings: Any = None
        self._client: Any = None
        self._client_task: Any = None
        self._client_stopped = threading.Event()
        self._client_stopped.set()
        self._login_transition = threading.Event()
        self._ready_transition = threading.Event()
        self._login_state = "logged_out"
        self._login_error = ""
        self._automatic_start = False
        self._authenticated_user_id: int | None = None
        self._loop = asyncio.new_event_loop()
        self._loop_thread = threading.Thread(target=self._run_loop, name="twitch-asyncio", daemon=True)
        self._loop_thread.start()
        self._load_core()
        self.commands.update({
            "get_campaigns": self.get_campaigns,
            "get_channels": self.get_channels,
            "select_channel": self.select_channel,
            "reload": self.reload,
            "get_games": self.get_games,
            "get_languages": self.get_languages,
            "auto_start": self.auto_start,
            "ssl_check": self.ssl_check,
            "network_check": self.network_check,
        })
        self._migrate_legacy_proxy()

    def _run_loop(self) -> None:
        asyncio.set_event_loop(self._loop)
        self._loop.run_forever()

    def _core_path(self) -> Path | None:
        configured = os.environ.get("CLOUDLIGHT_TWITCH_CORE", "").strip()
        candidates = [Path(configured)] if configured else []
        here = Path(__file__).resolve()
        candidates.extend([here.with_name("core"), here.parents[4] / "TwitchDropsMiner-dev-build"])
        if getattr(sys, "_MEIPASS", None):
            candidates.insert(0, Path(getattr(sys, "_MEIPASS")) / "core")
        return next((path.resolve() for path in candidates if (path / "twitch.py").is_file()), None)

    def _load_core(self) -> None:
        root = self._core_path()
        if root is None:
            self._core_error = "未找到 TwitchDropsMiner MIT 业务核心。"
            return
        try:
            sys.path.insert(0, str(root))
            import headless_gui
            sys.modules["gui"] = headless_gui
            constants = importlib.import_module("constants")
            constants.WORKING_DIR = self.data_dir
            constants.LANG_PATH = root / "lang"
            constants.LOG_PATH = self.log_file
            constants.DUMP_PATH = self.data_dir / "dump.dat"
            constants.LOCK_PATH = self.data_dir / "lock.file"
            constants.CACHE_PATH = self.cache_dir
            constants.CACHE_DB = self.cache_dir / "mapping.json"
            constants.COOKIES_PATH = self.cookies_path
            constants.SETTINGS_PATH = self.settings_path
            settings_module = importlib.import_module("settings")
            settings_module.SETTINGS_PATH = self.settings_path
            twitch_module = importlib.import_module("twitch")
            twitch_module.COOKIES_PATH = self.cookies_path
            args = SimpleNamespace(
                log=True, tray=False, dump=False, debug_ws=logging.WARNING,
                debug_gql=logging.WARNING, logging_level=logging.INFO,
            )
            had_settings = self.settings_path.is_file()
            self._settings = settings_module.Settings(args)
            if not had_settings and (root / "lang" / "简体中文.json").is_file():
                self._settings.language = "简体中文"
                self._settings.save(force=True)
            object.__setattr__(self._settings, "_cloudlight_fallback_direct", False)
            self._core = {
                "constants": constants, "settings": settings_module,
                "twitch": twitch_module, "PriorityMode": constants.PriorityMode,
                "State": constants.State, "root": root,
            }
            root_logger = logging.getLogger("TwitchDrops")
            root_logger.setLevel(logging.INFO)
            if self.logger.handlers and self.logger.handlers[0] not in root_logger.handlers:
                root_logger.addHandler(self.logger.handlers[0])
            self.logger.info("Twitch core loaded from %s", root)
        except Exception as exc:
            self._core_error = f"Twitch 业务核心加载失败: {exc}"
            self.logger.exception(self._core_error)

    def _require_core(self) -> None:
        if not self._core:
            raise RuntimeError(self._core_error or "Twitch 业务核心不可用")

    def _migrate_legacy_proxy(self) -> None:
        if self._settings is not None and self._settings.proxy:
            event("legacy_proxy", {"enabled": True, "url": str(self._settings.proxy), "fallbackDirect": False})

    def _submit(self, coroutine: Any, timeout: float = 35.0) -> Any:
        return asyncio.run_coroutine_threadsafe(coroutine, self._loop).result(timeout=timeout)

    def _set_login_state(self, state: str, summary: str = "", error: str = "") -> None:
        self._login_state = state
        self._login_error = error
        event("auth_state", {"state": state, "summary": summary, "error": error})

    @staticmethod
    def _friendly_runtime_error(exc: BaseException) -> str:
        message = str(exc)
        if "SSL is not supported" in message or "ssl" in message.lower() and "supported" in message.lower():
            return "Twitch 网络初始化失败，请检查代理设置或 Twitch 运行组件。"
        return "Twitch 掉宝服务启动失败，请检查网络或运行日志。"

    async def _start_client(self) -> None:
        client = self._core["twitch"].Twitch(self._settings)
        client._cloudlight_auto_start = self._automatic_start
        client._cloudlight_login_event = self._login_transition
        client._cloudlight_ready_event = self._ready_transition
        client._cloudlight_login_state = "checking"
        client._cloudlight_auth_required = None
        client._cloudlight_ready = False
        self._client = client
        self.running = True
        self._set_login_state("checking", "正在检查 Twitch 登录状态")
        self.status("正在检查登录状态", "Twitch")
        runtime_error = ""
        try:
            await client.run()
        except Exception as exc:
            self.logger.exception("Twitch worker runtime failed")
            runtime_error = self._friendly_runtime_error(exc)
            self._set_login_state("failed", runtime_error, runtime_error)
            event("error", {"message": runtime_error, "phase": "startup"})
            self._login_transition.set()
            self._ready_transition.set()
        finally:
            try:
                await self._shutdown_client_safely(client)
            finally:
                client_state = str(getattr(client, "_cloudlight_login_state", ""))
                auth = getattr(client, "_auth_state", None)
                if hasattr(auth, "user_id"):
                    self._authenticated_user_id = int(auth.user_id)
                if not runtime_error and client_state == "needs_login":
                    self._authenticated_user_id = None
                    self._set_login_state("needs_login", "Twitch Session 已失效，需要重新登录")
                if self._client is client:
                    self._client = None
                self.running = False
                if runtime_error:
                    self.status("启动失败", runtime_error)
                elif client_state == "needs_login":
                    self.status("需要登录", "请手动登录 Twitch 后重试")
                else:
                    if self._authenticated_user_id is not None:
                        self._set_login_state("logged_in", "Twitch 已登录，当前未运行")
                    elif self._login_state not in {"logged_out", "needs_login"}:
                        self._set_login_state("logged_out", "尚未登录 Twitch")
                    self.status("已停止", self._account_summary())
                self._client_stopped.set()

    async def _login_client_only(self) -> None:
        client = self._core["twitch"].Twitch(self._settings)
        client._cloudlight_auto_start = False
        client._cloudlight_login_event = self._login_transition
        client._cloudlight_ready_event = self._ready_transition
        client._cloudlight_login_state = "checking"
        client._cloudlight_auth_required = None
        client._cloudlight_ready = False
        self._client = client
        self._set_login_state("checking", "正在检查 Twitch 登录状态")
        self.status("正在检查登录状态", "Twitch")
        runtime_error = ""
        try:
            auth = await client.get_auth()
            self._authenticated_user_id = int(auth.user_id)
            self._set_login_state("logged_in", "Twitch 登录成功")
            self.status("登录成功", str(auth.user_id))
        except asyncio.CancelledError:
            raise
        except Exception as exc:
            self.logger.exception("Twitch login failed")
            runtime_error = self._friendly_runtime_error(exc)
            self._set_login_state("failed", runtime_error, runtime_error)
            event("error", {"message": runtime_error, "phase": "login"})
        finally:
            try:
                await self._shutdown_client_safely(client)
            finally:
                if self._client is client:
                    self._client = None
                self.running = False
                if runtime_error:
                    self.status("启动失败", runtime_error)
                elif self._authenticated_user_id is not None:
                    self.status("已登录 · 未运行", str(self._authenticated_user_id))
                self._client_stopped.set()

    async def _shutdown_client_safely(self, client: Any) -> None:
        try:
            await client.shutdown()
        except asyncio.CancelledError:
            self.logger.info("Twitch core shutdown was cancelled; forcing session cleanup")
        except Exception:
            self.logger.exception("Twitch core shutdown failed; forcing session cleanup")
        finally:
            session = getattr(client, "_session", None)
            if session is not None and not session.closed:
                await session.close()
                client._session = None
            await asyncio.sleep(0.25)

    def _settings_dict(self) -> dict[str, Any]:
        if self._settings is None:
            return {}
        mode = self._settings.priority_mode
        return {
            "language": self._settings.language,
            "dark_mode": self._settings.dark_mode,
            "exclude": sorted(self._settings.exclude),
            "priority": list(self._settings.priority),
            "autostart_tray": self._settings.autostart_tray,
            "connection_quality": self._settings.connection_quality,
            "tray_notifications": self._settings.tray_notifications,
            "enable_badges_emotes": self._settings.enable_badges_emotes,
            "available_drops_check": self._settings.available_drops_check,
            "auto_claim_drops": self._settings.auto_claim_drops,
            "priority_mode": mode.name,
        }

    def _account_summary(self) -> str:
        account = self.get_accounts({})
        return str(account[0].get("userId")) if account else "未登录"

    def load_state(self, payload: dict[str, Any]) -> dict[str, Any]:
        available_games = self.get_games({})
        client_state = str(getattr(self._client, "_cloudlight_login_state", "")) if self._client is not None else ""
        auth_state = client_state or self._login_state
        auth_required = getattr(self._client, "_cloudlight_auth_required", None) if self._client is not None else None
        if self._client is not None and getattr(self._client, "_cloudlight_ready", False):
            auth_state = "running"
        return {
            "running": self.running, "settings": self._settings_dict(),
            "accounts": self.get_accounts({}), "campaigns": self.get_campaigns({}),
            "inventory": self.get_inventory({}), "channels": self.get_channels({}),
            "currentChannel": self._current_channel(), "proxy": self.proxy,
            "availableGames": available_games, "games": available_games,
            "websockets": self._websocket_state(), "languages": self.get_languages({}),
            "coreAvailable": bool(self._core), "coreError": self._core_error,
            "authState": auth_state, "authRequired": auth_required,
            "loginError": self._login_error,
        }

    def start(self, payload: dict[str, Any]) -> dict[str, Any]:
        self._require_core()
        if self._client_task is not None and not self._client_task.done():
            return self.load_state({})
        self._automatic_start = bool(payload.get("automatic", False))
        self._login_transition.clear()
        self._ready_transition.clear()
        self._login_error = ""
        self._client_stopped.clear()
        self._client_task = asyncio.run_coroutine_threadsafe(self._start_client(), self._loop)
        self.running = True
        self.status("正在启动", self._account_summary())
        return self.load_state({})

    def stop(self, payload: dict[str, Any]) -> dict[str, Any]:
        client = self._client
        task = self._client_task
        if client is not None:
            self._loop.call_soon_threadsafe(client.close)
        if task is not None:
            try:
                task.result(timeout=2)
            except TimeoutError:
                self.logger.info("Twitch graceful stop timed out; cancelling the client task")
                task.cancel()
                if not self._client_stopped.wait(timeout=15):
                    self.logger.error("Twitch client cleanup did not finish after cancellation")
            except Exception:
                if not self._client_stopped.wait(timeout=15):
                    self.logger.exception("Twitch client stop failed before cleanup completed")
        self._client_task = None
        if self._client_stopped.is_set():
            self._client = None
        self.running = False
        self.status("已停止", self._account_summary())
        return self.load_state({})

    def login(self, payload: dict[str, Any]) -> dict[str, Any]:
        self._require_core()
        if self._client_task is not None and not self._client_task.done():
            return {"started": False, "state": self.load_state({})}
        self._automatic_start = False
        self._login_transition.clear()
        self._ready_transition.clear()
        self._login_error = ""
        self._client_stopped.clear()
        self._client_task = asyncio.run_coroutine_threadsafe(self._login_client_only(), self._loop)
        return {
            "started": True,
            "message": "正在检查已有 Session；如需设备授权，将显示 Twitch 返回的地址和授权码。",
            "state": self.load_state({}),
        }

    def auto_start(self, payload: dict[str, Any]) -> dict[str, Any]:
        self._require_core()
        self.status("正在检查登录状态", "正在恢复 Twitch Session")
        self.start({"automatic": True})
        if not self._login_transition.wait(timeout=75):
            raise TimeoutError("Twitch 登录状态检查超时。")
        state = self.load_state({})
        auth_state = str(state.get("authState", ""))
        if auth_state in {"authorization_required", "needs_login", "logged_out"}:
            return {**state, "requiresLogin": True, "autoStartCompleted": False}
        if auth_state == "failed":
            raise RuntimeError(self._login_error or "Twitch 自动启动失败。")

        self._set_login_state("starting", "正在刷新 Twitch 掉宝信息")
        self.status("启动中", "正在加载活动、奖励和频道")
        if not self._ready_transition.wait(timeout=90):
            raise TimeoutError("Twitch 初始掉宝信息加载超时。")
        refreshed = self.refresh({})
        self._set_login_state("running", "Twitch 正在运行")
        self.status("正在运行", self._account_summary())
        refreshed["autoStartCompleted"] = True
        refreshed["requiresLogin"] = False
        return refreshed

    def logout(self, payload: dict[str, Any]) -> dict[str, Any]:
        self.stop({})
        self.cookies_path.unlink(missing_ok=True)
        self._authenticated_user_id = None
        self._set_login_state("logged_out", "尚未登录 Twitch")
        return {"loggedOut": True}

    def ssl_check(self, payload: dict[str, Any]) -> dict[str, Any]:
        context = ssl.create_default_context()
        return {
            "contextCreated": isinstance(context, ssl.SSLContext),
            "openssl": ssl.OPENSSL_VERSION,
        }

    def network_check(self, payload: dict[str, Any]) -> dict[str, Any]:
        """Build/runtime diagnostic for HTTPS over the configured HTTP proxy."""
        import aiohttp

        url = str(payload.get("url", "https://www.twitch.tv")).strip()

        async def check() -> dict[str, Any]:
            timeout = aiohttp.ClientTimeout(total=30)
            connector = aiohttp.TCPConnector(ssl=ssl.create_default_context())
            proxy = self.proxy["proxyUrl"] if self.proxy["enableProxy"] else None
            async with aiohttp.ClientSession(timeout=timeout, connector=connector) as session:
                async with session.get(url, proxy=proxy, allow_redirects=True) as response:
                    await response.read()
                    return {
                        "ok": response.status < 500,
                        "status": response.status,
                        "url": str(response.url),
                        "proxy": bool(proxy),
                    }

        return self._submit(check(), timeout=35)

    def save_settings(self, payload: dict[str, Any]) -> dict[str, Any]:
        self._require_core()
        changes = payload.get("settings", payload)
        if not isinstance(changes, dict):
            raise ValueError("settings 必须是对象")
        fields = {
            "language", "dark_mode", "exclude", "priority", "autostart_tray",
            "connection_quality", "tray_notifications", "enable_badges_emotes",
            "available_drops_check", "auto_claim_drops", "priority_mode",
        }
        for key in fields:
            if key not in changes:
                continue
            value = changes[key]
            if key in {"exclude", "priority"}:
                value = set(map(str, value)) if key == "exclude" else list(map(str, value))
            elif key == "priority_mode":
                mode_name = str(value).upper()
                value = self._core["PriorityMode"][mode_name]
            elif key == "connection_quality":
                value = max(1, min(6, int(value)))
            setattr(self._settings, key, value)
        self._settings.save(force=True)
        if self._client is not None:
            self.reload({})
        return self._settings_dict()

    def on_proxy_changed(self) -> None:
        if self._settings is None:
            return
        from yarl import URL
        global_proxy = URL(self.proxy["proxyUrl"]) if self.proxy["enableProxy"] else URL()
        object.__setattr__(self._settings, "_cloudlight_global_proxy", global_proxy)
        object.__setattr__(self._settings, "_cloudlight_fallback_direct", bool(self.proxy["fallbackDirect"]))
        self._settings._settings["proxy"] = URL()
        self._settings.save(force=True)
        if self.running:
            event("notice", {"message": "Twitch 代理设置将在后台服务重启后生效。"})
            self.stop({})
            self.start({})

    def refresh(self, payload: dict[str, Any]) -> dict[str, Any]:
        client = self._client
        if client is None or not self.running:
            raise RuntimeError("Twitch 尚未运行，请先登录并启动掉宝服务。")
        facade = getattr(getattr(client, "gui", None), "settings", None)
        if facade is None or not callable(getattr(facade, "wait_for_games", None)):
            raise RuntimeError("Twitch 刷新状态接口不可用。")

        revision = facade.games_revision
        event("refresh_started", {})
        self.reload({})
        deadline = time.monotonic() + 75.0
        inventory_completed = False
        stable_states = {self._core["State"].IDLE, self._core["State"].CHANNEL_SWITCH}
        while time.monotonic() < deadline:
            if not inventory_completed:
                inventory_completed = facade.wait_for_games(
                    revision,
                    max(0.0, min(0.25, deadline - time.monotonic())),
                )
            state_change = getattr(client, "_state_change", None)
            state_is_stable = (
                getattr(client, "_state", None) in stable_states
                and state_change is not None
                and not state_change.is_set()
            )
            if inventory_completed and state_is_stable:
                state = self.load_state({})
                state["refreshCompleted"] = True
                event("refresh_completed", {})
                return state
            if self._client is not client or not self.running or (
                self._client_task is not None and self._client_task.done()
            ):
                raise RuntimeError("Twitch 掉宝服务在刷新完成前停止。")
            if inventory_completed:
                time.sleep(0.05)
        raise TimeoutError("Twitch 掉宝刷新超时，请检查网络后重试。")

    def reload(self, payload: dict[str, Any]) -> dict[str, Any]:
        if self._client is not None:
            self._loop.call_soon_threadsafe(self._client.change_state, self._core["State"].RESTART)
        return {"reloading": self._client is not None}

    def get_accounts(self, payload: dict[str, Any]) -> list[dict[str, Any]]:
        if self._client is not None:
            auth = self._client._auth_state
            if hasattr(auth, "user_id"):
                return [{"userId": auth.user_id, "loggedIn": True, "cookiesPath": str(self.cookies_path)}]
        if self._authenticated_user_id is not None:
            return [{"userId": self._authenticated_user_id, "loggedIn": True,
                     "cookiesPath": str(self.cookies_path)}]
        return ([{"userId": None, "loggedIn": False, "sessionSaved": True}]
                if self.cookies_path.exists() else [])

    def get_tasks(self, payload: dict[str, Any]) -> list[dict[str, Any]]:
        return self.get_campaigns(payload)

    def get_campaigns(self, payload: dict[str, Any]) -> list[dict[str, Any]]:
        if self._client is None:
            return []
        priority = set(self._settings.priority) if self._settings is not None else set()
        exclude = set(self._settings.exclude) if self._settings is not None else set()
        result: list[dict[str, Any]] = []
        for campaign in self._client.inventory:
            game_name = campaign.game.name
            drops = list(campaign.drops)
            required_minutes = max((drop.total_required_minutes for drop in drops), default=0)
            remaining_minutes = max((drop.total_remaining_minutes for drop in drops), default=0)
            excluded = game_name in exclude
            can_earn = campaign.can_earn(ignore_channel_status=True)
            available = can_earn and not excluded
            if campaign.finished:
                availability = "finished"
            elif campaign.upcoming:
                availability = "upcoming"
            elif campaign.expired:
                availability = "expired"
            elif excluded:
                availability = "excluded"
            elif not campaign.eligible:
                availability = "ineligible"
            elif available:
                availability = "available"
            else:
                availability = "unavailable"
            result.append({
                "id": campaign.id, "name": campaign.name, "game": game_name,
                "linked": campaign.linked, "active": campaign.active,
                "upcoming": campaign.upcoming, "expired": campaign.expired,
                "eligible": campaign.eligible, "finished": campaign.finished,
                "excluded": excluded, "priority": game_name in priority,
                "canEarn": can_earn, "available": available,
                "availability": availability,
                "startsAt": campaign.starts_at.isoformat(), "endsAt": campaign.ends_at.isoformat(),
                "claimedDrops": campaign.claimed_drops, "totalDrops": campaign.total_drops,
                "requiredMinutes": required_minutes, "remainingMinutes": remaining_minutes,
                "progress": campaign.progress,
            })
        return result

    def get_inventory(self, payload: dict[str, Any]) -> list[dict[str, Any]]:
        if self._client is None:
            return []
        result = []
        for campaign in self._client.inventory:
            for drop in campaign.drops:
                result.append({
                    "id": drop.id, "campaignId": campaign.id, "campaign": campaign.name,
                    "game": campaign.game.name, "name": drop.name,
                    "currentMinutes": drop.current_minutes, "requiredMinutes": drop.required_minutes,
                    "progress": drop.progress, "claimed": drop.is_claimed, "canClaim": drop.can_claim,
                    "startsAt": drop.starts_at.isoformat(), "endsAt": drop.ends_at.isoformat(),
                })
        return result

    def get_channels(self, payload: dict[str, Any]) -> list[dict[str, Any]]:
        if self._client is None:
            return []
        return [
            {"id": channel.id, "name": channel.name, "online": channel.online,
             "viewers": channel.viewers, "game": channel.game.name if channel.game else "",
             "dropsEnabled": channel.drops_enabled}
            for channel in self._client.channels.values()
        ]

    def get_games(self, payload: dict[str, Any]) -> list[str]:
        games: set[str] = set()
        if self._settings is not None:
            games.update(str(name).strip() for name in self._settings.priority if str(name).strip())
            games.update(str(name).strip() for name in self._settings.exclude if str(name).strip())
        if self._client is not None:
            facade = getattr(getattr(self._client, "gui", None), "settings", None)
            games.update(
                str(name).strip() for name in getattr(facade, "known_games", set())
                if str(name).strip()
            )
            games.update(
                campaign.game.name for campaign in self._client.inventory
                if campaign.game is not None and campaign.game.name
            )
        return sorted(games, key=str.casefold)

    def _websocket_state(self) -> list[dict[str, Any]]:
        if self._client is None:
            return []
        facade = getattr(getattr(self._client, "gui", None), "websockets", None)
        snapshot = getattr(facade, "snapshot", None)
        return snapshot() if callable(snapshot) else []

    def get_languages(self, payload: dict[str, Any]) -> list[dict[str, str]]:
        root = self._core.get("root")
        if root is None:
            return []
        preferred = {"简体中文": 0, "繁體中文": 1, "English": 2, "日本語": 3}
        names = [path.stem for path in (root / "lang").glob("*.json")]
        names.sort(key=lambda name: (preferred.get(name, 100), name.casefold()))
        return [{"value": name, "display": name} for name in names]

    def _current_channel(self) -> dict[str, Any] | None:
        if self._client is None:
            return None
        channel = self._client.watching_channel.get_with_default(None)
        return ({"id": channel.id, "name": channel.name, "game": channel.game.name if channel.game else ""}
                if channel is not None else None)

    def select_channel(self, payload: dict[str, Any]) -> dict[str, Any]:
        if self._client is None:
            raise ValueError("请先启动 Twitch 掉宝服务")
        channel_id = payload.get("id")
        self._client.gui.channels.select(int(channel_id) if channel_id is not None else None)
        self._loop.call_soon_threadsafe(self._client.change_state, self._core["State"].CHANNEL_SWITCH)
        return {"selected": channel_id}

    def shutdown(self, payload: dict[str, Any]) -> dict[str, Any]:
        if self.shutdown_requested:
            return {"shutdown": True}
        result = super().shutdown(payload)
        try:
            asyncio.run_coroutine_threadsafe(self._finish_loop(), self._loop).result(timeout=5)
        except Exception:
            self.logger.exception("Twitch event loop finalization failed")
        self._loop.call_soon_threadsafe(self._loop.stop)
        return result

    async def _finish_loop(self) -> None:
        await asyncio.sleep(0.25)
        await self._loop.shutdown_asyncgens()


if __name__ == "__main__":
    raise SystemExit(run_worker(TwitchWorker))
