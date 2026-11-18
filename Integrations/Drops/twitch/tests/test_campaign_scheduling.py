"""Real Core state-machine integration with deterministic Twitch transport/clock."""
from __future__ import annotations

import asyncio
import copy
import logging
import sys
import tempfile
import unittest
from datetime import datetime, timedelta, timezone
from pathlib import Path
from types import SimpleNamespace
from unittest.mock import AsyncMock, Mock, patch

ROOT = Path(__file__).resolve().parents[1]
for path in (ROOT, ROOT / "core", ROOT.parent):
    sys.path.insert(0, str(path))
import headless_gui
sys.modules["gui"] = headless_gui
import constants
constants.LANG_PATH = ROOT / "core" / "lang"
from constants import State, PriorityMode
from inventory import DropsCampaign
from channel import Channel, Stream
from settings import default_settings
from settings_store import SettingsStore
from twitch import Twitch
from worker import TwitchWorker
from campaign_priority import ordered


def client():
    settings = SimpleNamespace(**copy.deepcopy(default_settings), dump=False, log=False, tray=False)
    settings.priority_mode = PriorityMode.ENDING_SOONEST
    settings.auto_claim_drops = False
    result = Twitch(settings)
    result.save = lambda **kw: None
    return result


def campaign(twitch, cid, end=5, required=60, current=0, game="1", channels=()):
    now = datetime.now(timezone.utc)
    start = (now-timedelta(days=1)).strftime("%Y-%m-%dT%H:%M:%SZ")
    finish = (now+timedelta(hours=end)).strftime("%Y-%m-%dT%H:%M:%SZ")
    return DropsCampaign(twitch, {
        "id": cid, "name": cid, "game": {"id": game, "name": "Overwatch" if game=="1" else "Minecraft", "slug": game, "boxArtURL": ""},
        "self": {"isAccountConnected": True}, "accountLinkURL": "", "startAt": start,
        "endAt": finish, "status": "ACTIVE", "allow": {"channels": [{"id": str(ch), "name": "channel"+str(ch)} for ch in channels]},
        "timeBasedDrops": [{"id": cid+"-drop", "name": cid+" reward", "startAt": start,
            "endAt": finish, "requiredMinutesWatched": required, "preconditionDrops": [],
            "benefitEdges": [{"benefit": {"id": cid+"-benefit", "name": "Reward", "distributionType": "DIRECT_ENTITLEMENT", "imageAssetURL": ""}}],
            "self": {"isClaimed": False, "dropInstanceID": None, "currentMinutesWatched": current}}],
    }, {})


def channel(twitch, campaign, channel_id):
    ch = Channel(twitch, id=channel_id, login="channel"+str(channel_id))
    ch._stream = Stream(ch, id=channel_id, game={"id": str(campaign.game.id), "name": campaign.game.name, "slug": str(campaign.game.id), "boxArtURL": ""}, viewers=10, title="Test")
    return ch


