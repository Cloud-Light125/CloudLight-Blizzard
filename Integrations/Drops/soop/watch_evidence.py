"""Process-local evidence from completed network calls, never saved credentials or state ticks."""
from __future__ import annotations

from datetime import datetime, timezone
import threading
import time


class WatchEvidence:
    def __init__(self, clock=time.time):
        self.clock = clock
        self.lock = threading.RLock()
        self.accounts = {}

    def _row(self, uid):
        return self.accounts.setdefault(str(uid), {'auth': 'unverified', 'reads': {}, 'epoch': 0,
            'target': (), 'heartbeat': {'status': 'not_sent'}, 'official': {'status': 'unread', 'uid': str(uid)}})

    def stamp(self):
        return datetime.fromtimestamp(self.clock(), timezone.utc).isoformat()

    def auth(self, uid, state):
        with self.lock:
            row = self._row(uid)
            row['auth'] = state
            if state != 'verified':
                row['epoch'] += 1
                row['heartbeat'] = {'status': 'expired', 'requestExpired': True}
            if state in {'expired', 'failed', 'awaiting_login'}:
                row['reads'] = {}
                row['official']['status'] = 'stale'

    def official(self, uid, readings, campaigns=None):
        """Called exactly once per successful get_missions, including empty results."""
        with self.lock:
            row = self._row(uid)
            now = self.clock()
            prior = row['reads']
            valid_prior = row['auth'] == 'verified' and row['official'].get('status') not in {'failed', 'stale'}
            values = {str(k): v for k, v in readings.items()}
            grown = [k for k, v in values.items() if valid_prior and k in prior and
                     0 < now - prior[k][1] <= 300 and v > prior[k][0]]
            compared = [k for k in values if valid_prior and k in prior and 0 < now - prior[k][1] <= 300]
            campaign_ids = campaigns or {k: k for k in values}
            campaign_states = {}
            for key in values:
                campaign = campaign_ids.get(key, key)
                status = 'grown' if key in grown else 'unchanged' if key in compared else 'synced'
                if campaign_states.get(campaign) != 'grown':
                    campaign_states[campaign] = status
            row['auth'] = 'verified'
            row['reads'] = {k: (v, now) for k, v in values.items()}
            row['official'] = {'status': 'grown' if grown else 'unchanged' if compared else 'synced',
                'syncedAt': self.stamp(), 'readAt': now, 'uid': str(uid), 'grownTasks': grown,
                'comparedTasks': compared, 'values': values, 'campaignStates': campaign_states}

    def failure(self, uid, error, authentication=False):
        with self.lock:
            row = self._row(uid)
            row['official'] = {**row['official'], 'status': 'failed', 'error': str(error)}
            if authentication:
                self.auth(uid, 'expired')

    def target(self, uid, channel, broadcast, campaign):
        with self.lock:
            row = self._row(uid)
            target = (str(channel or ''), str(broadcast or ''), str(campaign or ''))
            if row['target'] != target:
                row['target'] = target
                row['epoch'] += 1
                row['heartbeat'] = {'status': 'not_sent'}
            return row['epoch'], target

    def begin(self, uid, channel, broadcast, campaign):
        with self.lock:
            token = self.target(uid, channel, broadcast, campaign)
            self._row(uid)['heartbeat']['status'] = 'sending'
            return token

    def finish(self, uid, token, status):
        with self.lock:
            row = self._row(uid)
            if token != (row['epoch'], row['target']):
                return False
            previous = row['heartbeat']
            row['heartbeat'] = {**previous, 'status': status, 'uid': str(uid),
                'channelId': token[1][0], 'broadcastNo': token[1][1], 'campaignId': token[1][2],
                'epoch': token[0], 'requestExpired': False}
            if status == 'success':
                row['heartbeat'].update(lastSuccessAt=self.stamp(), successAt=self.clock())
            return True

    def stop(self, uid):
        with self.lock:
            row = self._row(uid)
            row['epoch'] += 1
            row['heartbeat'] = {**row['heartbeat'], 'status': 'expired', 'requestExpired': True}

    def snapshot(self, uid):
        with self.lock:
            row = self._row(uid)
            heartbeat = dict(row['heartbeat'])
            official = dict(row['official'])
            if heartbeat.get('status') == 'success' and self.clock() - heartbeat.get('successAt', 0) > 150:
                heartbeat.update(status='expired', requestExpired=True)
            if official.get('status') not in {'unread', 'failed', 'stale'} and self.clock() - official.get('readAt', 0) > 300:
                official['status'] = 'stale'
            return {'authenticationState': row['auth'], 'loggedIn': row['auth'] == 'verified',
                'heartbeatEvidence': heartbeat, 'officialEvidence': official}
