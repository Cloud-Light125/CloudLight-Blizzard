from __future__ import annotations

import asyncio
import threading
from typing import Any

from Shared.protocol import event, redact
from exceptions import ExitRequest


class _Button:
    def config(self, **kwargs: Any) -> None:
        pass


class _Help:
    def __init__(self) -> None:
        self._invalidate_button = _Button()


class _Status:
    def __init__(self) -> None:
        self.value = ""

    def update(self, value: object) -> None:
        self.value = str(value)
        event("mining_status", {"status": str(value)})

    def clear(self) -> None:
        self.value = ""
        event("mining_status", {"status": ""})


class _Tray:
    def __init__(self) -> None:
        self.state = "pickaxe"

    def change_icon(self, value: str) -> None:
        self.state = value
        event("tray_state", {"state": value})

    def notify(self, message: object, title: object = "") -> None:
        event("notification", {"title": str(title), "message": redact(message)})


class _Progress:
    def __init__(self) -> None:
        self.drop: Any = None
        self._seconds = 0
        self._timer_task: asyncio.Task[None] | None = None

    def minute_almost_done(self) -> bool:
        return self._timer_task is None or self._seconds <= 10

    def stop_timer(self) -> None:
        if self._timer_task is not None:
            self._timer_task.cancel()
            self._timer_task = None

    async def _timer(self) -> None:
        self._seconds = 60
        while self._seconds > 0:
            await asyncio.sleep(1)
            self._seconds -= 1
        self._timer_task = None

    def display(self, drop: Any, *, countdown: bool = True, subone: bool = False) -> None:
        self.stop_timer()
        self.drop = drop
        self._seconds = 0 if subone else 60
        if drop is not None and countdown:
            self._timer_task = asyncio.create_task(self._timer())


class WebsocketStatus:
    def __init__(self) -> None:
        self.items: dict[int, dict[str, Any]] = {}

    def update(
        self, index: int, status: object | None = None, topics: int | None = None
    ) -> None:
        if status is None and topics is None:
            raise TypeError("You need to provide at least one of: status, topics")
        entry = self.items.setdefault(index, {"index": index, "status": "disconnected", "topics": 0})
        if status is not None:
            entry["status"] = str(status)
        if topics is not None:
            entry["topics"] = int(topics)
        event("websocket", dict(entry))

    def remove(self, index: int) -> None:
        self.items.pop(index, None)
        event("websocket", {"index": index, "status": "removed"})

    def snapshot(self) -> list[dict[str, Any]]:
        return [dict(self.items[index]) for index in sorted(self.items)]


class HeadlessSettingsFacade:
    def __init__(self, twitch: Any = None) -> None:
        self._twitch = twitch
        self.known_games: set[str] = set()
        self._games_revision = 0
        self._games_changed = threading.Condition()

    @property
    def games_revision(self) -> int:
        with self._games_changed:
            return self._games_revision

    def wait_for_games(self, after_revision: int, timeout: float) -> bool:
        with self._games_changed:
            return self._games_changed.wait_for(
                lambda: self._games_revision > after_revision,
                timeout=timeout,
            )

    def set_games(self, games: set[Any]) -> None:
        names = {
            str(game.name).strip()
            for game in games
            if getattr(game, "name", None) and str(game.name).strip()
        }
        with self._games_changed:
            self.known_games = names
            self._games_revision += 1
            self._games_changed.notify_all()
        if self._twitch is not None:
            self._twitch._cloudlight_ready = True
            ready_event = getattr(self._twitch, "_cloudlight_ready_event", None)
            if ready_event is not None:
                ready_event.set()
        event("games", {"items": sorted(names, key=str.casefold)})


class ChannelList:
    def __init__(self) -> None:
        self.items: dict[int, Any] = {}
        self.watching: Any = None
        self.selection: Any = None

    def display(self, channel: Any, add: bool = False) -> None:
        self.items[channel.id] = channel
        event("channel", {
            "id": channel.id, "name": channel.name, "online": channel.online,
            "viewers": channel.viewers, "game": channel.game.name if channel.game else "",
            "dropsEnabled": channel.drops_enabled, "add": add,
        })

    def remove(self, channel: Any) -> None:
        self.items.pop(channel.id, None)
        event("channel_removed", {"id": channel.id})

    def clear(self) -> None:
        self.items.clear()
        self.selection = None
        event("channels_cleared", {})

    def clear_selection(self) -> None:
        self.selection = None

    def get_selection(self) -> Any:
        return self.selection

    def select(self, channel_id: int | None) -> None:
        self.selection = self.items.get(channel_id) if channel_id is not None else None

    def set_watching(self, channel: Any) -> None:
        self.watching = channel
        event("current_channel", {"id": channel.id, "name": channel.name, "game": channel.game.name if channel.game else ""})

    def clear_watching(self) -> None:
        self.watching = None
        event("current_channel", {})