class SchedulingIntegration(unittest.IsolatedAsyncioTestCase):
    async def run_scheduler(self, twitch, channels, available=None):
        """Run inventory -> games -> channel discovery -> switch -> actual watch."""
        twitch.get_auth = AsyncMock(return_value=SimpleNamespace(user_id=123))
        twitch.websocket = SimpleNamespace(start=AsyncMock(), add_topics=Mock(), remove_topics=Mock())
        twitch.fetch_inventory = AsyncMock()
        async def check_online(items):
            for item in items:
                source=next((ch for ch in channels if ch.id==item.id),None)
                item._stream=source._stream if source is not None else None
        twitch.bulk_check_online = check_online
        twitch.get_live_streams = AsyncMock(return_value=channels)
        async def gql(op):
            cid = int(op["variables"]["channelID"])
            ids = available[cid] if available is not None else [c.id for c in twitch.inventory if c.can_earn(next(ch for ch in channels if ch.id==cid))]
            return {"data": {"channel": {"viewerDropCampaigns": [{"id": cid} for cid in ids]}}}
        twitch.gql_request = gql
        async def wait_forever():
            await asyncio.Event().wait()
        twitch._watch_loop = wait_forever
        original_watch = Twitch.watch.__get__(twitch)
        watched = []
        def watch(ch, **kwargs):
            original_watch(ch, **kwargs)
            watched.append(ch.id)
            asyncio.get_running_loop().call_soon(twitch.change_state, State.EXIT)
        twitch.watch = watch
        try:
            async with asyncio.timeout(3):
                await twitch._run()
        finally:
            if twitch._watching_task:
                twitch._watching_task.cancel()
                await asyncio.gather(twitch._watching_task, return_exceptions=True)
        self.assertTrue(watched, "real Worker state machine must execute watch")
        return watched[-1]

    async def test_custom_order_changes_actual_worker_watch_and_logs(self):
        for first in ("talon", "owcs"):
            c = client()
            a, b, third = campaign(c,"talon",channels=(1,)), campaign(c,"owcs",channels=(2,)), campaign(c,"third",channels=(3,))
            a.name="Reign of Talon S5 Mid";b.name="OWCS Stage 3 Playoffs"
            c.inventory=[b, third, a]
            c.settings.campaign_watch_priority={"1": {"mode":"custom","campaignIds":[first,"owcs" if first=="talon" else "talon","third"]}}
            with self.assertLogs("TwitchDrops", level="INFO") as logs:
                chosen=await self.run_scheduler(c,[channel(c,a,1),channel(c,b,2),channel(c,third,3)])
            self.assertEqual(chosen,1 if first=="talon" else 2)
            self.assertEqual(c._cloudlight_schedule["campaignId"],first)
            self.assertTrue(any("观看优先级 1："+(a.name if first=="talon" else b.name) in line for line in logs.output))
            print("\n".join(logs.output))

    async def test_all_automatic_modes_drive_worker(self):
        for mode,expected in (("ending",1),("remaining",2),("progress_high",2),("progress_low",1)):
            c=client();a=campaign(c,"a",end=1,required=120,current=0,channels=(1,));b=campaign(c,"b",end=4,required=60,current=50,channels=(2,))
            c.inventory=[a,b];c.settings.campaign_watch_priority={"1":{"mode":mode}}
            self.assertEqual(await self.run_scheduler(c,[channel(c,a,1),channel(c,b,2)]),expected)

    async def test_unavailable_completed_expired_ineligible_and_offline_are_skipped(self):
        for condition in ("no-channel","completed","expired","ineligible","offline","twitch-denied"):
            c=client();a=campaign(c,"a",channels=(1,));b=campaign(c,"b",channels=(2,));c.inventory=[a,b]
            c.settings.campaign_watch_priority={"1":{"mode":"custom","campaignIds":["a","b"]}}
            channels=[channel(c,a,1),channel(c,b,2)]
            if condition=="no-channel":channels.pop(0)
            if condition=="offline":channels[0]._stream=None
            if condition=="completed":next(iter(a.drops)).real_current_minutes=60
            if condition=="expired":a.ends_at=datetime.now(timezone.utc)-timedelta(hours=1)
            if condition=="ineligible":a.linked=False
            available={1:[] if condition=="twitch-denied" else ["a"],2:["b"]}
            self.assertEqual(await self.run_scheduler(c,channels,available),2,condition)

    async def test_manual_channel_override_survives_custom_priority(self):
        c=client();a=campaign(c,"a",channels=(1,));b=campaign(c,"b",channels=(2,));c.inventory=[a,b]
        c.settings.campaign_watch_priority={"1":{"mode":"custom","campaignIds":["a","b"]}}
        channels=[channel(c,a,1),channel(c,b,2)]
        c.gui.channels.get_selection=lambda:channels[1]
        self.assertEqual(await self.run_scheduler(c,channels),2)

    async def test_official_progress_completion_switches_without_inventing_minutes(self):
        c=client();a=campaign(c,"a",current=59,channels=(1,));b=campaign(c,"b",channels=(2,));c.inventory=[a,b]
        c._drops={d.id:d for cam in c.inventory for d in cam.drops};channels=[channel(c,a,1),channel(c,b,2)]
        c.wanted_games=[a.game];c.watching_channel.set(channels[0])
        await c.process_drops(123,{"type":"drop-progress","data":{"drop_id":"a-drop","current_progress_min":60,"required_progress_min":60}})
        self.assertEqual(next(iter(a.drops)).real_current_minutes,60)
        self.assertEqual(next(iter(b.drops)).real_current_minutes,0)
        self.assertEqual(await self.run_scheduler(c,channels),2)

    async def test_cooldown_and_shared_channel_do_not_oscillate(self):
        c=client();a=campaign(c,"a");b=campaign(c,"b");c.inventory=[a,b];c.wanted_games=[a.game]
        shared=channel(c,a,1);other=channel(c,a,2)
        for ch in (shared,other):ch._cloudlight_available_campaigns={"a","b"}
        c.watch(shared)
        self.assertFalse(c.should_switch(other))
        self.assertEqual(c._cloudlight_schedule["campaignId"],"a")
        self.assertEqual(next(iter(a.drops)).real_current_minutes,0)
        self.assertEqual(next(iter(b.drops)).real_current_minutes,0)

    async def test_worker_save_command_immediately_changes_schedule_and_survives_restart(self):
        with tempfile.TemporaryDirectory() as folder:
            data=Path(folder)/"data";log=Path(folder)/"worker.log"
            worker=TwitchWorker(data,log)
            def close(item):
                item._client=None;item._loop.call_soon_threadsafe(item._loop.stop);item._loop_thread.join(5)
                for handler in list(item.logger.handlers):
                    item.logger.removeHandler(handler);logging.getLogger("TwitchDrops").removeHandler(handler);handler.close()
            try:
                worker._settings.priority_mode=PriorityMode.ENDING_SOONEST
                c=client();c.settings=worker._settings;c._state=State.CHANNEL_SWITCH
                worker._client=c;worker.running=True
                a=campaign(c,"a",channels=(1,));b=campaign(c,"b",channels=(2,));c.inventory=[a,b]
                worker.save_settings({"settings":{"campaign_watch_priority":{"1":{"mode":"custom","campaignIds":["b","a"]},"2":{"mode":"remaining"}}}})
                await asyncio.sleep(.01)
                self.assertTrue(c._campaign_priority_changed)
                worker.save_settings({"settings":{"campaign_display_sort":"name"}})
                self.assertEqual(await self.run_scheduler(c,[channel(c,a,1),channel(c,b,2)]),2)
                self.assertEqual(c.settings.campaign_watch_priority["2"],{"mode":"remaining"})
            finally:close(worker)
            reconnected=TwitchWorker(data,log)
            try:
                self.assertEqual(reconnected._settings.campaign_watch_priority["1"]["campaignIds"],["b","a"])
                self.assertEqual(reconnected._settings.campaign_display_sort,"name")
            finally:close(reconnected)

    async def test_two_campaigns_can_keep_distinct_official_progress_on_shared_channel(self):
        c=client();a=campaign(c,"a");b=campaign(c,"b");c.inventory=[a,b];c.wanted_games=[a.game]
        c._drops={d.id:d for cam in c.inventory for d in cam.drops};shared=channel(c,a,1)
        shared._cloudlight_available_campaigns={"a","b"};c.watch(shared)
        for cid,minutes in (("a",5),("b",9)):
            await c.process_drops(123,{"type":"drop-progress","data":{"drop_id":cid+"-drop","current_progress_min":minutes,"required_progress_min":60}})
        self.assertEqual(next(iter(a.drops)).real_current_minutes,5)
        self.assertEqual(next(iter(b.drops)).real_current_minutes,9)


