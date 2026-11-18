from __future__ import annotations
import asyncio
import logging
import tempfile
import unittest
from pathlib import Path
from types import SimpleNamespace
from unittest.mock import AsyncMock

from test_campaign_scheduling import client, campaign
from worker import TwitchWorker
from claim_guard import claim_once, coordinator


class ClaimActions(unittest.IsolatedAsyncioTestCase):
    def ready_drop(self):
        c=client();cam=campaign(c,"ready",current=60)
        c.inventory=[cam];drop=next(iter(cam.drops));drop.claim_id="test-only-instance"
        return c,drop

    async def test_real_viewer_mutation_and_duplicate_auto_manual_clicks(self):
        c,drop=self.ready_drop();calls=[]
        async def gql(op):
            calls.append(op)
            await asyncio.sleep(.01)
            return {"data":{"claimDropRewards":{"status":"ELIGIBLE_FOR_ALL"}}}
        c.gql_request=gql
        self.assertEqual(await asyncio.gather(*(drop.claim() for _ in range(6))),[True]+[False]*5)
        self.assertEqual(len(calls),1)
        self.assertEqual(calls[0]["operationName"],"DropsPage_ClaimDropRewards")
        self.assertEqual(calls[0]["variables"],{"input":{"dropInstanceID":"test-only-instance"}})
        self.assertFalse(drop.is_claimed, "mutation acceptance cannot stand in for Inventory")

    async def test_timeout_cancellation_is_not_replayed(self):
        c,drop=self.ready_drop();calls=0
        async def uncertain():
            nonlocal calls
            calls+=1
            raise TimeoutError()
        self.assertFalse(await claim_once(drop,uncertain))
        self.assertFalse(await claim_once(drop,uncertain))
        self.assertEqual(calls,1)
        self.assertEqual(coordinator(c).pending(drop)["status"],"unconfirmed")
        c,drop=self.ready_drop()
        operation=AsyncMock(side_effect=asyncio.CancelledError())
        with self.assertRaises(asyncio.CancelledError):await claim_once(drop,operation)
        self.assertFalse(await claim_once(drop,operation))
        self.assertEqual(operation.await_count,1)

    async def test_failure_auth_permissions_rate_limit_and_already_claimed(self):
        for code,status in ((401,"authentication_error"),(403,"permission_error"),(429,"rate_limited"),(0,"unconfirmed")):
            c,drop=self.ready_drop()
            error=RuntimeError();error.status=code
            op=AsyncMock(side_effect=error)
            self.assertFalse(await claim_once(drop,op))
            self.assertEqual(coordinator(c).pending(drop)["status"],status)
            self.assertFalse(drop.is_claimed)
        c,drop=self.ready_drop();drop.is_claimed=True
        op=AsyncMock();self.assertTrue(await claim_once(drop,op));op.assert_not_awaited()

    async def test_incomplete_expired_and_failed_response_preserve_claimability(self):
        c=client();cam=campaign(c,"incomplete",current=1);drop=next(iter(cam.drops));op=AsyncMock()
        self.assertFalse(await claim_once(drop,op));op.assert_not_awaited()
        c,drop=self.ready_drop();c.gql_request=AsyncMock(return_value={"data":{"claimDropRewards":{"status":"NOT_ELIGIBLE"}}})
        self.assertFalse(await drop.claim());self.assertFalse(drop.is_claimed);self.assertTrue(drop.can_claim)
        self.assertFalse(await drop.claim());self.assertEqual(c.gql_request.await_count,1)
        from datetime import datetime,timedelta,timezone
        c,drop=self.ready_drop();drop.campaign.ends_at=datetime.now(timezone.utc)-timedelta(days=2)
        op=AsyncMock();self.assertFalse(await claim_once(drop,op));op.assert_not_awaited()

    async def test_worker_batch_independent_results_and_final_inventory(self):
        with tempfile.TemporaryDirectory() as folder:
            worker=TwitchWorker(Path(folder)/"data",Path(folder)/"worker.log")
            try:
                c=client();cams=[campaign(c,cid,current=60) for cid in ("ok","bad","other")]
                c.inventory=cams
                for cam in cams:next(iter(cam.drops)).claim_id=cam.id
                async def gql(op):
                    cid=op["variables"]["input"]["dropInstanceID"]
                    return {"data":{"claimDropRewards":{"status":"NOT_ELIGIBLE" if cid=="bad" else "ELIGIBLE_FOR_ALL"}}}
                async def inventory():
                    for cam in cams:
                        if cam.id in {"ok","other"} and getattr(next(iter(cam.drops)), "_claim_response_status", ""):
                            next(iter(cam.drops)).is_claimed=True
                c.gql_request=gql;c.fetch_inventory=AsyncMock(side_effect=inventory);worker._client=c;worker.running=True
                worker.load_state=lambda p:{"inventory":worker.get_inventory({})}
                result=await worker._claim_all_drops(c)
                self.assertEqual((result["success"],result["skipped"],result["failed"]),(2,0,1))
                self.assertEqual(len(result["results"]),3);self.assertIn("message",result["results"][1])
                self.assertEqual(c.fetch_inventory.await_count,2)
                self.assertFalse(next(iter(cams[1].drops)).is_claimed)
            finally:
                worker._client=None;worker._loop.call_soon_threadsafe(worker._loop.stop);worker._loop_thread.join(5)
                for handler in list(worker.logger.handlers):
                    worker.logger.removeHandler(handler);logging.getLogger("TwitchDrops").removeHandler(handler);handler.close()

    async def test_uncertain_worker_retry_queries_inventory_before_any_resubmit(self):
        with tempfile.TemporaryDirectory() as folder:
            worker=TwitchWorker(Path(folder)/"data",Path(folder)/"worker.log")
            try:
                c,drop=self.ready_drop();c.gql_request=AsyncMock(side_effect=TimeoutError());c.fetch_inventory=AsyncMock()
                worker._client=c;worker.running=True;worker.load_state=lambda p:{}
                first=await worker._claim_drop(c,drop.id)
                second=await worker._claim_drop(c,drop.id)
                self.assertEqual(first["status"],"unconfirmed");self.assertEqual(second["status"],"unconfirmed")
                self.assertEqual(c.gql_request.await_count,1);self.assertGreaterEqual(c.fetch_inventory.await_count,2)
                drop.is_claimed=True
                self.assertEqual((await worker._claim_drop(c,drop.id))["status"],"already_claimed")
                self.assertEqual(c.gql_request.await_count,1)
            finally:
                worker._client=None;worker._loop.call_soon_threadsafe(worker._loop.stop);worker._loop_thread.join(5)
                for handler in list(worker.logger.handlers):
                    worker.logger.removeHandler(handler);logging.getLogger("TwitchDrops").removeHandler(handler);handler.close()
