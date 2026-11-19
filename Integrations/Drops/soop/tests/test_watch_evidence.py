"""Offline network-boundary evidence, directory refresh and stale-response regressions."""
import json
from pathlib import Path
import sys
import tempfile
import unittest

sys.path.insert(0, str(Path(__file__).resolve().parents[1]))
from watch_evidence import WatchEvidence
from game_catalog import GameCatalog


class EvidenceTests(unittest.TestCase):
    def setUp(self):
        self.now = 1800000000.0
        self.e = WatchEvidence(lambda: self.now)

    def test_unverified_worker_never_means_authenticated(self):
        token = self.e.begin('A', 'room', '1', 'task')
        self.e.finish('A', token, 'success')
        self.assertFalse(self.e.snapshot('A')['loggedIn'])

    def test_first_official_read_verifies_but_never_confirms_growth(self):
        self.e.official('A', {'task|cycle|tier|360': 247})
        s = self.e.snapshot('A')
        self.assertTrue(s['loggedIn'])
        self.assertEqual(s['officialEvidence']['status'], 'synced')

    def test_repeated_state_ticks_cannot_confirm_growth(self):
        self.e.official('A', {'task': 247})
        for _ in range(10): self.e.snapshot('A')
        self.assertEqual(self.e.snapshot('A')['officialEvidence']['status'], 'synced')

    def test_heartbeat_is_independent_of_official_growth(self):
        self.e.official('A', {'task': 247})
        self.now += 60
        token = self.e.begin('A', 'room', '1', 'task')
        self.e.finish('A', token, 'success')
        self.e.official('A', {'task': 247})
        s = self.e.snapshot('A')
        self.assertEqual(s['officialEvidence']['status'], 'unchanged')
        self.assertEqual(s['heartbeatEvidence']['status'], 'success')

    def test_two_reads_confirm_same_task_growth_without_requiring_heartbeat(self):
        self.e.official('A', {'task|cycle|tier|360': 247})
        self.now += 60
        self.e.official('A', {'task|cycle|tier|360': 248})
        self.assertEqual(self.e.snapshot('A')['officialEvidence']['status'], 'grown')

    def test_different_cycle_or_stage_never_confirms_growth(self):
        self.e.official('A', {'task|cycle1|tier1|360': 247})
        self.now += 60
        self.e.official('A', {'task|cycle2|tier1|360': 248, 'task|cycle1|tier2|360': 248})
        self.assertEqual(self.e.snapshot('A')['officialEvidence']['status'], 'synced')

    def test_double_uid_isolation(self):
        self.e.official('A', {'task': 247})
        self.now += 60
        self.e.official('B', {'task': 248})
        self.assertEqual(self.e.snapshot('B')['officialEvidence']['status'], 'synced')
        self.assertEqual(self.e.snapshot('A')['officialEvidence']['values']['task'], 247)

    def test_delayed_old_channel_cannot_certify_new_room(self):
        token = self.e.begin('A', 'old', '1', 'task')
        self.e.target('A', 'new', '2', 'task')
        self.assertFalse(self.e.finish('A', token, 'success'))
        self.assertEqual(self.e.snapshot('A')['heartbeatEvidence']['status'], 'not_sent')

    def test_same_room_new_broadcast_or_task_rejects_old_response(self):
        for target in [('room', '2', 'task'), ('room', '1', 'task2')]:
            token = self.e.begin('A', 'room', '1', 'task')
            self.e.target('A', *target)
            self.assertFalse(self.e.finish('A', token, 'success'))

    def test_a_b_a_switch_still_rejects_old_a(self):
        token = self.e.begin('A', 'a', '1', 'task')
        self.e.target('A', 'b', '1', 'task')
        self.e.target('A', 'a', '1', 'task')
        self.assertFalse(self.e.finish('A', token, 'success'))

    def test_timeout_and_stop_invalidate_success(self):
        token = self.e.begin('A', 'room', '1', 'task')
        self.e.finish('A', token, 'timeout')
        self.assertEqual(self.e.snapshot('A')['heartbeatEvidence']['status'], 'timeout')
        token = self.e.begin('A', 'room', '1', 'task')
        self.e.stop('A')
        self.assertFalse(self.e.finish('A', token, 'success'))

    def test_expiry_keeps_official_values_and_marks_stale(self):
        self.e.official('A', {'task': 247})
        token = self.e.begin('A', 'room', '1', 'task')
        self.e.finish('A', token, 'success')
        self.now += 301
        s = self.e.snapshot('A')
        self.assertEqual(s['heartbeatEvidence']['status'], 'expired')
        self.assertEqual(s['officialEvidence']['status'], 'stale')
        self.assertEqual(s['officialEvidence']['values']['task'], 247)

    def test_failed_read_breaks_continuous_growth_evidence(self):
        self.e.official('A', {'task': 247})
        self.e.failure('A', 'offline')
        self.assertEqual(self.e.snapshot('A')['officialEvidence']['values']['task'], 247)
        self.now += 60
        self.e.official('A', {'task': 248})
        self.assertEqual(self.e.snapshot('A')['officialEvidence']['status'], 'synced')

    def test_reauthentication_requires_fresh_api_read(self):
        self.e.official('A', {'task': 247})
        self.e.auth('A', 'expired')
        self.assertFalse(self.e.snapshot('A')['loggedIn'])
        self.e.auth('A', 'verifying')
        self.now += 60
        self.e.official('A', {'task': 248})
        self.assertTrue(self.e.snapshot('A')['loggedIn'])
        self.assertEqual(self.e.snapshot('A')['officialEvidence']['status'], 'synced')

    def test_process_restart_does_not_reuse_auth_or_watch_evidence(self):
        self.e.official('A', {'task': 247})
        restarted = WatchEvidence(lambda: self.now)
        self.assertFalse(restarted.snapshot('A')['loggedIn'])

    def test_auth_expiry_rejects_in_flight_heartbeat_even_after_revalidation(self):
        self.e.official('A', {'task':247})
        token=self.e.begin('A','room','1','task')
        self.e.auth('A','expired')
        self.now+=1
        self.e.official('A',{'task':247})
        self.assertFalse(self.e.finish('A',token,'success'))

    def test_growth_is_scoped_to_campaign_identity(self):
        values={'tier-a':247,'tier-b':10}
        campaigns={'tier-a':'campaign-a','tier-b':'campaign-b'}
        self.e.official('A',values,campaigns)
        self.now+=60
        self.e.official('A',{'tier-a':247,'tier-b':11},campaigns)
        states=self.e.snapshot('A')['officialEvidence']['campaignStates']
        self.assertEqual(states,{'campaign-a':'unchanged','campaign-b':'grown'})