class Persistence(unittest.TestCase):
    def test_restart_new_campaign_other_games_unknown_fields_and_display_sort(self):
        with tempfile.TemporaryDirectory() as folder:
            path=Path(folder)/"settings.json"
            store=SettingsStore(path,default_settings);settings=store.load()
            settings["unknownFuture"]={"keep":True}
            settings["campaign_watch_priority"]={"1":{"mode":"custom","campaignIds":["b","missing","a"],"future":42},"2":{"mode":"remaining"}}
            settings["campaign_display_sort"]="name";store.save(settings)
            restored=SettingsStore(path,default_settings).load()
            self.assertEqual(restored["campaign_watch_priority"],settings["campaign_watch_priority"])
            c=client();campaigns=[campaign(c,"a"),campaign(c,"new"),campaign(c,"b")]
            self.assertEqual([x.id for x in ordered(campaigns,restored["campaign_watch_priority"])],["b","a","new"])
            self.assertEqual(restored["unknownFuture"],{"keep":True})
            restored["campaign_display_sort"]="ending"
            self.assertEqual([x.id for x in ordered(campaigns,restored["campaign_watch_priority"])],["b","a","new"])
            self.assertEqual(restored["campaign_watch_priority"]["2"],{"mode":"remaining"})
    def test_legacy_settings_migrate_and_invalid_priority_is_rejected(self):
        with tempfile.TemporaryDirectory() as folder:
            path=Path(folder)/"settings.json";path.write_text("{}")
            self.assertEqual(SettingsStore(path,default_settings).load()["campaign_watch_priority"],{})
            store=SettingsStore(path,default_settings);values=store.load()
            values["campaign_watch_priority"]={"1":{"mode":"custom","campaignIds":["x","x"]}}
            with self.assertRaises(Exception):store.encode(values)


if __name__=="__main__":unittest.main()
