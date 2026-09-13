from __future__ import annotations

import logging
import json
import sys
import tempfile
import unittest
from pathlib import Path
from types import SimpleNamespace


SOOP_DIR = Path(__file__).resolve().parents[1]
DROPS_DIR = SOOP_DIR.parent
for path in (str(DROPS_DIR), str(SOOP_DIR)):
    if path not in sys.path:
        sys.path.insert(0, path)

from worker import SoopWorker


class _FakeAuth:
    def __init__(self) -> None:
        self.accounts = ["first", "second"]

    def list_accounts(self) -> list[str]:
        return list(self.accounts)

    def remove_account(self, uid: str) -> bool:
        self.accounts.remove(uid)
        return True

    def load_cookies(self, uid: str) -> dict[str, str]:
        return {"uid": uid}


class _FakeManager:
    def __init__(self, on_state) -> None:
        self._on_state = on_state
        self.running_uids: list[str] = []

    async def start_account(self, cookies: dict[str, str]) -> None:
        uid = cookies["uid"]
        self.running_uids = [uid]
        self._on_state(SimpleNamespace(
            uid=uid, running=True, status="挂机中",
            channel_id="owesports", channel_nick="OW Esports", broad_no="1",
            connection_healthy=True, bridge_connected=True,
            heartbeat_status="ok", heartbeat_last_success="now",
            network_uploaded=0, network_downloaded=0, network_last_minute_bps=0,
            missions=[], inventory=[], available_channels=[],
        ))

    async def stop_account_and_wait(self, uid: str) -> None:
        self.running_uids = []