class CatalogTests(unittest.TestCase):
    def test_all_54_official_categories_exactly_mapped(self):
        with tempfile.TemporaryDirectory() as tmp:
            catalog = GameCatalog(Path(tmp)/'catalog.json')
            rows = json.loads(Path(__file__).with_name('fixtures').joinpath('official-categories-54.json').read_text('utf-8'))['data']
            self.assertEqual(len(rows), 54)
            for row in rows:
                with self.subTest(game=row['gameIdx']):
                    value = catalog.name('game:'+row['gameIdx'])
                    self.assertIn(row['gameTitle'], value)
                    self.assertFalse(value.startswith('game:'))

    def test_latest_directory_snapshot_maps_every_current_category(self):
        with tempfile.TemporaryDirectory() as tmp:
            catalog = GameCatalog(Path(tmp)/'catalog.json')
            rows = json.loads(Path(__file__).resolve().parents[1].joinpath('game-catalog.official.json').read_text('utf-8'))['data']
            self.assertGreaterEqual(len(rows), 54)
            for row in rows:
                self.assertIn(row['gameTitle'], catalog.name('game:'+row['gameIdx']))

    def test_refresh_cache_fallback_unknown_and_multilingual_names(self):
        with tempfile.TemporaryDirectory() as tmp:
            path = Path(tmp)/'catalog.json'
            catalog = GameCatalog(path)
            catalog.update([{'gameIdx':'9001','gameTitle':'中文 / English / 한국어'}])
            catalog.update([{'gameIdx':'9001','gameTitle':''}])
            reopened = GameCatalog(path)
            self.assertEqual(reopened.name('game:9001'), '中文 / English / 한국어')
            self.assertEqual(reopened.name('game:9002'), '未知游戏（ID: 9002）')
            self.assertEqual(reopened.name('game:9002', '游戏 9002'), '未知游戏（ID: 9002）')
            before = {'gameId':'game:12','id':'campaign','priority':3}
            reopened.decorate(before)
            self.assertEqual((before['gameId'],before['priority']), ('game:12',3))
            self.assertEqual(before['gameName'], '守望先锋（오버워치）')
