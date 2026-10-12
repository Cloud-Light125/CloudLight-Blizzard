from __future__ import annotations

import json
import asyncio
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


def _mission(
    idx: str,
    minutes: int,
    *,
    allowed_channel: str = "room",
    item_id: str = "",
    give_term: int = 60,
    start_date: str = "",
    end_date: str = "",
) -> SimpleNamespace:
    item = SimpleNamespace(
        item_name=f"reward-{idx}",
        give_term=give_term,
        view_time=minutes,
        percent=min(100, minutes),
        mission_success=False,
        raw={"itemCodeIdx": item_id} if item_id else {},
    )
    return SimpleNamespace(
        drops_idx=idx,
        title=f"Mission {idx}",
        type_label="固定型",
        start_date=start_date,
        end_date=end_date,
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
    def test_stop_during_reauthentication_start_cleans_late_miner_only(self) -> None:
        with tempfile.TemporaryDirectory() as temporary:
            root = Path(temporary)
            worker = SoopWorker(root / "data", root / "worker.log")
            async def scenario():
                entered, release = asyncio.Event(), asyncio.Event()
                active = {"beta"}
                async def start(cookies):
                    entered.set()
                    await release.wait()
                    active.add("alpha")
                async def stop(uid):
                    active.discard(uid)
                manager = SimpleNamespace(start_account=start, stop_account_and_wait=stop)
                worker._manager = manager
                worker._core = {"auth": SimpleNamespace(login=mock.AsyncMock(return_value={"uid": "alpha"}))}
                worker._app_config = lambda: None
                worker._refresh_saved_account = mock.AsyncMock(return_value=SimpleNamespace(uid="alpha"))
                worker._state_callback = lambda _: None
                cancel = __import__("threading").Event()
                recovery = asyncio.create_task(worker._reauthenticate_once("alpha", "fixture", True, cancel))
                await entered.wait()
                cancel.set()
                stopping = asyncio.create_task(worker._stop_account_session(manager, "alpha"))
                await asyncio.sleep(0)
                self.assertFalse(stopping.done())
                release.set()
                await asyncio.wait_for(asyncio.gather(recovery, stopping), 2)
                self.assertEqual(active, {"beta"})
                await worker._start_account_session(manager, {"uid":"alpha"}, "alpha")
                self.assertEqual(active, {"alpha", "beta"})
            try:
                asyncio.run(scenario())
            finally:
                self._close(worker)
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
                details = worker._progress_details(
                    "alpha", second.missions[0], second.missions[0].items[0], 0, second
                )
                rollback_at = details["rollbackAt"]
                before_minutes = details["beforeMinutes"]
                after_minutes = details["afterMinutes"]
                queried_at = details["queriedAt"]
                worker._observe_state_progress(second)
                repeated = worker._progress_details(
                    "alpha", second.missions[0], second.missions[0].items[0], 0, second
                )
                self.assertEqual(repeated["rollbackAt"], rollback_at)
                self.assertEqual(repeated["beforeMinutes"], before_minutes)
                self.assertEqual(repeated["afterMinutes"], after_minutes)
                self.assertEqual(repeated["queriedAt"], queried_at)
                second.missions[0].items[0].item_name = "new reward stage"
                worker._observe_state_progress(second)
                changed = worker._current_progress_for_state(second)[0]
                self.assertEqual(changed["progressState"], "rollback")
                self.assertEqual(changed["historicalHighWaterMinutes"], 10)
            finally:
                self._close(worker)

    def test_progress_history_survives_restart_without_promoting_cache(self) -> None:
        with tempfile.TemporaryDirectory() as temporary:
            data_dir = Path(temporary) / "data"
            first_worker = SoopWorker(data_dir, Path(temporary) / "first.log")
            try:
                first = _state("alpha", _mission("same", 10, item_id="api-stage-1"))
                self._configure_channels(first_worker, {"alpha": first})
                first_worker._states["alpha"] = first
                first_worker._observe_state_progress(first)
                first_worker._record_refresh_diagnostic("alpha", status="success")
                store = json.loads(first_worker._progress_store_path.read_text(encoding="utf-8"))
                self.assertEqual(store["records"][0]["stageId"], "api-stage-1")
                self.assertNotIn("reward-same", json.dumps(store, ensure_ascii=False))
            finally:
                self._close(first_worker)

            second_worker = SoopWorker(data_dir, Path(temporary) / "second.log")
            try:
                second = _state("alpha", _mission("same", 0, item_id="api-stage-1"))
                second.missions[0].items[0].item_name = "renamed reward"
                self._configure_channels(second_worker, {"alpha": second})
                second_worker._states["alpha"] = second
                second_worker._observe_state_progress(second)

                cached = second_worker._progress_details(
                    "alpha", second.missions[0], second.missions[0].items[0], 0, second
                )
                self.assertEqual(cached["currentMinutes"], 0)
                self.assertEqual(cached["confirmedMinutes"], 0)
                self.assertEqual(cached["historicalHighWaterMinutes"], 10)
                self.assertEqual(cached["progressSource"], "cache")
                self.assertEqual(cached["progressState"], "unavailable")

                second_worker._record_refresh_diagnostic("alpha", status="success")
                refreshed = second_worker._progress_details(
                    "alpha", second.missions[0], second.missions[0].items[0], 0, second
                )
                self.assertEqual(refreshed["currentMinutes"], 0)
                self.assertEqual(refreshed["historicalHighWaterMinutes"], 10)
                self.assertEqual(refreshed["progressSource"], "remote")
                self.assertEqual(refreshed["progressState"], "rollback")
            finally:
                self._close(second_worker)

            history_worker = SoopWorker(data_dir, Path(temporary) / "history.log")
            try:
                history = history_worker.load_state({})["progressHistory"]
                self.assertEqual(len(history), 1)
                self.assertEqual(history[0]["uid"], "alpha")
                self.assertEqual(history[0]["taskId"], "same")
                self.assertEqual(history[0]["stageId"], "api-stage-1")
                self.assertEqual(history[0]["officialMinutes"], 0)
                self.assertEqual(history[0]["historicalHighWaterMinutes"], 10)
                self.assertEqual(history[0]["source"], "cache")
            finally:
                self._close(history_worker)

    def test_progress_identity_keeps_cycle_and_reward_terms_distinct(self) -> None:
        with tempfile.TemporaryDirectory() as temporary:
            worker = SoopWorker(Path(temporary) / "data", Path(temporary) / "soop.log")
            try:
                mission = _mission(
                    "multi", 10, item_id="same-code",
                    start_date="2026-10-01", end_date="2026-10-10",
                )
                state = _state("alpha", mission)
                worker._observe_state_progress(state)

                mission.end_date = "2026-10-31"
                mission.items[0].item_name = "renamed stage"
                mission.items[0].view_time = 0
                worker._observe_state_progress(state)
                same_term = [
                    (key, observation)
                    for key, observation in worker._progress_observations.items()
                    if key[1] == "multi"
                ]
                self.assertEqual(len(same_term), 1)
                self.assertEqual(same_term[0][1].historical_high_water_minutes, 10)

                second_item = SimpleNamespace(
                    item_name="another stage",
                    give_term=120,
                    view_time=3,
                    percent=3,
                    mission_success=False,
                    raw={"itemCodeIdx": "same-code"},
                )
                mission.items.append(second_item)
                worker._observe_state_progress(state)
                terms = {
                    key[-1]
                    for key in worker._progress_observations
                    if key[1] == "multi"
                }
                self.assertEqual(terms, {60, 120})
            finally:
                self._close(worker)

    def test_refresh_does_not_promote_absent_historical_stage(self) -> None:
        with tempfile.TemporaryDirectory() as temporary:
            worker = SoopWorker(Path(temporary) / "data", Path(temporary) / "soop.log")
            try:
                mission = _mission("old", 10, item_id="old-stage")
                state = _state("alpha", mission)
                worker._states["alpha"] = state
                worker._observe_state_progress(state)
                worker._record_refresh_diagnostic("alpha", status="success", state=state)
                key = next(iter(worker._progress_observations))
                self.assertEqual(worker._progress_observations[key].source, "remote")

                mission.items = []
                worker._record_refresh_diagnostic("alpha", status="success", state=state)
                self.assertEqual(worker._progress_observations[key].source, "cache")
            finally:
                self._close(worker)

    def test_corrupt_history_degrades_and_account_delete_cleans_it(self) -> None:
        with tempfile.TemporaryDirectory() as temporary:
            data_dir = Path(temporary) / "data"
            data_dir.mkdir(parents=True)
            store_path = data_dir / "progress_diagnostics.json"
            store_path.write_text("{broken", encoding="utf-8")
            worker = SoopWorker(data_dir, Path(temporary) / "soop.log")
            try:
                self.assertEqual(worker._progress_observations, {})
                state = _state("alpha", _mission("same", 10, item_id="api-stage-1"))
                worker._states["alpha"] = state
                worker._observe_state_progress(state)
                worker._record_refresh_diagnostic("alpha", status="success")

                accounts = ["alpha"]
                worker._core = {"auth": SimpleNamespace(
                    list_accounts=lambda: list(accounts),
                    remove_account=lambda uid: accounts.remove(uid) or True,
                )}
                worker.delete_account({"userid": "alpha"})
                persisted = json.loads(store_path.read_text(encoding="utf-8"))
                self.assertEqual(persisted["records"], [])
                self.assertNotIn("alpha", persisted["refreshDiagnostics"])
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
                worker._record_refresh_diagnostic("alpha", status="failed", error="access_token=private-test-token")
                worker._persist_progress_diagnostics(force=True)
                self.assertNotIn("private-test-token", worker._progress_store_path.read_text(encoding="utf-8"))

                class _Future:
                    def __init__(self):
                        self.cancelled = False

                    def result(self, timeout=None):
                        raise FutureTimeoutError()

                    def cancel(self):
                        self.cancelled = True

                future = _Future()
                def reject_submission(coroutine, loop):
                    coroutine.close()
                    return future
                with mock.patch("worker.asyncio.run_coroutine_threadsafe", side_effect=reject_submission):
                    with self.assertRaises(FutureTimeoutError):
                        worker._submit(object(), timeout=0.01)
                self.assertTrue(future.cancelled)
            finally:
                self._close(worker)


if __name__ == "__main__":
    unittest.main()
