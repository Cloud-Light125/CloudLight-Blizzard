from __future__ import annotations

import logging
import sys
import tempfile
import unittest
from concurrent.futures import TimeoutError as FutureTimeoutError
from pathlib import Path
from types import SimpleNamespace
from unittest import mock


SOOP_DIR = Path(__file__).resolve().parents[1]
DROPS_DIR = SOOP_DIR.parent
for path in (str(DROPS_DIR), str(SOOP_DIR)):
    if path not in sys.path:
        sys.path.insert(0, path)

from worker import SoopWorker


def _mission(idx: str, minutes: int, *, allowed_channel: str = "room") -> SimpleNamespace:
    item = SimpleNamespace(
        item_name=f"reward-{idx}",
        give_term=60,
        view_time=minutes,
        percent=min(100, minutes),
        mission_success=False,
    )
    return SimpleNamespace(
        drops_idx=idx,
        title=f"Mission {idx}",
        type_label="固定型",
        start_date="",
        end_date="",
        category_name="",
        category_no="",
        raw={},
        is_event_active=True,
        is_truly_ended=False,
        is_not_yet_open=False,
        items=[item],
        allowed_channel=allowed_channel,
        active_item=lambda: item,
    )


def _state(uid: str, mission: SimpleNamespace, *, channel: str = "room") -> SimpleNamespace:
    return SimpleNamespace(
        uid=uid,
        running=True,
        status="挂机中",
        channel_id=channel,
        channel_nick=channel,
        broad_no="1",
        connection_healthy=True,
        bridge_connected=True,
        heartbeat_status="success",
        heartbeat_last_success="now",
        network_uploaded=0,
        network_downloaded=0,
        network_last_minute_bps=0,
        missions=[mission],
        events=[],
        inventory=[],
        available_channels=[],
    )


