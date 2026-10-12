from __future__ import annotations

import tempfile
import json
import queue
import subprocess
import threading
import sys
import unittest
from pathlib import Path
from unittest.mock import patch

DROPS_DIR = Path(__file__).resolve().parents[2]
if str(DROPS_DIR) not in sys.path:
    sys.path.insert(0, str(DROPS_DIR))

from Shared.protocol import RuntimeDependencyError, WorkerBase, parse_protocol_request


class _HttpsWorker(WorkerBase):
    platform = "test"


class RuntimeDependencyTests(unittest.TestCase):
    def test_stop_interrupts_read_only_refresh_without_stopping_other_account(self) -> None:
        # Exercise the real stdin reader, JSONL transport and future cancellation.
        # Business responses are fixtures; no platform API or credentials are used.
        script = '''
import sys
from pathlib import Path
from concurrent.futures import Future
sys.path.insert(0, sys.argv[1])
from Shared.protocol import WorkerBase, event
class Worker(WorkerBase):
    needs_https = False
    def __init__(self):
        super().__init__(Path(sys.argv[2]), Path(sys.argv[2])/'worker.log')
        self.accounts = {'A': True, 'B': True}
        self.commands['stop_account'] = self.stop_account
    def refresh(self, payload):
        event('refresh_waiting', {})
        self.wait_future(Future(), 30)
    def stop_account(self, payload):
        self.accounts[payload['uid']] = False
        return {'accounts': self.accounts, 'minutes': 10}
raise SystemExit(Worker().run())
'''
        with tempfile.TemporaryDirectory() as temporary:
            process = subprocess.Popen([sys.executable, '-c', script, str(DROPS_DIR), temporary],
                stdin=subprocess.PIPE, stdout=subprocess.PIPE, stderr=subprocess.PIPE, text=True, encoding='utf-8')
            mailbox = queue.Queue()
            def read():
                for line in process.stdout:
                    mailbox.put(json.loads(line))
            threading.Thread(target=read, daemon=True).start()
            def send(command, payload=None):
                process.stdin.write(json.dumps({'id':command,'command':command,'payload':payload or {}})+'\n')
                process.stdin.flush()
            try:
                # A malformed control must not leave the reader's pending-stop
                # counter set and cancel all subsequent refreshes.
                process.stdin.write(json.dumps({'id':'bad','command':'stop_account','payload':['invalid']})+'\n')
                process.stdin.flush()
                while True:
                    invalid = mailbox.get(timeout=5)
                    if invalid.get('id') == 'bad':
                        self.assertFalse(invalid['ok'])
                        break
                send('refresh')
                while mailbox.get(timeout=5).get('event') != 'refresh_waiting':
                    pass
                send('stop_account', {'uid':'B'})
                responses = {}
                while 'stop_account' not in responses:
                    item = mailbox.get(timeout=5)
                    if 'id' in item: responses[item['id']] = item
                self.assertFalse(responses['refresh']['ok'])
                self.assertEqual(responses['stop_account']['result']['accounts'], {'A':True,'B':False})
                self.assertEqual(responses['stop_account']['result']['minutes'], 10)
                send('shutdown'); process.stdin.close(); process.wait(timeout=5)
                self.assertEqual(process.returncode, 0)
            finally:
                if process.poll() is None: process.kill(); process.wait(timeout=5)
                process.stdout.close(); process.stderr.close()

    def test_protocol_accepts_windows_powershell_utf8_bom(self) -> None:
        request = parse_protocol_request(
            '\ufeff{"id":"ssl-selftest","command":"ssl_check","payload":{}}\n')
        self.assertEqual(request["id"], "ssl-selftest")
        self.assertEqual(request["command"], "ssl_check")

    def test_ssl_import_failure_is_structured_non_retryable_and_logged_once(self) -> None:
        failure = RuntimeDependencyError(
            "ssl", "ssl_runtime_unavailable",
            "Python SSL 组件无法加载。", "DLL load failed while importing _ssl",
        )
        emitted: list[tuple[str, dict[str, object]]] = []
        with tempfile.TemporaryDirectory() as temporary, \
             patch("Shared.protocol.check_ssl_runtime", side_effect=failure), \
             patch("Shared.protocol.event", side_effect=lambda name, payload: emitted.append((name, payload))):
            root = Path(temporary)
            worker = _HttpsWorker(root / "data", root / "worker.log")
            try:
                for _ in range(2):
                    with self.assertRaises(RuntimeDependencyError):
                        worker.require_runtime()
                runtime_events = [payload for name, payload in emitted if name == "runtime_error"]
                self.assertGreaterEqual(len(runtime_events), 1)
                self.assertTrue(all(payload["code"] == "ssl_runtime_unavailable" for payload in runtime_events))
                self.assertTrue(all(payload["retryable"] is False for payload in runtime_events))
                log_text = (root / "worker.log").read_text(encoding="utf-8")
                self.assertEqual(log_text.count("ERROR"), 1)
                self.assertNotIn("Traceback", log_text)
            finally:
                for handler in list(worker.logger.handlers):
                    worker.logger.removeHandler(handler)
                    handler.close()


if __name__ == "__main__":
    unittest.main()
