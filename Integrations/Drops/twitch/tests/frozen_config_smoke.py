"""Real JSONL/frozen Worker tests; uses only isolated synthetic data."""
from __future__ import annotations
import argparse
import json
import queue
import subprocess
import threading
import time
from pathlib import Path


class Client:
    def __init__(self, executable: Path, root: Path):
        root.mkdir(parents=True, exist_ok=True)
        self.root = root
        self.process = subprocess.Popen([str(executable), '--data-dir', str(root), '--log-file', str(root/'worker.log')],
                                        stdin=subprocess.PIPE, stdout=subprocess.PIPE, stderr=subprocess.DEVNULL,
                                        text=True, encoding='utf-8', creationflags=getattr(subprocess, 'CREATE_NO_WINDOW', 0))
        self.messages = queue.Queue()
        self.events = []
        self.sequence = 0
        def read():
            for line in self.process.stdout:
                try: self.messages.put(json.loads(line))
                except ValueError: pass
        self.reader = threading.Thread(target=read, daemon=True)
        self.reader.start()

    def request(self, command, payload=None, timeout=45):
        self.sequence += 1
        identifier = str(self.sequence)
        self.process.stdin.write(json.dumps({'id':identifier, 'command':command, 'payload':payload or {}})+'\n')
        self.process.stdin.flush()
        deadline = time.monotonic() + timeout
        while time.monotonic() < deadline:
            message = self.messages.get(timeout=max(.01, deadline-time.monotonic()))
            if message.get('id') == identifier:
                if not message.get('ok'): raise RuntimeError(str(message.get('error',{})))
                return message.get('result')
            self.events.append(message)
        raise TimeoutError(command)

    def close(self):
        if self.process.poll() is None:
            self.request('shutdown', timeout=20)
            self.process.wait(timeout=20)
        assert self.process.returncode == 0
        self.process.stdin.close()
        self.reader.join(timeout=3)
        self.process.stdout.close()


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument('--worker', type=Path, required=True)
    parser.add_argument('--output', type=Path, required=True)
    args = parser.parse_args()
    args.output.mkdir(parents=True, exist_ok=True)
    results = []
    good = json.dumps({'auto_claim_drops':False,'priority':['Overwatch'],'exclude':[],
                       'language':'English','connection_quality':2,'priority_mode':'ENDING_SOONEST'}).encode()
    for case in ('valid', 'missing', 'invalid', 'new-recovery', 'lkg-recovery', 'controlled-reset', 'external-repair'):
        root = args.output / case
        root.mkdir(exist_ok=False)
        settings = root/'settings.json'
        cookie = root/'cookies.jar'
        cookie.write_bytes(b'opaque-test-session-do-not-load')
        if case != 'missing': settings.write_bytes(good if case == 'valid' else b'\0'*600)
        if case in ('new-recovery', 'lkg-recovery'):
            settings.with_name('settings.json.' + ('new' if case == 'new-recovery' else 'lkg')).write_bytes(good)
        client = Client(args.worker, root)
        try:
            assert client.request('hello')['platform'] == 'twitch'
            state = client.request('load_state')
            if case in ('invalid','controlled-reset','external-repair'):
                assert not state['coreAvailable'] and state['coreErrorKind'] == 'configuration'
                assert state['configuration']['state'] == 'InvalidJson'
                assert state['accounts'][0]['sessionSaved']
                for _ in range(3): assert not client.request('retry_core')['coreAvailable']
                if case == 'controlled-reset':
                    state = client.request('repair_settings', {'confirmDefaults':True})
                    assert next(root.glob('*.original')).read_bytes() == b'\0'*600
                elif case == 'external-repair':
                    # Fixture only: user externally supplies a valid configuration.
                    settings.write_bytes(good)
                    state = client.request('retry_core')
                else:
                    assert settings.read_bytes() == b'\0'*600
            if case != 'invalid':
                assert state['coreAvailable'] and not state['coreError']
                assert not state['settings']['auto_claim_drops']
                if case in ('valid','new-recovery','lkg-recovery','external-repair'):
                    assert state['settings']['priority'] == ['Overwatch']
                if case in ('new-recovery','lkg-recovery'):
                    assert next(root.glob('*.original')).read_bytes() == b'\0'*600
            client.request('stop')
            assert not client.request('load_state')['running']
            assert cookie.read_bytes() == b'opaque-test-session-do-not-load'
        finally:
            client.close()
        results.append({'case':case, 'status':'PASS', 'workerExit':client.process.returncode,
                        'cookieUnchanged':True, 'automaticClaims':False})
    (args.output/'summary.json').write_text(json.dumps(results, indent=2), encoding='utf-8')
    print(f'PASS {len(results)} frozen Twitch configuration scenarios')


if __name__ == '__main__': main()
