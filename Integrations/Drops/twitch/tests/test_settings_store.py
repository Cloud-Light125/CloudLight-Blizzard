from __future__ import annotations

import copy
import json
import logging
import os
import subprocess
import sys
import tempfile
import unittest
from pathlib import Path
from types import SimpleNamespace
from unittest.mock import patch

ROOT = Path(__file__).resolve().parents[1]
for location in (ROOT.parent, ROOT, ROOT / 'core'):
    sys.path.insert(0, str(location))
import headless_gui
sys.modules['gui'] = headless_gui
import constants
constants.LANG_PATH = ROOT / 'core/lang'
from settings import default_settings
from settings_store import FileLease, SettingsError, SettingsStore


class SettingsIntegrityTests(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory()
        self.addCleanup(self.temp.cleanup)
        self.path = Path(self.temp.name) / 'settings.json'
        self.cookies = self.path.with_name('cookies.jar')
        self.cookies.write_bytes(b'unchanged-session-fixture')
        self.store = SettingsStore(self.path, default_settings)

    def tearDown(self):
        self.assertEqual(self.cookies.read_bytes(), b'unchanged-session-fixture')

    def invalid(self, data, state='InvalidJson'):
        self.path.write_bytes(data)
        with self.assertRaises(SettingsError) as error:
            self.store.load()
        self.assertEqual(error.exception.state, state)
        self.assertEqual(self.path.read_bytes(), data)
        self.assertEqual(error.exception.path.name, 'settings.json')

    def good(self, **changes):
        result = copy.deepcopy(default_settings)
        result.update(changes)
        return self.store.encode(result)

    def test_01_missing_claim_disabled(self):
        values = self.store.load()
        self.assertFalse(values['auto_claim_drops'])
        self.assertEqual(self.store.state, 'Missing')
        self.assertFalse(self.path.exists())

    def test_02_empty(self): self.invalid(b'')
    def test_03_whitespace(self): self.invalid(b' \r\n\t')
    def test_04_truncated(self): self.invalid(b'{"priority":[')
    def test_05_malformed(self): self.invalid(b'{invalid}')
    def test_06_null(self): self.invalid(b'null', 'InvalidSchema')
    def test_07_array(self): self.invalid(b'[]', 'InvalidSchema')

    def test_08_legacy_preserves_fields(self):
        self.path.write_text(json.dumps({'priority':['Overwatch'], 'exclude':['Other'], 'proxy':'http://127.0.0.1:7897',
                                          'priority_mode':'ENDING_SOONEST', 'custom':{'kept':1}}), encoding='utf8')
        values = self.store.load()
        self.assertEqual(values['priority'], ['Overwatch'])
        self.assertEqual(values['exclude'], {'Other'})
        self.assertEqual(str(values['proxy']), 'http://127.0.0.1:7897')
        self.assertEqual(values['custom'], {'kept':1})
        self.assertFalse(values['auto_claim_drops'])
        self.assertEqual(self.store.state, 'LegacyCompatible')

    def test_09_partial(self):
        self.path.write_text('{"auto_claim_drops":false}', encoding='utf8')
        self.assertFalse(self.store.load()['auto_claim_drops'])

    def test_10_wrong_types_not_silently_reset(self):
        for value in ({'auto_claim_drops':'true'}, {'priority':'game'}, {'connection_quality':True},
                      {'connection_quality':99}, {'exclude':[1]}, {'language':None}, {'priority_mode':{}}, {'proxy':[]}):
            with self.subTest(value=value):
                self.invalid(json.dumps(value).encode(), 'InvalidSchema')

    def test_11_valid_new_recovers_bad_primary(self):
        self.path.write_bytes(b'\0' * 600)
        backup = self.path.with_name('settings.json.new')
        backup.write_bytes(self.good(priority=['kept'], auto_claim_drops=False))
        self.assertEqual(self.store.load()['priority'], ['kept'])
        self.assertEqual(self.store.state, 'RecoveryAvailable')
        self.assertEqual(next(self.path.parent.glob('*.original')).read_bytes(), b'\0'*600)
        self.assertTrue(backup.exists())

    def test_12_invalid_new_valid_primary(self):
        self.path.write_bytes(self.good(priority=['correct'], auto_claim_drops=True))
        backup = self.path.with_name('settings.json.new')
        backup.write_bytes(b'{broken')
        self.assertEqual(self.store.load()['priority'], ['correct'])
        self.assertTrue(self.store.load()['auto_claim_drops'])
        self.assertEqual(backup.read_bytes(), b'{broken')

    def test_13_both_invalid_preserved(self):
        backup = self.path.with_name('settings.json.new')
        backup.write_bytes(b'bad')
        self.invalid(b'\0'*600)
        self.assertEqual(backup.read_bytes(), b'bad')

    def test_14_lkg(self):
        self.path.with_name('settings.json.lkg').write_bytes(self.good(priority=['lkg']))
        self.path.write_bytes(b'bad')
        self.assertEqual(self.store.load()['priority'], ['lkg'])
        self.assertEqual(self.store.recovered_from, 'settings.json.lkg')

    def test_15_corrupt_backup(self):
        self.path.with_name('settings.json.bak').write_bytes(b'bad')
        self.invalid(b'')

    def test_16_read_permission_not_missing(self):
        self.path.write_bytes(self.good())
        with patch('settings_store.read_bytes', side_effect=PermissionError):
            with self.assertRaises(SettingsError) as error: self.store.load()
        self.assertEqual(error.exception.state, 'Unreadable')

    def test_17_replace_failure_keeps_original(self):
        self.path.write_bytes(self.good(priority=['before']))
        values = self.store.load()
        values['priority'] = ['after']
        original = self.path.read_bytes()
        with patch('settings_store.os.replace', side_effect=PermissionError):
            with self.assertRaises(SettingsError): self.store.save(values)
        self.assertEqual(self.path.read_bytes(), original)
        self.assertTrue(list(self.path.parent.glob('*.tmp')))

    def test_18_interruption_before_commit(self):
        self.path.write_bytes(self.good(priority=['old']))
        values = self.store.load()
        values['priority'] = ['new']
        original = os.replace
        def crash(source, target):
            if target == self.path: raise SystemExit('simulated crash')
            return original(source, target)
        with patch('settings_store.os.replace', side_effect=crash):
            with self.assertRaises(SystemExit): self.store.save(values)
        self.assertEqual(SettingsStore(self.path, default_settings).load()['priority'], ['old'])

    def test_19_repeated_saves_lkg_and_previous(self):
        values = self.store.load()
        for number in range(6):
            values['priority'] = [str(number)]
            self.store.save(values)
        self.assertEqual(json.loads(self.path.read_text())['priority'], ['5'])
        self.assertEqual(json.loads(self.path.with_name('settings.json.bak').read_text())['priority'], ['4'])
        self.assertEqual(self.path.read_bytes(), self.path.with_name('settings.json.lkg').read_bytes())

    def test_20_competing_process_cannot_overwrite(self):
        self.path.write_bytes(self.good(priority=['before']))
        stale = self.store.load()
        source = 'import sys; from pathlib import Path; from settings import default_settings; from settings_store import SettingsStore; s=SettingsStore(Path(sys.argv[1]),default_settings); v=s.load(); v["priority"]=["other-process"]; s.save(v)'
        env = dict(os.environ, PYTHONPATH=str(ROOT / 'core'))
        subprocess.run([sys.executable, '-c', source, str(self.path)], env=env, check=True, capture_output=True)
        with self.assertRaises(SettingsError): self.store.save(stale)
        self.assertEqual(self.store.load()['priority'], ['other-process'])

    def test_worker_lease_cross_process_and_release(self):
        lock = self.path.with_name('worker.lock')
        lease = FileLease(lock).acquire()
        source = 'import sys; from pathlib import Path; from settings_store import FileLease; FileLease(Path(sys.argv[1])).acquire()'
        env = dict(os.environ, PYTHONPATH=str(ROOT / 'core'))
        result = subprocess.run([sys.executable, '-c', source, str(lock)], env=env, capture_output=True)
        self.assertNotEqual(result.returncode, 0)
        lease.close()
        subprocess.run([sys.executable, '-c', source, str(lock)], env=env, check=True, capture_output=True)

    def test_reset_requires_corruption_keeps_evidence_claim_off(self):
        self.path.write_bytes(b'broken')
        self.store.reset_defaults()
        self.assertEqual(next(self.path.parent.glob('*.original')).read_bytes(), b'broken')
        self.assertFalse(self.store.load()['auto_claim_drops'])
        with self.assertRaises(SettingsError): self.store.reset_defaults()

    def test_recovery_replace_failure(self):
        self.path.write_bytes(b'bad')
        self.path.with_name('settings.json.lkg').write_bytes(self.good())
        with patch('settings_store.os.replace', side_effect=PermissionError):
            with self.assertRaises(SettingsError) as error: self.store.load()
        self.assertEqual(error.exception.state, 'RecoveryFailed')
        self.assertEqual(self.path.read_bytes(), b'bad')

    def test_reject_link_and_directory(self):
        self.path.mkdir()
        with self.assertRaises(SettingsError): self.store.load()
        self.path.rmdir()
        target = self.path.with_name('other.json')
        target.write_bytes(self.good())
        os.link(target, self.path)
        with self.assertRaises(SettingsError): self.store.load()
        self.assertEqual(target.read_bytes(), self.good())

    @unittest.skipUnless(os.name == 'nt', 'Windows replace semantics')
    def test_windows_open_target_replace_fails_safely(self):
        self.path.write_bytes(self.good(priority=['before']))
        values = self.store.load()
        original = self.path.read_bytes()
        with self.path.open('rb'):
            with self.assertRaises(SettingsError): self.store.save(values)
        self.assertEqual(self.path.read_bytes(), original)

    def test_utf8_bom_compatible(self):
        self.path.write_bytes(b'\xef\xbb\xbf' + self.good())
        self.assertIn('priority', self.store.load())

    def test_duplicate_keys_and_boolean_enum_rejected(self):
        self.invalid(b'{"auto_claim_drops":false,"auto_claim_drops":true}')
        self.invalid(b'{"priority_mode":{"__type":"PriorityMode","data":true}}', 'InvalidSchema')


class WorkerConfigTests(unittest.TestCase):
    def test_core_error_retry_repair_and_worker_exclusion(self):
        from worker import TwitchWorker
        with tempfile.TemporaryDirectory() as folder:
            root = Path(folder)
            path = root / 'settings.json'
            path.write_bytes(b'\0'*600)
            (root / 'cookies.jar').write_bytes(b'session-fixture')
            worker = TwitchWorker(root, root/'worker.log')
            try:
                state = worker.load_state({})
                self.assertFalse(state['coreAvailable'])
                self.assertEqual(state['coreErrorKind'], 'configuration')
                self.assertTrue(state['accounts'][0]['sessionSaved'])
                for _ in range(2):
                    self.assertFalse(worker.retry_core({})['coreAvailable'])
                with self.assertRaises(ValueError): worker.repair_settings({})
                state = worker.repair_settings({'confirmDefaults':True})
                self.assertTrue(state['coreAvailable'])
                self.assertEqual(state['coreError'], '')
                self.assertFalse(state['settings']['auto_claim_drops'])
                self.assertEqual((root/'cookies.jar').read_bytes(), b'session-fixture')
                other = TwitchWorker(root, root/'second.log')
                try:
                    self.assertEqual(other.load_state({})['coreErrorKind'], 'data_in_use')
                finally:
                    other.shutdown({})
            finally:
                worker.shutdown({})
                for logger in (worker.logger, logging.getLogger('TwitchDrops')):
                    for handler in list(logger.handlers):
                        logger.removeHandler(handler)
                        handler.close()


if __name__ == '__main__': unittest.main()
