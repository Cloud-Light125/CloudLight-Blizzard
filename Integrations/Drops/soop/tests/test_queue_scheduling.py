"""Integration replay against the actual Core + CloudLight adapter; no network."""
import asyncio
import copy
import importlib
import json
import sys
import tempfile
import unittest
from pathlib import Path
from types import SimpleNamespace
from unittest.mock import AsyncMock, patch

SOOP = Path(__file__).resolve().parents[1]
sys.path.insert(0, str(SOOP))
from worker import SoopWorker
from scheduling import matches, metadata, queue
import test_worker_contract as contract


class QueueIntegration(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory()
        root = Path(self.temp.name)
        self.events = patch('worker.event').start()
        self.worker = SoopWorker(root/'data', root/'test.log')
        self.assertTrue(self.worker._core, self.worker._core_error)
        self.models = importlib.import_module('cloudlight_soop_core.models')
        self.channels = self.worker._core['channel']
        self.worker.settings.update(campaign_selection_mode='selected_only', selected_games=['game:1','game:2','game:3','game:4'],
            game_priority=['game:1','game:2','game:3','game:4'], campaign_priorities={'game:1':['a','b'], 'game:2':['c'], 'game:3':['d'], 'game:4':['e']})
        self.miners = []

    def tearDown(self):
        contract.SoopWorkerContractTests._close(self.worker)
        patch.stopall()
        self.temp.cleanup()

    def raw(self, id='a', game='1', **changes):
        value = dict(dropsIdx=id, gameNo=game, title='Identical title', giveCon='term', ingameGiveYn='N',
            startDate='2026-01-01', endDate='2099-01-01', filter='progress',
            broadIdList=[dict(userId='room-'+id, onAir=True, broadNo='123')], cateList=[],
            itemList=[dict(itemName='Reward', giveTerm=60, viewTime=10, percent=16, missionSuccess=False)])
        value.update(changes)
        return value

    def miner(self, uid='A', rows=None):
        miner = self.worker._core['miner'].SoopMiner({'BbsTicket':uid}, channel_config=self.worker._channel_config(), app_config=self.worker._app_config())
        miner._ensure_session = AsyncMock()
        miner._session = object()
        miner._missions = [self.models.Mission.from_api(r) for r in (rows or [self.raw(), self.raw('b'), self.raw('c','2')])]
        miner._events = [self.models.DropEvent.from_api(r) for r in (rows or [self.raw(), self.raw('b'), self.raw('c','2')])]
        self.miners.append(miner)
        return miner

    def live(self, id):
        return self.models.LiveChannel(id,id,'123',True,category_no='001',has_drops=True)

    def resolve(self, miner, offline=()):
        async def fetch(session,cookies,id,**kw): return None if id in offline else self.live(id)
        with patch.object(self.channels,'fetch_live_channel',side_effect=fetch):
            return self.worker._submit(miner._resolve_channel())

    def test_same_title_different_ids_and_four_games(self):
        miner = self.miner(rows=[self.raw(),self.raw('b'),self.raw('c','2'),self.raw('d','3'),self.raw('e','4')])
        rows = self.worker._tasks_for_state(miner.get_state(),include_ended=True)
        self.assertEqual([r['id'] for r in queue(rows,self.worker.settings)],['a','b','c','d','e'])
        self.assertEqual(self.resolve(miner).user_id,'room-a')
        self.worker.settings['game_priority'] = ['game:4','game:3','game:2','game:1']
        self.assertEqual(self.resolve(miner).user_id,'room-e')

    def test_completed_offline_end_and_unlinked_fallback(self):
        for change in ({'itemList':[dict(itemName='R',giveTerm=60,viewTime=60,percent=100,missionSuccess=True)]},
                       {'endDate':'2020-01-01'}, {'startDate':'2098-01-01'}, {'ingameGiveYn':'Y','acctConn':False}):
            miner = self.miner(rows=[self.raw(**change),self.raw('b')])
            self.assertEqual(self.resolve(miner).user_id,'room-b')
        miner = self.miner()
        self.assertEqual(self.resolve(miner,offline=['room-a','room-b']).user_id,'room-c')
        self.assertEqual(self.worker.settings['campaign_priorities']['game:1'],['a','b'])

    def test_uid_progress_and_completion_are_independent(self):
        a = self.miner('A', [self.raw(itemList=[dict(itemName='R',giveTerm=60,viewTime=60,percent=100,missionSuccess=True)]),self.raw('b')])
        b = self.miner('B')
        self.assertEqual(self.resolve(a).user_id,'room-b')
        self.assertEqual(self.resolve(b).user_id,'room-a')
        self.assertEqual(self.worker._queue_targets['A']['campaignId'],'b')
        self.assertEqual(self.worker._queue_targets['B']['campaignId'],'a')
        self.assertEqual(self.worker._tasks_for_state(b.get_state())[0]['items'][0]['viewMinutes'],10)
        self.assertTrue(self.worker._tasks_for_state(a.get_state())[0]['completed'])

    def test_random_no_fake_progress_and_unknown_rules_block(self):
        miner = self.miner(rows=[self.raw(giveCon='none',itemList=[{'itemName':'Lottery'}]),self.raw('b',broadIdList=[],cateList=[])])
        rows = self.worker._tasks_for_state(miner.get_state())
        self.assertIsNone(rows[0]['items'][0]['viewMinutes'])
        self.assertIn('不代表必定',rows[0]['randomNotice'])
        self.assertFalse(rows[1]['executable'])
        self.assertEqual(self.resolve(miner).user_id,'room-a')

    def test_category_exact_id_no_fuzzy_title(self):
        miner = self.miner(rows=[self.raw(broadIdList=[],cateList=[{'cateNo':'001','cateName':'Game'}])])
        row = self.worker._tasks_for_state(miner.get_state())[0]
        good = self.live('category-room')
        wrong = self.live('wrong'); wrong.category_no='002'; wrong.category_names=['Game']
        self.assertTrue(matches(row,good)); self.assertFalse(matches(row,wrong))
        with patch.object(self.channels,'fetch_hashtag_drops_channels',new=AsyncMock(return_value=[wrong,good])):
            self.assertEqual(self.worker._submit(miner._resolve_channel()).user_id,'category-room')
        self.assertNotEqual(metadata({'title':'same'},'1')['gameId'],metadata({'title':'same'},'2')['gameId'])

    def test_manual_override_and_ow_restriction(self):
        miner = self.miner(); miner._channel_config.mode='manual'
        with patch.object(self.channels,'_pick_manual_channel',new=AsyncMock(return_value=self.live('manual'))):
            self.assertEqual(self.worker._submit(miner._resolve_channel()).user_id,'manual')
            self.assertEqual(self.worker._queue_targets['A']['campaignId'],'')
        miner._channel_config.mode='owesports'
        with patch.object(self.channels,'_pick_owesports_channel',new=AsyncMock(return_value=self.live('owesports'))):
            self.assertIsNone(self.worker._submit(miner._resolve_channel()))
        self.assertEqual(self.worker.settings['channel_mode'],'smart')

    def test_cooldown_only_retains_valid_live_current_target(self):
        miner=self.miner(); self.resolve(miner)
        miner._current=self.live('room-a')
        self.worker.settings['campaign_priorities']['game:1']=['b','a']
        self.assertEqual(self.resolve(miner).user_id,'room-a')
        self.assertEqual(self.resolve(miner,offline=['room-a']).user_id,'room-b')
        miner._missions[0].items[0].mission_success=True
        self.assertEqual(self.resolve(miner).user_id,'room-b')

    def test_no_target_closes_heartbeat_and_bridge(self):
        miner=self.miner(); miner._current=self.live('room-a'); miner._heartbeat=self.worker._core['miner'].WatchHeartbeat('A',miner._current)
        bridge=SimpleNamespace(close=AsyncMock(),is_connected=True,seconds_since_last_activity=0); miner._bridge=bridge
        self.worker.settings['selected_games']=[]
        self.worker._submit(miner._sync_channel())
        self.assertIsNone(miner._current); self.assertIsNone(miner._heartbeat)
        bridge.close.assert_awaited_once()

    def test_catalog_only_fixed_joins_on_exact_id_not_title(self):
        miner=self.miner(rows=[self.raw()]); miner._events=[self.models.DropEvent.from_api(self.raw('b'))]
        rows=self.worker._tasks_for_state(miner.get_state())
        self.assertEqual(len(rows),2)
        self.assertTrue(rows[1]['eventOnly']); self.assertIsNone(rows[1]['items'][0]['viewMinutes'])
        self.worker.settings['campaign_priorities']['game:1']=['b','a']
        self.assertEqual(self.resolve(miner).user_id,'room-b')

    def test_strategies_use_only_official_time(self):
        miner=self.miner(rows=[self.raw(),self.raw('b',itemList=[dict(itemName='R',giveTerm=30,viewTime=20,percent=66,missionSuccess=False)]),self.raw('x',giveCon='none')])
        rows=self.worker._tasks_for_state(miner.get_state())
        self.worker.settings['campaign_priorities']['game:1']=['a','x','b']
        for mode,first in [('remaining','b'),('most','b'),('least','a'),('custom','a'),('ending','a')]:
            self.worker.settings['campaign_sort_mode']=mode
            self.assertEqual(queue(rows,self.worker.settings)[0]['id'],first)

    def test_readonly_refresh_atomic_failure_and_history(self):
        miner=self.miner(); previous=miner._missions
        miner._drops=SimpleNamespace(_request=AsyncMock(return_value={'data':[]}),get_missions=AsyncMock(return_value=[]),get_events=AsyncMock(side_effect=ConnectionError('offline')),get_inventory=AsyncMock())
        with self.assertRaises(ConnectionError): self.worker._submit(miner.force_refresh())
        self.assertIs(miner._missions,previous)
        miner._drops.get_events=AsyncMock(return_value=[self.models.DropEvent.from_api(self.raw('old',endDate='2020-01-01'))])
        miner._drops.get_inventory=AsyncMock(return_value=[])
        with patch.object(self.channels,'fetch_live_drops_channels',new=AsyncMock(return_value=([],[]))): state=self.worker._submit(miner.force_refresh())
        self.assertTrue(self.worker._tasks_for_state(state,include_ended=True)[0]['ended'])
        self.assertEqual(self.worker._tasks_for_state(state),[])
        miner._drops.get_inventory.assert_awaited_once_with(with_codes=False)

    def test_save_and_process_restart_preserves_queue_and_legacy(self):
        expected=copy.deepcopy(self.worker.settings)
        self.worker.save_settings({'settings':expected})
        contract.SoopWorkerContractTests._close(self.worker)
        root=Path(self.temp.name)
        self.worker=SoopWorker(root/'data',root/'test.log')
        self.assertEqual(self.worker.settings['campaign_priorities'],expected['campaign_priorities'])
        self.assertEqual(self.worker.settings['game_priority'],expected['game_priority'])
        self.worker.settings.update(campaign_selection_mode='legacy',priority_mission_id='c')
        self.assertEqual(self.resolve(self.miner()).user_id,'room-c')
        self.worker._normalize_priority_mission_id(fallback_if_missing=True)
        self.assertEqual(self.worker.settings['priority_mission_id'],'c')

    def test_real_run_keeps_waiting_without_starting_a_room(self):
        miner=self.miner(); miner._drops=SimpleNamespace(ensure_drops_ready=AsyncMock())
        miner._refresh_task_snapshot=AsyncMock(); miner._refresh_inventory=AsyncMock()
        observed=[]
        async def heartbeat(): await miner._stop.wait()
        async def poll():
            observed.append((miner._running,miner._current))
            miner._stop.set()
        miner._heartbeat_loop=heartbeat; miner._poll_loop=poll
        self.worker._submit(miner.run())
        self.assertEqual(observed,[(True,None)])
        self.assertFalse(miner._running)

    def test_official_refresh_reselects_immediately(self):
        miner=self.miner(); miner._fetch_missions=AsyncMock(); miner._fetch_events=AsyncMock(); miner._sync_channel=AsyncMock()
        self.worker._submit(miner._refresh_task_snapshot())
        miner._sync_channel.assert_awaited_once()

    def test_explicit_rejection_skips_uid_target_without_erasing_priority(self):
        miner=self.miner(); miner._queue_target='a'
        miner._heartbeat=SimpleNamespace(last_response_result='明确失败码')
        async def sync(): miner._stop.set()
        miner._sync_channel=sync
        self.worker._submit(miner._queue_refusal_loop())
        miner._stop.clear()
        miner._heartbeat=None
        self.assertEqual(self.resolve(miner).user_id,'room-b')
        self.assertEqual(self.worker.settings['campaign_priorities']['game:1'],['a','b'])
        self.assertEqual(self.resolve(self.miner('B')).user_id,'room-a')

    def test_delayed_old_room_heartbeat_cannot_certify_new_target(self):
        beat=self.worker._core['miner'].WatchHeartbeat('A',self.live('old'))
        base=type(beat).__mro__[1]
        async def delayed(instance,session):
            instance.switch_channel(self.live('new'))
            instance.connection_healthy=True
            return self.worker._core['miner'].HeartbeatStatus.SUCCESS
        with patch.object(base,'send',new=delayed):
            result=self.worker._submit(beat.send(object()))
        self.assertEqual(result,self.worker._core['miner'].HeartbeatStatus.UNKNOWN)
        self.assertFalse(beat.connection_healthy)

    def test_actual_channel_switch_and_heartbeat_after_adapter_reload(self):
        # Source hosts can reload a Worker in-process. No subclass may retain
        # the previous worker's settings or target dictionary in a closure.
        self.assertFalse(hasattr(self.worker._core['miner'].SoopMiner.__mro__[1], '_cloudlight_queue_base'))
        miner=self.miner()
        async def fetch(session,cookies,id,**kw): return self.live(id)
        with patch.object(self.channels,'fetch_live_channel',side_effect=fetch):
            self.worker._submit(miner._sync_channel())
            self.assertEqual(miner._current.user_id,'room-a')
            self.assertEqual(miner._heartbeat.channel.user_id,'room-a')
            miner._missions[0].items[0].mission_success=True
            self.worker._submit(miner._sync_channel())
            self.assertEqual(miner._current.user_id,'room-b')
            self.assertEqual(miner._heartbeat.channel.user_id,'room-b')
            self.assertEqual(self.worker._queue_targets['A']['campaignId'],'b')

    def test_real_read_boundary_serializes_auth_and_official_progress_without_watching(self):
        miner=self.miner()
        self.assertFalse(self.worker._state_to_dict(miner.get_state())['loggedIn'])
        miner._drops=SimpleNamespace(_request=AsyncMock(return_value={'data':[]}),
            get_missions=AsyncMock(return_value=miner._missions),get_events=AsyncMock(return_value=[]),
            get_inventory=AsyncMock(return_value=[]))
        with patch.object(self.channels,'fetch_live_drops_channels',new=AsyncMock(return_value=([],[]))):
            state=self.worker._submit(miner.force_refresh())
        wire=self.worker._state_to_dict(state)
        self.assertTrue(wire['loggedIn'])
        self.assertFalse(wire['running'])
        self.assertEqual(wire['authenticationState'],'verified')
        self.assertEqual(wire['officialEvidence']['uid'],'A')
        self.assertEqual(wire['officialEvidence']['status'],'synced')
        self.assertEqual(wire['officialProgress'][0]['currentMinutes'],10)
        self.assertEqual(wire['heartbeatEvidence']['status'],'not_sent')
        # No enabling, code lookup or claim operation exists on this fake client.
        miner._drops.get_inventory.assert_awaited_once_with(with_codes=False)

    def test_real_heartbeat_serializes_exact_uid_room_broadcast_and_campaign(self):
        self.worker._queue_targets['A']={'campaignId':'a','channelId':'room-a'}
        beat=self.worker._core['miner'].WatchHeartbeat('A',self.live('room-a'))
        base=type(beat).__mro__[1]
        async def accepted(instance,session):
            return self.worker._core['miner'].HeartbeatStatus.SUCCESS
        with patch.object(base,'send',new=accepted):
            self.worker._submit(beat.send(object()))
        evidence=self.worker.evidence.snapshot('A')['heartbeatEvidence']
        self.assertEqual((evidence['uid'],evidence['channelId'],evidence['broadcastNo'],evidence['campaignId']),
            ('A','room-a','123','a'))
        self.assertEqual(evidence['status'],'success')
        self.assertFalse(self.worker.evidence.snapshot('A')['loggedIn'])


if __name__ == '__main__': unittest.main()
