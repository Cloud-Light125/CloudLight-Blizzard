"""Fault injection at the actual Worker/Core transaction boundary, no network."""
from __future__ import annotations
import asyncio
import logging
import tempfile
import unittest
from pathlib import Path
from types import SimpleNamespace
from unittest.mock import AsyncMock, Mock

from test_campaign_scheduling import client, campaign, channel
from worker import TwitchWorker
from claim_guard import coordinator


class ClaimAudit(unittest.IsolatedAsyncioTestCase):
    async def asyncSetUp(self):
        self.temp = tempfile.TemporaryDirectory()
        self.worker = TwitchWorker(Path(self.temp.name)/"data", Path(self.temp.name)/"worker.log")
        self.c = client()
        self.c.inventory = [campaign(self.c, "a", current=60), campaign(self.c, "b", current=60)]
        for cam in self.c.inventory:
            next(iter(cam.drops)).claim_id = cam.id
        self.worker._client = self.c
        self.worker.running = True
        self.worker.load_state = lambda p: {"inventory": self.worker.get_inventory({})}
        self.sent = []
        self.remote_claimed = set()
        async def gql(op):
            cid = op["variables"]["input"]["dropInstanceID"]
            self.sent.append(cid)
            self.remote_claimed.add(cid)
            await asyncio.sleep(.005)
            return {"data": {"claimDropRewards": {"status": "ELIGIBLE_FOR_ALL"}}}
        async def inventory():
            for cam in self.c.inventory:
                next(iter(cam.drops)).is_claimed = cam.id in self.remote_claimed
        self.c.gql_request = gql
        self.c.fetch_inventory = AsyncMock(side_effect=inventory)

    async def asyncTearDown(self):
        await coordinator(self.c).cancel_pending()
        self.worker._client = None
        self.worker._loop.call_soon_threadsafe(self.worker._loop.stop)
        self.worker._loop_thread.join(5)
        for handler in list(self.worker.logger.handlers):
            self.worker.logger.removeHandler(handler)
            logging.getLogger("TwitchDrops").removeHandler(handler)
            handler.close()
        self.temp.cleanup()

    async def test_duplicate_worker_calls_have_one_mutation_and_official_confirmation(self):
        self.c.gui.tray.notify=Mock()
        results = await asyncio.gather(*(self.worker._claim_drop(self.c, "a-drop", campaign_id="a") for _ in range(5)))
        self.assertEqual(self.sent, ["a"])
        self.assertEqual([r["status"] for r in results], ["claimed"]+["already_claimed"]*4)
        self.c.gui.tray.notify.assert_called_once()

    async def test_game_ids_survive_actual_worker_snapshot_projection(self):
        campaigns=self.worker.get_campaigns({})
        inventory=self.worker.get_inventory({})
        self.assertTrue(all(c["gameId"]=="1" for c in campaigns))
        self.assertTrue(all(d["gameId"]=="1" for d in inventory))

    async def test_batch_fetches_official_set_before_using_stale_cards(self):
        self.remote_claimed.add("a")
        result = await self.worker._claim_all_drops(self.c)
        self.assertEqual(self.sent, ["b"])
        self.assertEqual((result["total"], result["success"]), (1, 1))

    async def test_batch_initial_inventory_failure_submits_nothing(self):
        self.c.fetch_inventory.side_effect = TimeoutError()
        result = await self.worker._claim_all_drops(self.c)
        self.assertEqual(result["status"], "inventory_error")
        self.assertEqual(self.sent, [])

    async def test_accepted_claim_failed_verification_never_marks_card_claimed(self):
        self.c.gui.tray.notify=Mock()
        self.c.fetch_inventory.side_effect = [None, TimeoutError()]
        result = await self.worker._claim_drop(self.c, "a-drop")
        self.assertEqual(result["status"], "unconfirmed")
        self.assertFalse(next(iter(self.c.inventory[0].drops)).is_claimed)
        self.assertFalse(result["state"]["inventory"][0]["claimed"])
        self.assertEqual(self.sent, ["a"])
        self.c.gui.tray.notify.assert_not_called()

    async def test_batch_accepted_claims_failed_inventory_are_unknown(self):
        self.c.fetch_inventory.side_effect = [None, TimeoutError()]
        result = await self.worker._claim_all_drops(self.c)
        self.assertEqual((result["success"], result["unknown"], result["failed"]), (0, 2, 0))

    async def test_response_lost_is_reconciled_by_official_inventory_without_replay(self):
        async def lost(op):
            self.sent.append("a"); self.remote_claimed.add("a"); raise TimeoutError()
        self.c.gql_request = lost
        result = await self.worker._claim_drop(self.c, "a-drop")
        self.assertEqual(result["status"], "claimed")
        result = await self.worker._claim_drop(self.c, "a-drop")
        self.assertEqual(result["status"], "already_claimed")
        self.assertEqual(self.sent, ["a"])

    async def test_timeout_and_late_inventory_never_auto_replay(self):
        self.c.gql_request = AsyncMock(side_effect=TimeoutError())
        first = await self.worker._claim_drop(self.c, "a-drop")
        second = await self.worker._claim_drop(self.c, "a-drop")
        self.assertEqual((first["status"], second["status"]), ("unconfirmed", "unconfirmed"))
        self.assertEqual(self.c.gql_request.await_count, 1)

    async def test_batch_and_single_interleaving_share_one_transaction(self):
        batch = asyncio.create_task(self.worker._claim_all_drops(self.c))
        await asyncio.sleep(.02)
        single = asyncio.create_task(self.worker._claim_drop(self.c, "a-drop"))
        other_batch = await self.worker._claim_all_drops(self.c)
        result, single_result = await asyncio.gather(batch, single)
        self.assertEqual(other_batch["status"], "busy")
        self.assertEqual(self.sent, ["a", "b"])
        self.assertEqual(single_result["status"], "already_claimed")

    async def test_cancel_after_first_submission_preserves_remaining_not_submitted(self):
        original = self.c.gql_request
        async def stop_after_one(op):
            result = await original(op)
            self.worker._controls_pending = 1
            return result
        self.c.gql_request = stop_after_one
        result = await self.worker._claim_all_drops(self.c)
        self.assertEqual(self.sent, ["a"])
        self.assertEqual((result["success"], result["pending"]), (1, 1))

    async def test_shutdown_cancels_owned_task_and_preserves_uncertain_intent(self):
        entered = asyncio.Event()
        async def hanging(op):
            self.sent.append("a"); entered.set(); await asyncio.Event().wait()
        self.c.gql_request = hanging
        task = asyncio.create_task(self.worker._claim_drop(self.c, "a-drop"))
        await entered.wait()
        await coordinator(self.c).cancel_pending()
        self.assertTrue(task.cancelled())
        drop = next(iter(self.c.inventory[0].drops))
        self.assertEqual(coordinator(self.c).pending(drop)["status"], "unconfirmed")
        self.assertEqual(self.sent, ["a"])

    async def test_worker_restart_loads_uncertain_journal_without_resubmitting(self):
        self.c.settings._store = SimpleNamespace(path=Path(self.temp.name)/"settings.json")
        drop = next(iter(self.c.inventory[0].drops))
        self.c.gql_request = AsyncMock(side_effect=TimeoutError())
        await self.worker._claim_drop(self.c, drop.id)
        self.assertEqual(self.c.gql_request.await_count, 1)
        del self.c._claim_coordinator
        self.c._claim_results = {}
        result = await self.worker._claim_drop(self.c, drop.id)
        self.assertEqual(result["status"], "unconfirmed")
        self.assertEqual(self.c.gql_request.await_count, 1)
        self.assertNotIn('dropInstanceID', (Path(self.temp.name)/"claim-journal.json").read_text())

    async def test_campaign_id_mismatch_and_ambiguous_drop_fail_closed(self):
        result = await self.worker._claim_drop(self.c, "a-drop", campaign_id="b")
        self.assertEqual(result["status"], "not_found")
        next(iter(self.c.inventory[1].drops)).id = "a-drop"
        result = await self.worker._claim_drop(self.c, "a-drop")
        self.assertEqual(result["status"], "not_found")
        self.assertEqual(self.sent, [])

    async def test_ineligible_and_incomplete_never_reach_transport(self):
        self.c.inventory[0].linked = False
        result = await self.worker._claim_drop(self.c, "a-drop")
        self.assertEqual(result["status"], "not_claimable")
        drop = next(iter(self.c.inventory[1].drops)); drop.claim_id = None; drop.real_current_minutes = 1
        result = await self.worker._claim_drop(self.c, "b-drop")
        self.assertEqual(result["status"], "not_claimable")
        self.assertEqual(self.sent, [])

    async def test_instance_identifier_alone_does_not_complete_unfinished_reward(self):
        drop = next(iter(self.c.inventory[0].drops)); drop.real_current_minutes = 1
        self.assertFalse(drop.can_claim)
        result = await self.worker._claim_drop(self.c, drop.id)
        self.assertEqual(result["status"], "not_claimable")
        self.assertEqual(self.sent, [])

    async def test_multiple_benefits_do_not_duplicate_mutation_or_minutes(self):
        drop = next(iter(self.c.inventory[0].drops))
        drop.benefits *= 3
        result = await self.worker._claim_all_drops(self.c)
        self.assertEqual(self.sent, ["a", "b"])
        self.assertEqual(result["total"], 2)
        self.assertEqual(drop.real_current_minutes, 60)

    async def test_one_observed_benefit_cannot_mark_a_multiple_benefit_drop_claimed(self):
        from inventory import TimedDrop
        from datetime import datetime,timezone,timedelta
        cam=self.c.inventory[0]
        stamp=datetime.now(timezone.utc)
        data={"id":"multi","name":"multi","startAt":(stamp-timedelta(hours=1)).strftime("%Y-%m-%dT%H:%M:%SZ"),
              "endAt":(stamp+timedelta(hours=1)).strftime("%Y-%m-%dT%H:%M:%SZ"),"requiredMinutesWatched":60,"preconditionDrops":[],
              "benefitEdges":[{"benefit":{"id":bid,"name":bid,"distributionType":"DIRECT_ENTITLEMENT","imageAssetURL":""}} for bid in ("one","two")]}
        self.assertFalse(TimedDrop(cam,data,{"one":stamp}).is_claimed)
        self.assertTrue(TimedDrop(cam,data,{"one":stamp,"two":stamp}).is_claimed)

    async def test_no_auto_claim_survives_inventory_schedule_and_worker_restart(self):
        drop = next(iter(self.c.inventory[0].drops))
        await self.c.claim_drop_if_enabled(drop)
        self.c.wanted_games = [drop.campaign.game]
        ch = channel(self.c, drop.campaign, 1); ch._cloudlight_available_campaigns = {"a", "b"}
        self.c.watch(ch); self.c.stop_watching()
        await self.c.fetch_inventory()
        self.worker.save_settings({"settings": {"auto_claim_drops": False, "campaign_watch_priority": {"1": {"mode": "remaining"}}}})
        self.assertFalse(self.worker._settings.auto_claim_drops)
        self.assertEqual(self.sent, [])

    async def test_existing_opted_in_auto_claim_waits_for_inventory_confirmation(self):
        self.c.settings.auto_claim_drops=True
        drop=next(iter(self.c.inventory[0].drops))
        self.assertTrue(await self.c.claim_drop_if_enabled(drop))
        self.assertTrue(drop.is_claimed)
        self.assertEqual(self.sent,["a"])
        self.c.fetch_inventory.assert_awaited_once()

    async def test_corrupt_claim_journal_fails_closed_without_transport(self):
        path=Path(self.temp.name)/"claim-journal.json"
        self.c.settings._store=SimpleNamespace(path=path.with_name("settings.json"))
        for content in ('{invalid','[]','{"old":"claimed"}'):
            if hasattr(self.c,"_claim_coordinator"):del self.c._claim_coordinator
            path.write_text(content,encoding="utf-8")
            for _ in range(2):
                with self.assertRaises(ValueError):
                    await self.worker._claim_drop(self.c,"a-drop")
            self.assertEqual(path.read_text(),content)
        self.assertEqual(self.sent,[])

    async def test_shared_channel_target_changes_after_official_completion(self):
        a, b = self.c.inventory
        for cam in (a,b):
            drop = next(iter(cam.drops)); drop.claim_id = None; drop.real_current_minutes = 1
        self.c.wanted_games = [a.game]
        ch = channel(self.c, a, 1); ch._cloudlight_available_campaigns = {"a", "b"}
        self.c.watch(ch)
        next(iter(a.drops)).real_current_minutes = 60
        self.c.choose_watch_channel([ch])
        self.assertEqual(self.c._cloudlight_schedule["campaignId"], "b")
        self.assertEqual(self.c._cloudlight_schedule["dropId"], "b-drop")
        self.assertEqual(self.c._cloudlight_schedule["eligibleCampaignIds"], ["a", "b"])