class ProgressSyncTests(unittest.TestCase):
    def test_proxy_restart_preserves_running_account_subset(self) -> None:
        worker = object.__new__(SoopWorker)
        worker._manager = SimpleNamespace(running_uids=["alpha"])
        worker.stop = mock.Mock()
        worker.start_account = mock.Mock()
        worker._restart_manager()
        worker.stop.assert_called_once_with({})
        worker.start_account.assert_called_once_with({"uid": "alpha"})

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

    @staticmethod
    def _configure_channels(worker: SoopWorker, states: dict[str, SimpleNamespace]) -> None:
        worker._manager = SimpleNamespace(
            get_miner=lambda uid: SimpleNamespace(
                _current=SimpleNamespace(user_id=states[uid].channel_id)
            )
        )

        def missions_for_channel(missions, channel):
            return [mission for mission in missions if mission.allowed_channel == channel.user_id]

        worker._core = {"channel": SimpleNamespace(missions_for_channel=missions_for_channel)}

    def test_lower_remote_value_is_displayed_and_marked_rollback(self) -> None:
        with tempfile.TemporaryDirectory() as temporary:
            worker = SoopWorker(Path(temporary) / "data", Path(temporary) / "soop.log")
            try:
                first = _state("alpha", _mission("same", 10))
                second = _state("alpha", _mission("same", 0))
                self._configure_channels(worker, {"alpha": second})
                worker._states["alpha"] = first
                worker._observe_state_progress(first)
                worker._record_refresh_diagnostic("alpha", status="success")
                worker._states["alpha"] = second
                worker._observe_state_progress(second)

                progress = worker._state_to_dict(second)["currentProgress"]
                self.assertEqual(progress[0]["currentMinutes"], 0)
                self.assertEqual(progress[0]["percent"], 0)
                self.assertEqual(progress[0]["progressSource"], "remote")
                self.assertEqual(progress[0]["progressState"], "rollback")
                self.assertEqual(progress[0]["historicalHighWaterMinutes"], 10)
                self.assertIn("最新确认 viewTime=0", progress[0]["progressDiagnostic"])
                self.assertIn("高水位=10", progress[0]["progressDiagnostic"])
                self.assertTrue(progress[0]["lastConfirmedAt"])
                second.missions[0].items[0].item_name = "new reward stage"
                worker._observe_state_progress(second)
                changed = worker._current_progress_for_state(second)[0]
                self.assertNotEqual(changed["progressState"], "rollback")
                self.assertEqual(changed["historicalHighWaterMinutes"], 0)
            finally:
                self._close(worker)

    def test_account_progress_and_channel_qualification_are_isolated(self) -> None:
        with tempfile.TemporaryDirectory() as temporary:
            worker = SoopWorker(Path(temporary) / "data", Path(temporary) / "soop.log")
            try:
                first = _state("alpha", _mission("same", 10, allowed_channel="room"), channel="room")
                second = _state("beta", _mission("same", 2, allowed_channel="other"), channel="room")
                states = {"alpha": first, "beta": second}
                self._configure_channels(worker, states)
                for uid, state in states.items():
                    worker._states[uid] = state
                    worker._observe_state_progress(state)
                    worker._record_refresh_diagnostic(uid, status="success")

                alpha = worker._progress_details("alpha", first.missions[0], first.missions[0].items[0], 0, first)
                beta = worker._progress_details("beta", second.missions[0], second.missions[0].items[0], 0, second)
                self.assertEqual(alpha["confirmedMinutes"], 10)
                self.assertEqual(beta["confirmedMinutes"], 2)
                current = worker._current_progress_for_state(first)
                self.assertEqual(len(current), 1)
                self.assertEqual(current[0]["progressState"], "watching")
                self.assertEqual(current[0]["progressSource"], "remote")
                self.assertEqual(current[0]["lastConfirmedAt"], worker._refresh_diagnostics["alpha"]["lastConfirmedAt"])
                self.assertTrue(current[0]["watchEligible"])
                self.assertTrue(current[0]["heartbeatSuccess"])
                self.assertEqual(current[0]["heartbeatStatus"], "success")
                self.assertEqual(current[0]["heartbeatFailures"], 0)
                self.assertTrue(current[0]["connectionHealthy"])
                self.assertTrue(current[0]["bridgeConnected"])
                first.heartbeat_status = "unknown"
                unknown = worker._current_progress_for_state(first)[0]
                self.assertEqual(unknown["confirmedMinutes"], 10)
                self.assertEqual(unknown["progressState"], "waiting_server")
                self.assertFalse(unknown["heartbeatSuccess"])
                self.assertTrue(unknown["watchEligible"])
                self.assertEqual(worker._current_progress_for_state(second), [])
                self.assertEqual(worker._progress_details("beta", second.missions[0], second.missions[0].items[0], 0, second)["progressState"], "waiting_server")
            finally:
                self._close(worker)

    def test_refresh_failure_keeps_snapshot_and_submit_timeout_cancels_future(self) -> None:
        with tempfile.TemporaryDirectory() as temporary:
            worker = SoopWorker(Path(temporary) / "data", Path(temporary) / "soop.log")
            try:
                previous = _state("alpha", _mission("same", 10))
                worker._states["alpha"] = previous

                async def fail_refresh(cookies):
                    raise OSError("network unavailable")

                worker._core = {
                    "auth": SimpleNamespace(
                        list_accounts=lambda: ["alpha"],
                        load_cookies=lambda uid: {"uid": uid},
                    )
                }
                worker._refresh_saved_account = fail_refresh
                result = worker.refresh({})
                self.assertEqual(result["refreshStatus"], "failed")
                self.assertEqual(result["tasks"][0]["items"][0]["viewMinutes"], 10)
                self.assertEqual(result["refreshDiagnostics"][0]["status"], "failed")

                class _Future:
                    def __init__(self):
                        self.cancelled = False

                    def result(self, timeout=None):
                        raise FutureTimeoutError()

                    def cancel(self):
                        self.cancelled = True

                future = _Future()
                with mock.patch("worker.asyncio.run_coroutine_threadsafe", return_value=future):
                    with self.assertRaises(FutureTimeoutError):
                        worker._submit(object(), timeout=0.01)
                self.assertTrue(future.cancelled)
            finally:
                self._close(worker)


if __name__ == "__main__":
    unittest.main()
