from __future__ import annotations

import asyncio
import logging
import sys
import tempfile
import threading
import time
import unittest
from datetime import datetime, timedelta, timezone
from pathlib import Path
from types import SimpleNamespace


TWITCH_DIR = Path(__file__).resolve().parents[1]
CORE_DIR = TWITCH_DIR / "core"
DROPS_DIR = TWITCH_DIR.parent
for path in (str(DROPS_DIR), str(TWITCH_DIR), str(CORE_DIR)):
    if path not in sys.path:
        sys.path.insert(0, path)

import headless_gui
from exceptions import ExitRequest, GQLException
from worker import TwitchWorker


class HeadlessGuiContractTests(unittest.TestCase):
    def test_device_authorization_exposes_real_url_and_code(self) -> None:
        login_event = threading.Event()
        client = SimpleNamespace(
            _cloudlight_auto_start=False,
            _cloudlight_login_event=login_event,
        )
        form = headless_gui.LoginForm(client)
        asyncio.run(form.ask_enter_code("https://www.twitch.tv/activate?device-code=ABCDEFGH", "ABCD-EFGH"))
        self.assertTrue(login_event.is_set())
        self.assertEqual(client._cloudlight_login_state, "authorization_required")
        self.assertEqual(client._cloudlight_auth_required["code"], "ABCD-EFGH")
        self.assertEqual(
            client._cloudlight_auth_required["url"],
            "https://www.twitch.tv/activate?device-code=ABCDEFGH",
        )

        automatic = SimpleNamespace(
            _cloudlight_auto_start=True,
            _cloudlight_login_event=threading.Event(),
        )
        with self.assertRaises(ExitRequest):
            asyncio.run(headless_gui.LoginForm(automatic).ask_enter_code(
                "https://www.twitch.tv/activate?device-code=IJKLMNOP", "IJKL-MNOP"
            ))
        self.assertEqual(automatic._cloudlight_login_state, "needs_login")

    def test_validated_session_is_logged_in_but_not_running(self) -> None:
        with tempfile.TemporaryDirectory() as temporary:
            root = Path(temporary)
            worker = TwitchWorker(root / "data", root / "twitch.log")
            try:
                worker._authenticated_user_id = 884633382
                worker._login_state = "logged_in"
                state = worker.load_state({})
                self.assertFalse(state["running"])
                self.assertEqual(state["authState"], "logged_in")
                self.assertEqual(state["accounts"][0]["userId"], 884633382)
                self.assertTrue(state["accounts"][0]["loggedIn"])
                self.assertTrue(worker.ssl_check({})["contextCreated"])
                self.assertEqual(
                    worker._friendly_runtime_error(RuntimeError("SSL is not supported.")),
                    "Twitch 网络初始化失败，请检查代理设置或 Twitch 运行组件。",
                )
            finally:
                worker._loop.call_soon_threadsafe(worker._loop.stop)
                worker._loop_thread.join(timeout=5)
                for handler in list(worker.logger.handlers):
                    worker.logger.removeHandler(handler)
                    logging.getLogger("TwitchDrops").removeHandler(handler)
                    handler.close()

    def test_auto_start_uses_login_and_refresh_completion_signals(self) -> None:
        with tempfile.TemporaryDirectory() as temporary:
            root = Path(temporary)
            worker = TwitchWorker(root / "data", root / "twitch.log")
            try:
                auth = SimpleNamespace(user_id=884633382)
                client = SimpleNamespace(
                    _auth_state=auth,
                    _cloudlight_login_state="logged_in",
                    _cloudlight_ready=True,
                    _cloudlight_auth_required=None,
                )
                refresh_calls: list[bool] = []

                def start(payload: dict[str, object]) -> dict[str, object]:
                    self.assertTrue(payload["automatic"])
                    worker._client = client
                    worker.running = True
                    worker._login_transition.set()
                    worker._ready_transition.set()
                    return {"running": True}

                def refresh(payload: dict[str, object]) -> dict[str, object]:
                    refresh_calls.append(True)
                    return {"running": True, "accounts": [{"userId": 884633382, "loggedIn": True}]}

                worker.start = start
                worker.refresh = refresh
                worker.load_state = lambda payload: {"authState": "logged_in", "running": True}
                state = worker.auto_start({})
                self.assertEqual(refresh_calls, [True])
                self.assertTrue(state["autoStartCompleted"])
                self.assertFalse(state["requiresLogin"])
                self.assertTrue(state["running"])
            finally:
                worker._loop.call_soon_threadsafe(worker._loop.stop)
                worker._loop_thread.join(timeout=5)
                for handler in list(worker.logger.handlers):
                    worker.logger.removeHandler(handler)
                    logging.getLogger("TwitchDrops").removeHandler(handler)
                    handler.close()

    def test_core_initialization_and_public_facades(self) -> None:
        sys.modules["gui"] = headless_gui
        import constants

        with tempfile.TemporaryDirectory() as temporary:
            settings_path = Path(temporary) / "settings.json"
            constants.WORKING_DIR = Path(temporary)
            constants.LANG_PATH = CORE_DIR / "lang"
            constants.SETTINGS_PATH = settings_path
            import settings
            settings.SETTINGS_PATH = settings_path
            import twitch
            settings.SETTINGS_PATH = settings_path
            args = SimpleNamespace(
                log=False, tray=False, dump=False,
                debug_ws=logging.WARNING, debug_gql=logging.WARNING,
                logging_level=logging.INFO,
            )
            client = twitch.Twitch(settings.Settings(args))

        self.assertIsInstance(client.gui, headless_gui.GUIManager)
        self.assertIsInstance(client.gui.settings, headless_gui.HeadlessSettingsFacade)
        self.assertIsInstance(client.gui.websockets, headless_gui.WebsocketStatus)

        client.gui.websockets.update(0, status="connected")
        client.gui.websockets.update(0, topics=2)
        self.assertEqual(client.gui.websockets.snapshot(), [
            {"index": 0, "status": "connected", "topics": 2}
        ])
        client.gui.websockets.remove(0)
        self.assertEqual(client.gui.websockets.snapshot(), [])

        class Game:
            def __init__(self, name: str) -> None:
                self.name = name

        client.gui.set_games({Game("Overwatch 2"), Game("Marvel Rivals")})
        self.assertEqual(client.gui.settings.known_games, {"Overwatch 2", "Marvel Rivals"})
        revision = client.gui.settings.games_revision
        client.gui.set_games({Game("World of Warcraft")})
        self.assertTrue(client.gui.settings.wait_for_games(revision, timeout=0))
        self.assertEqual(client.gui.settings.known_games, {"World of Warcraft"})
        client.gui.channels.clear_selection()
        client.gui.status.update("idle")
        client.gui.status.clear()

    def test_worker_available_games_merges_all_compatible_sources(self) -> None:
        class Game:
            def __init__(self, name: str) -> None:
                self.name = name

        with tempfile.TemporaryDirectory() as temporary:
            root = Path(temporary)
            worker = TwitchWorker(root / "data", root / "twitch.log")
            try:
                worker._settings.priority = ["Overwatch 2"]
                worker._settings.exclude = {"Marvel Rivals"}
                facade = headless_gui.HeadlessSettingsFacade()
                facade.known_games.add("World of Warcraft")
                worker._client = SimpleNamespace(
                    gui=SimpleNamespace(settings=facade), inventory=[], channels={}
                )
                self.assertEqual(worker.get_games({}), [
                    "Marvel Rivals", "Overwatch 2", "World of Warcraft"
                ])
            finally:
                worker._loop.call_soon_threadsafe(worker._loop.stop)
                worker._loop_thread.join(timeout=5)
                for handler in list(worker.logger.handlers):
                    worker.logger.removeHandler(handler)
                    logging.getLogger("TwitchDrops").removeHandler(handler)
                    handler.close()

    def test_campaign_fields_support_all_three_display_scopes(self) -> None:
        class Game:
            def __init__(self, name: str) -> None:
                self.name = name

        class Drop:
            def __init__(self, required: int, remaining: int) -> None:
                self.total_required_minutes = required
                self.total_remaining_minutes = remaining
                self.required_minutes = required
                self.is_claimed = remaining == 0
                self.watch_requirement_completed = remaining == 0

        class Campaign:
            def __init__(
                self, campaign_id: str, game: str, *, can_earn: bool,
                active: bool = True, upcoming: bool = False, expired: bool = False,
                eligible: bool = True, finished: bool = False,
            ) -> None:
                self.id = campaign_id
                self.name = f"Campaign {campaign_id}"
                self.game = Game(game)
                self.linked = eligible
                self.active = active
                self.upcoming = upcoming
                self.expired = expired
                self.eligible = eligible
                self.finished = finished
                self.starts_at = datetime.now(timezone.utc) - timedelta(hours=1)
                self.ends_at = datetime.now(timezone.utc) + timedelta(hours=2)
                self.claimed_drops = 1 if finished else 0
                self.total_drops = 1
                self.progress = 1.0 if finished else 0.25
                self.drops = [Drop(60, 0 if finished else 45)]
                self._can_earn = can_earn

            def can_earn(self, *, ignore_channel_status: bool = False) -> bool:
                self.assert_ignore_channel_status = ignore_channel_status
                return self._can_earn

        with tempfile.TemporaryDirectory() as temporary:
            root = Path(temporary)
            worker = TwitchWorker(root / "data", root / "twitch.log")
            try:
                worker._settings.priority = ["Priority Game"]
                worker._settings.exclude = {"Excluded Game"}
                campaigns = [
                    Campaign("priority", "Priority Game", can_earn=True),
                    Campaign("available", "Other Game", can_earn=True),
                    Campaign("excluded", "Excluded Game", can_earn=True),
                    Campaign("upcoming", "Future Game", can_earn=False, active=False, upcoming=True),
                    Campaign("finished", "Old Game", can_earn=False, active=False, finished=True),
                ]
                worker._client = SimpleNamespace(inventory=campaigns)
                rows = worker.get_campaigns({})

                current = [row["id"] for row in rows if row["available"]]
                priority = [row["id"] for row in rows if row["available"] and row["priority"]]
                all_campaigns = [row["id"] for row in rows]
                self.assertEqual(current, ["priority", "available"])
                self.assertEqual(priority, ["priority"])
                self.assertEqual(all_campaigns, ["priority", "available", "excluded", "upcoming", "finished"])
                self.assertEqual(next(row for row in rows if row["id"] == "excluded")["availability"], "excluded")
                self.assertEqual(next(row for row in rows if row["id"] == "upcoming")["availability"], "upcoming")
                self.assertEqual(next(row for row in rows if row["id"] == "finished")["availability"], "finished")
                self.assertTrue(all(campaign.assert_ignore_channel_status for campaign in campaigns))
            finally:
                worker._loop.call_soon_threadsafe(worker._loop.stop)
                worker._loop_thread.join(timeout=5)
                for handler in list(worker.logger.handlers):
                    worker.logger.removeHandler(handler)
                    logging.getLogger("TwitchDrops").removeHandler(handler)
                    handler.close()

    def test_refresh_response_waits_for_inventory_games_signal(self) -> None:
        with tempfile.TemporaryDirectory() as temporary:
            root = Path(temporary)
            worker = TwitchWorker(root / "data", root / "twitch.log")
            try:
                facade = headless_gui.HeadlessSettingsFacade()
                worker._client = SimpleNamespace(
                    gui=SimpleNamespace(settings=facade),
                    _state=worker._core["State"].IDLE,
                    _state_change=threading.Event(),
                )
                worker.running = True
                worker.load_state = lambda payload: {"campaigns": ["fresh"]}

                def signal_after_inventory_fetch(payload: dict[str, object]) -> dict[str, bool]:
                    threading.Timer(0.05, lambda: facade.set_games(set())).start()
                    return {"reloading": True}

                worker.reload = signal_after_inventory_fetch
                started = time.monotonic()
                state = worker.refresh({})
                self.assertGreaterEqual(time.monotonic() - started, 0.04)
                self.assertEqual(state["campaigns"], ["fresh"])
                self.assertTrue(state["refreshCompleted"])
            finally:
                worker._loop.call_soon_threadsafe(worker._loop.stop)
                worker._loop_thread.join(timeout=5)
                for handler in list(worker.logger.handlers):
                    worker.logger.removeHandler(handler)
                    logging.getLogger("TwitchDrops").removeHandler(handler)
                    handler.close()

    def test_campaign_progress_counts_completed_unclaimed_drops(self) -> None:
        class Drop:
            def __init__(self, required: int, current: int) -> None:
                self.required_minutes = required
                self.total_required_minutes = required
                self.total_remaining_minutes = max(0, required - current)
                self.is_claimed = False
                self.watch_requirement_completed = current >= required

        drops = [Drop(required, 245) for required in (180, 240, 300, 90, 60, 120)]
        campaign = SimpleNamespace(
            id="campaign", name="Campaign", game=SimpleNamespace(name="Game"),
            linked=True, active=True, upcoming=False, expired=False, eligible=True,
            finished=False, starts_at=datetime.now(timezone.utc) - timedelta(hours=1),
            ends_at=datetime.now(timezone.utc) + timedelta(hours=2),
            claimed_drops=0, total_drops=6, drops=drops,
            can_earn=lambda ignore_channel_status: True,
        )
        with tempfile.TemporaryDirectory() as temporary:
            root = Path(temporary)
            worker = TwitchWorker(root / "data", root / "twitch.log")
            try:
                worker._client = SimpleNamespace(inventory=[campaign])
                row = worker.get_campaigns({})[0]
                self.assertEqual(row["completedDrops"], 5)
                self.assertEqual(row["claimedDrops"], 0)
                self.assertEqual(row["totalDrops"], 6)
                self.assertEqual(row["remainingMinutes"], 55)
                self.assertAlmostEqual(row["progress"], 245 / 300)
            finally:
                worker._loop.call_soon_threadsafe(worker._loop.stop)
                worker._loop_thread.join(timeout=5)
                for handler in list(worker.logger.handlers):
                    worker.logger.removeHandler(handler)
                    logging.getLogger("TwitchDrops").removeHandler(handler)
                    handler.close()

    def test_completed_unclaimed_drop_continues_campaign_without_inventory_fetch(self) -> None:
        sys.modules["gui"] = headless_gui
        import constants
        constants.LANG_PATH = CORE_DIR / "lang"
        import inventory
        import twitch

        campaign = object.__new__(inventory.DropsCampaign)
        campaign._twitch = SimpleNamespace(settings=SimpleNamespace(auto_claim_drops=False))
        campaign.timed_drops = {
            "done": SimpleNamespace(
                is_claimed=False, required_minutes=60, watch_requirement_completed=True
            )
        }
        self.assertTrue(campaign.finished)

        async def scenario() -> None:
            restarted: list[bool] = []
            states: list[object] = []
            active_campaign = SimpleNamespace(can_earn=lambda channel: True)
            drop = SimpleNamespace(
                id="done", campaign=active_campaign,
                update_claim=lambda claim_id: None, display=lambda: None,
            )
            client = object.__new__(twitch.Twitch)
            client.settings = SimpleNamespace(auto_claim_drops=False)
            client._drops = {"done": drop}
            client.watching_channel = SimpleNamespace(get_with_default=lambda default: "same-channel")

            async def skip_claim(item) -> bool:
                self.assertIs(item, drop)
                return False

            client.claim_drop_if_enabled = skip_claim
            client.restart_watching = lambda: restarted.append(True)
            client.change_state = lambda state: states.append(state)
            await twitch.Twitch.process_drops.__wrapped__(client, 1, {
                "type": "drop-claim",
                "data": {"drop_id": "done", "drop_instance_id": "claim-id"},
            })
            self.assertEqual(restarted, [True])
            self.assertEqual(states, [])

        asyncio.run(scenario())

    def test_completed_unclaimed_drop_is_logged_once_per_client_lifecycle(self) -> None:
        sys.modules["gui"] = headless_gui
        import constants
        constants.LANG_PATH = CORE_DIR / "lang"
        import twitch

        client = object.__new__(twitch.Twitch)
        client._reported_pending_claims = set()
        messages: list[str] = []
        client.print = messages.append
        drop = SimpleNamespace(id="drop-id", name="Esports Lootbox")

        with self.assertLogs("TwitchDrops", level="INFO") as captured:
            twitch.Twitch._report_pending_claim(client, drop)
            twitch.Twitch._report_pending_claim(client, drop)

        user_lines = [line for line in captured.output if "等待手动领取" in line]
        self.assertEqual(user_lines, [
            "INFO:TwitchDrops:掉宝奖励已完成，等待手动领取：Esports Lootbox"
        ])
        self.assertEqual(sum("等待手动领取" in message for message in messages), 1)

    def test_notification_not_found_is_idempotent_but_other_gql_errors_propagate(self) -> None:
        sys.modules["gui"] = headless_gui
        import constants
        constants.LANG_PATH = CORE_DIR / "lang"
        import twitch

        async def scenario() -> None:
            client = object.__new__(twitch.Twitch)
            client._notification_delete_not_found_logged = False
            states: list[object] = []
            client.change_state = lambda state: states.append(state)
            current_error = GQLException([{
                "message": "notification not found", "path": ["deleteNotification"]
            }])

            async def gql_request(query):
                raise current_error

            client.gql_request = gql_request
            message = {
                "type": "create-notification",
                "data": {"notification": {
                    "id": "missing", "type": "user_drop_reward_reminder_notification"
                }},
            }
            await twitch.Twitch.process_notifications.__wrapped__(client, 1, message)
            self.assertEqual(states, [])
            self.assertTrue(client._notification_delete_not_found_logged)

            current_error = GQLException([{
                "message": "service failure", "path": ["deleteNotification"]
            }])
            with self.assertRaises(GQLException):
                await twitch.Twitch.process_notifications.__wrapped__(client, 1, message)

        asyncio.run(scenario())


if __name__ == "__main__":
    unittest.main()
