from __future__ import annotations

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
from worker import TwitchWorker


class HeadlessGuiContractTests(unittest.TestCase):
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


if __name__ == "__main__":
    unittest.main()