class SoopWorkerContractTests(unittest.TestCase):
    @staticmethod
    def _state(
        uid: str,
        missions: list | None = None,
        events: list | None = None,
        *,
        running: bool = False,
    ) -> SimpleNamespace:
        return SimpleNamespace(
            uid=uid,
            running=running,
            status="挂机中" if running else "已保存",
            channel_id="owesports" if running else None,
            channel_nick="OW Esports" if running else None,
            broad_no="1" if running else None,
            connection_healthy=running,
            bridge_connected=running,
            heartbeat_status="ok" if running else None,
            heartbeat_last_success="now" if running else None,
            network_uploaded=0,
            network_downloaded=0,
            network_last_minute_bps=0,
            missions=list(missions or []),
            events=list(events or []),
            inventory=[],
            available_channels=[],
        )

    @staticmethod
    def _mission(idx: str, *, active: bool = True) -> SimpleNamespace:
        item = SimpleNamespace(
            item_name=f"reward-{idx}", give_term=60, view_time=1, percent=1, mission_success=False,
        )
        return SimpleNamespace(
            drops_idx=idx,
            title=f"Mission {idx}",
            type_label="固定型",
            start_date="",
            end_date="",
            category_name="",
            category_no="",
            is_event_active=active,
            is_truly_ended=not active,
            is_not_yet_open=False,
            items=[item],
        )

    @staticmethod
    def _event(idx: str, *, active: bool = True) -> SimpleNamespace:
        return SimpleNamespace(
            drops_idx=idx,
            title=f"Activity {idx}",
            type_label="固定型",
            start_date="2026-01-01",
            end_date="2099-12-31" if active else "2020-01-01",
            is_event_active=active,
            is_truly_ended=not active,
            is_not_yet_open=False,
            acct_conn=False,
            raw={"cateName": "Overwatch"},
        )

    @staticmethod
    def _close(worker: SoopWorker) -> None:
        worker._loop.call_soon_threadsafe(worker._loop.stop)
        worker._loop_thread.join(timeout=5)
        if not worker._loop.is_running():
            worker._loop.close()
        for handler in list(worker.logger.handlers):
            worker.logger.removeHandler(handler)
            logging.getLogger().removeHandler(handler)
            handler.close()

    def test_primary_account_is_explicit_and_cleared_on_delete(self) -> None:
        with tempfile.TemporaryDirectory() as temporary:
            root = Path(temporary)
            worker = SoopWorker(root / "data", root / "soop.log")
            try:
                fake_auth = _FakeAuth()
                worker._core = {"auth": fake_auth}
                worker.set_primary_account({"userid": "second"})
                accounts = worker.get_accounts({})
                self.assertFalse(accounts[0]["primary"])
                self.assertTrue(accounts[1]["primary"])

                worker.delete_account({"userid": "second"})
                self.assertEqual(worker.settings["primary_account_uid"], "")
                self.assertFalse(any(item["primary"] for item in worker.get_accounts({})))

                state = worker.auto_start({})
                self.assertTrue(state["missingPrimary"])
                self.assertFalse(state["autoStartCompleted"])
            finally:
                self._close(worker)

    def test_refresh_returns_structured_success_even_when_channels_are_empty(self) -> None:
        with tempfile.TemporaryDirectory() as temporary:
            root = Path(temporary)
            worker = SoopWorker(root / "data", root / "soop.log")
            try:
                state = worker.refresh({})
                self.assertEqual(state["refreshStatus"], "success")
                self.assertTrue(state["refreshCompleted"])
                self.assertEqual(state["accounts"], [])
            finally:
                self._close(worker)

    def test_delete_removes_real_persistent_account_directory_and_reloads_without_uid(self) -> None:
        with tempfile.TemporaryDirectory() as temporary:
            root = Path(temporary)
            worker = SoopWorker(root / "data", root / "soop.log")
            try:
                account_dir = worker.accounts_dir / "testuser"
                cookie_path = account_dir / "cookies.json"
                account_dir.mkdir(parents=True)
                cookie_path.write_text(json.dumps({"BbsTicket": "testuser"}), encoding="utf-8")
                worker.settings["primary_account_uid"] = "testuser"
                worker._states["testuser"] = self._state("testuser")

                result = worker.delete_account({"userid": "testuser"})

                self.assertTrue(result["removed"])
                self.assertFalse(cookie_path.exists())
                self.assertFalse(account_dir.exists())
                self.assertNotIn("testuser", worker._core["auth"].list_accounts())
                self.assertNotIn("testuser", worker.get_accounts({}))
                self.assertNotIn("testuser", worker._states)
                self.assertEqual(worker.settings["primary_account_uid"], "")
                loaded = worker.load_state({})
                self.assertNotIn("testuser", {item["uid"] for item in loaded["accounts"]})
            finally:
                self._close(worker)

    def test_delete_running_account_waits_for_stop_before_removing(self) -> None:
        with tempfile.TemporaryDirectory() as temporary:
            root = Path(temporary)
            worker = SoopWorker(root / "data", root / "soop.log")
            try:
                calls: list[str] = []

                class Auth:
                    def __init__(self) -> None:
                        self.accounts = ["running"]

                    def list_accounts(self) -> list[str]:
                        return list(self.accounts)

                    def remove_account(self, uid: str) -> bool:
                        calls.append("remove")
                        self.accounts.remove(uid)
                        return True

                class Manager:
                    running_uids = ["running"]

                    async def stop_account_and_wait(self, uid: str) -> bool:
                        calls.append("stop")
                        self.running_uids = []
                        return True

                worker._core = {"auth": Auth()}
                worker._manager = Manager()
                result = worker.delete_account({"userid": "running"})

                self.assertTrue(result["removed"])
                self.assertEqual(calls, ["stop", "remove"])
            finally:
                self._close(worker)

    def test_delete_stop_failure_preserves_persistent_account(self) -> None:
        with tempfile.TemporaryDirectory() as temporary:
            root = Path(temporary)
            worker = SoopWorker(root / "data", root / "soop.log")
            try:
                account_dir = worker.accounts_dir / "running"
                account_dir.mkdir(parents=True)
                cookie_path = account_dir / "cookies.json"
                cookie_path.write_text("{}", encoding="utf-8")
                worker.settings["primary_account_uid"] = "running"
                removed = False

                class Auth:
                    def list_accounts(self) -> list[str]:
                        return ["running"]

                    def remove_account(self, uid: str) -> bool:
                        nonlocal removed
                        removed = True
                        return True

                class Manager:
                    running_uids = ["running"]

                    async def stop_account_and_wait(self, uid: str) -> bool:
                        raise TimeoutError("stop timed out")

                worker._core = {"auth": Auth()}
                worker._manager = Manager()
                with self.assertRaisesRegex(RuntimeError, "停止 SOOP 账号失败"):
                    worker.delete_account({"userid": "running"})
                self.assertFalse(removed)
                self.assertTrue(cookie_path.exists())
                self.assertTrue(account_dir.exists())
                self.assertEqual(worker.settings["primary_account_uid"], "running")
            finally:
                self._close(worker)

    def test_delete_false_or_residual_directory_is_a_failure(self) -> None:
        with tempfile.TemporaryDirectory() as temporary:
            root = Path(temporary)
            worker = SoopWorker(root / "data", root / "soop.log")
            try:
                account_dir = worker.accounts_dir / "testuser"
                account_dir.mkdir(parents=True)
                (account_dir / "cookies.json").write_text("{}", encoding="utf-8")
                worker.settings["primary_account_uid"] = "testuser"

                class FalseAuth:
                    def list_accounts(self) -> list[str]:
                        return ["testuser"]

                    def remove_account(self, uid: str) -> bool:
                        return False

                worker._core = {"auth": FalseAuth()}
                with self.assertRaisesRegex(RuntimeError, "本地登录信息仍然存在"):
                    worker.delete_account({"userid": "testuser"})
                self.assertEqual(worker.settings["primary_account_uid"], "testuser")

                class ResidualAuth:
                    def list_accounts(self) -> list[str]:
                        return []

                    def remove_account(self, uid: str) -> bool:
                        return True

                worker._core = {"auth": ResidualAuth()}
                with self.assertRaisesRegex(RuntimeError, "本地登录信息仍然存在"):
                    worker.delete_account({"userid": "testuser"})
                self.assertTrue(account_dir.exists())
            finally:
                self._close(worker)

    def test_refresh_running_account_calls_core_force_refresh_and_includes_new_mission(self) -> None:
        with tempfile.TemporaryDirectory() as temporary:
            root = Path(temporary)
            worker = SoopWorker(root / "data", root / "soop.log")
            try:
                old = self._mission("old")
                new = self._mission("new")
                old_state = self._state("account", [old], running=True)
                new_state = self._state("account", [old, new], running=True)
                calls: list[str] = []

                class Auth:
                    def list_accounts(self) -> list[str]:
                        return ["account"]

                class Manager:
                    running_uids = ["account"]

                    def get_miner(self, uid: str) -> object:
                        return object()

                    async def force_refresh_account(self, uid: str) -> SimpleNamespace:
                        calls.append(uid)
                        return new_state

                worker._core = {"auth": Auth()}
                worker._manager = Manager()
                worker._states["account"] = old_state

                result = worker.refresh({})

                self.assertEqual(calls, ["account"])
                self.assertTrue(result["refreshCompleted"])
                self.assertIn("new", {task["id"] for task in result["tasks"]})
                self.assertEqual({task["id"] for task in worker.get_tasks({})}, {"old", "new"})
            finally:
                self._close(worker)

    def test_refresh_exposes_new_event_when_mission_cache_is_old(self) -> None:
        with tempfile.TemporaryDirectory() as temporary:
            root = Path(temporary)
            worker = SoopWorker(root / "data", root / "soop.log")
            try:
                old = self._mission("old")
                event = self._event("owwc-day-2")
                old_state = self._state("account", [old], [self._event("old")], running=True)
                refreshed = self._state("account", [old], [self._event("old"), event], running=True)

                class Auth:
                    def list_accounts(self) -> list[str]:
                        return ["account"]

                class Manager:
                    running_uids = ["account"]

                    def get_miner(self, uid: str) -> object:
                        return object()

                    async def force_refresh_account(self, uid: str) -> SimpleNamespace:
                        return refreshed

                worker._core = {"auth": Auth()}
                worker._manager = Manager()
                worker._states["account"] = old_state

                result = worker.refresh({})

                activity = next(task for task in result["tasks"] if task["id"] == "owwc-day-2")
                self.assertTrue(activity["active"])
                self.assertTrue(activity["eventOnly"])
                self.assertEqual(activity["categoryName"], "Overwatch")
                self.assertIn("owwc-day-2", {item["id"] for item in result["events"]})
            finally:
                self._close(worker)

    def test_refresh_saved_account_queries_session_without_starting_manager(self) -> None:
        with tempfile.TemporaryDirectory() as temporary:
            root = Path(temporary)
            worker = SoopWorker(root / "data", root / "soop.log")
            try:
                refreshed = self._state("saved", [self._mission("new")])
                seen: list[dict[str, str]] = []

                class Auth:
                    def list_accounts(self) -> list[str]:
                        return ["saved"]

                    def load_cookies(self, uid: str) -> dict[str, str]:
                        return {"BbsTicket": uid}

                async def refresh_saved(cookies: dict[str, str]) -> SimpleNamespace:
                    seen.append(cookies)
                    return refreshed

                worker._core = {"auth": Auth()}
                worker._refresh_saved_account = refresh_saved

                result = worker.refresh({})

                self.assertEqual(seen, [{"BbsTicket": "saved"}])
                self.assertIsNone(worker._manager)
                self.assertTrue(result["refreshCompleted"])
                self.assertIn("new", {task["id"] for task in result["tasks"]})
            finally:
                self._close(worker)

    def test_refresh_failure_keeps_last_successful_task_snapshot(self) -> None:
        with tempfile.TemporaryDirectory() as temporary:
            root = Path(temporary)
            worker = SoopWorker(root / "data", root / "soop.log")
            try:
                old_state = self._state("account", [self._mission("old")], running=True)

                class Auth:
                    def list_accounts(self) -> list[str]:
                        return ["account"]

                class Manager:
                    running_uids = ["account"]

                    def get_miner(self, uid: str) -> object:
                        return object()

                    async def force_refresh_account(self, uid: str) -> None:
                        raise ConnectionError("SOOP API unavailable")

                worker._core = {"auth": Auth()}
                worker._manager = Manager()
                worker._states["account"] = old_state

                result = worker.refresh({})

                self.assertFalse(result["refreshCompleted"])
                self.assertEqual(result["refreshStatus"], "failed")
                self.assertEqual([task["id"] for task in result["tasks"]], ["old"])
                self.assertIn("SOOP API unavailable", result["refreshError"])
            finally:
                self._close(worker)

    def test_auto_start_waits_for_primary_account_running_state(self) -> None:
        with tempfile.TemporaryDirectory() as temporary:
            root = Path(temporary)
            worker = SoopWorker(root / "data", root / "soop.log")
            try:
                fake_auth = _FakeAuth()
                worker._core = {
                    "auth": fake_auth,
                    "multi": SimpleNamespace(MultiMinerManager=lambda **kwargs: _FakeManager(kwargs["on_state"])),
                }
                worker._channel_config = lambda: None
                worker._app_config = lambda: None
                worker.settings["primary_account_uid"] = "first"
                state = worker.auto_start({})
                self.assertTrue(state["autoStartCompleted"])
                self.assertFalse(state["missingPrimary"])
                self.assertTrue(state["running"])
                self.assertEqual(state["accounts"][0]["status"], "挂机中")
            finally:
                self._close(worker)

    def test_current_progress_uses_core_channel_and_active_tier_selection(self) -> None:
        with tempfile.TemporaryDirectory() as temporary:
            root = Path(temporary)
            worker = SoopWorker(root / "data", root / "soop.log")
            try:
                current_channel = SimpleNamespace(user_id="owesports")
                current_item = SimpleNamespace(
                    item_name="1 Esports Loot Box 3-1", give_term=120,
                    view_time=106, percent=88, mission_success=False,
                )
                mission = SimpleNamespace(
                    drops_idx="campaign-3", title="OWWC GROUP STAGE DAY 3",
                    type_label="固定型", start_date="", end_date="",
                    category_name="Overwatch 2", category_no="123",
                    is_event_active=True, items=[current_item],
                    active_item=lambda: current_item,
                )
                worker._manager = SimpleNamespace(
                    get_miner=lambda uid: SimpleNamespace(_current=current_channel)
                )
                worker._core = {"channel": SimpleNamespace(
                    missions_for_channel=lambda missions, channel: missions
                    if channel is current_channel else []
                )}
                state = SimpleNamespace(
                    uid="account", running=True, status="挂机中",
                    channel_id="owesports", channel_nick="OW Esports", broad_no="1",
                    connection_healthy=True, bridge_connected=True,
                    heartbeat_status="ok", heartbeat_last_success="now",
                    network_uploaded=0, network_downloaded=0, network_last_minute_bps=0,
                    missions=[mission], inventory=[], available_channels=[],
                )

                progress = worker._state_to_dict(state)["currentProgress"]
                self.assertEqual(progress, [{
                    "id": "account:campaign-3", "account": "account", "channel": "OW Esports",
                    "campaign": "OWWC GROUP STAGE DAY 3", "reward": "1 Esports Loot Box 3-1",
                    "currentMinutes": 106, "requiredMinutes": 120, "percent": 88,
                }])
            finally:
                self._close(worker)

    def test_manual_claim_uses_verified_core_flow_without_real_network(self) -> None:
        with tempfile.TemporaryDirectory() as temporary:
            root = Path(temporary)
            worker = SoopWorker(root / "data", root / "soop.log")
            try:
                calls: list[tuple[str, object]] = []

                class FakeContext:
                    def __init__(self, uid, cookies, config) -> None:
                        calls.append(("context", (uid, cookies, config)))

                    async def open(self):
                        return "session"

                    async def close(self) -> None:
                        calls.append(("closed", True))

                class FakeDropsClient:
                    def __init__(self, session) -> None:
                        self.session = session

                    async def claim_and_verify(self, item_id, *, max_attempts):
                        calls.append(("claim", (item_id, max_attempts)))
                        return SimpleNamespace(
                            status=SimpleNamespace(value="claimed"), success=True,
                            redeem_code="CODE-123",
                        )

                    async def get_inventory(self, *, with_codes):
                        calls.append(("refresh", with_codes))
                        return []

                worker._core = {
                    "auth": SimpleNamespace(load_cookies=lambda uid: {"uid": uid}),
                    "network": SimpleNamespace(AccountNetworkContext=FakeContext),
                    "drops": SimpleNamespace(DropsClient=FakeDropsClient),
                }
                worker._app_config = lambda: "config"
                worker.get_inventory = lambda payload: [{
                    "uid": "account", "id": "reward", "claimed": False,
                }]

                result = worker.claim_reward({"userid": "account", "id": "reward"})
                self.assertEqual(result, {
                    "id": "reward", "status": "claimed", "success": True,
                    "redeemCode": "CODE-123",
                })
                self.assertIn(("claim", ("reward", 2)), calls)
                self.assertIn(("refresh", True), calls)
                self.assertIn(("closed", True), calls)
            finally:
                self._close(worker)


if __name__ == "__main__":
    unittest.main()
