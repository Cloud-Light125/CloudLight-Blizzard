from __future__ import annotations

import logging
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
    def _close(worker: SoopWorker) -> None:
        worker._loop.call_soon_threadsafe(worker._loop.stop)
        worker._loop_thread.join(timeout=5)
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


if __name__ == "__main__":
    unittest.main()