class _Inventory:
    def __init__(self) -> None:
        self.campaigns: dict[str, Any] = {}

    async def add_campaign(self, campaign: Any) -> None:
        self.campaigns[campaign.id] = campaign
        event("campaign", {
            "id": campaign.id, "name": campaign.name, "game": campaign.game.name,
            "active": campaign.active, "linked": campaign.linked,
            "claimedDrops": campaign.claimed_drops, "totalDrops": campaign.total_drops,
        })

    def update_drop(self, drop: Any) -> None:
        event("drop", {
            "id": drop.id, "name": drop.name, "currentMinutes": drop.current_minutes,
            "requiredMinutes": drop.required_minutes, "claimed": drop.is_claimed,
            "canClaim": drop.can_claim,
        })

    def clear(self) -> None:
        self.campaigns.clear()
        event("campaigns_cleared", {})


class LoginForm:
    def __init__(self, twitch: Any = None) -> None:
        self._twitch = twitch
        self.status = ""
        self.user_id: int | None = None

    async def ask_enter_code(self, verification_uri: object, user_code: str) -> None:
        automatic = bool(getattr(self._twitch, "_cloudlight_auto_start", False))
        payload = {
            "url": str(verification_uri), "code": user_code,
            "method": "device", "automatic": automatic,
        }
        if self._twitch is not None:
            self._twitch._cloudlight_auth_required = payload
            self._twitch._cloudlight_login_state = "needs_login" if automatic else "authorization_required"
            login_event = getattr(self._twitch, "_cloudlight_login_event", None)
            if login_event is not None:
                login_event.set()
        event("auth_required", payload)
        if automatic:
            # Automatic startup must not wait invisibly for a device code or
            # force-open a browser every time the application starts.
            raise ExitRequest()

    async def ask_login(self) -> Any:
        raise RuntimeError("Worker 模式只支持 Twitch 设备码登录")

    def update(self, status: object, user_id: int | None) -> None:
        self.status = str(status)
        self.user_id = user_id
        if self._twitch is not None:
            self._twitch._cloudlight_login_status = str(status)
            self._twitch._cloudlight_login_user_id = user_id
            self._twitch._cloudlight_login_state = "logged_in" if user_id is not None else "checking"
            if user_id is not None:
                self._twitch._cloudlight_auth_required = None
            login_event = getattr(self._twitch, "_cloudlight_login_event", None)
            if login_event is not None and user_id is not None:
                login_event.set()
        event("login_status", {"status": str(status), "userId": user_id})

    def clear(self, **kwargs: Any) -> None:
        if not kwargs:
            self.status = ""
            self.user_id = None


class GUIManager:
    def __init__(self, twitch: Any) -> None:
        self._twitch = twitch
        self.close_requested = False
        self._closed = asyncio.Event()
        self.status = _Status()
        self.tray = _Tray()
        self.progress = _Progress()
        self.websockets = WebsocketStatus()
        self.channels = ChannelList()
        self.inv = _Inventory()
        self.login = LoginForm(twitch)
        self.settings = HeadlessSettingsFacade(twitch)
        self.help = _Help()

    def start(self) -> None:
        event("worker_ready", {})

    def stop(self) -> None:
        self.progress.stop_timer()

    def close(self) -> None:
        self.close_requested = True
        self._closed.set()
        self._twitch.close()

    def close_window(self) -> None:
        event("worker_window_closed", {})

    def prevent_close(self) -> None:
        self.close_requested = False
        self._closed.clear()

    def print(self, message: object) -> None:
        event("log", {"level": "info", "message": redact(message)})

    def save(self, *, force: bool = False) -> None:
        pass

    def set_games(self, games: set[Any]) -> None:
        self.settings.set_games(games)

    def display_drop(self, drop: Any, countdown: bool = True, subone: bool = False) -> None:
        self.progress.display(drop, countdown=countdown, subone=subone)
        self.inv.update_drop(drop)

    def clear_drop(self) -> None:
        self.progress.display(None)
        event("drop", {})

    def grab_attention(self, *, sound: bool = True) -> None:
        pass

    async def coro_unless_closed(self, coroutine: Any) -> Any:
        if self.close_requested:
            coroutine.close()
            raise ExitRequest()
        task = asyncio.ensure_future(coroutine)
        closed = asyncio.create_task(self._closed.wait())
        done, pending = await asyncio.wait({task, closed}, return_when=asyncio.FIRST_COMPLETED)
        for pending_task in pending:
            pending_task.cancel()
        if closed in done:
            task.cancel()
            raise ExitRequest()
        return await task

    async def wait_until_closed(self) -> None:
        await self._closed.wait()
