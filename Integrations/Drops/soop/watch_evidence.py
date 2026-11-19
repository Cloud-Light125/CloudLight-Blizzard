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
                previous = campaign_states.get(campaign)
                if previous is None or status == 'grown' or (status == 'unchanged' and previous == 'synced'):
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


def account_status_text(account):
    """Render the same independent evidence used by the desktop account cards."""
    uid = str(account['uid'])
    auth = account.get('authenticationState')
    heartbeat = account.get('heartbeatEvidence', {})
    official = account.get('officialEvidence', {})
    target = account.get('queueTarget', {}).get('campaignId') or ''
    room = account.get('channelId') or ''
    queue = account.get('queueDiagnostics', {})
    running = account.get('running', False)
    connected = bool(auth == 'verified' and running and room and account.get('bridgeConnected') and account.get('connectionHealthy', True))
    correlated = (connected and str(heartbeat.get('uid')) == uid and not heartbeat.get('requestExpired')
                  and heartbeat.get('channelId') == room and target and heartbeat.get('campaignId') == target
                  and account.get('broadcastNo') and str(heartbeat.get('broadcastNo')) == str(account['broadcastNo']))
    heartbeat_state = heartbeat.get('status')
    success = heartbeat_state == 'success' and correlated
    if success:
        beat = '成功'
    elif heartbeat_state == 'success' or heartbeat_state == 'expired' and heartbeat.get('lastSuccessAt'):
        beat = '已过期'
    else:
        beat = {'timeout': '未成功 · 超时', 'failure': '未成功 · 请求失败', 'sending': '未成功 · 正在发送'}.get(heartbeat_state, '未成功')
    login = {'verified': '已验证登录', 'verifying': '未验证 · 正在验证', 'expired': '未验证 · 认证失效',
             'awaiting_login': '未验证 · 需要重新登录', 'failed': '未验证 · 登录失败'}.get(auth, '未验证')
    if not target:
        for key in ('currentProgress', 'officialProgress'):
            for progress in account.get(key, []):
                task_id = str(progress.get('id', ''))
                target = progress.get('campaignId') or (task_id[len(uid) + 1:] if task_id.startswith(uid + ':') else '')
                if target:
                    break
            if target:
                break
    official_state = official.get('status') if str(official.get('uid')) == uid else 'stale'
    if official_state in {'grown', 'unchanged', 'synced'}:
        official_state = official.get('campaignStates', {}).get(target, 'synced')
    progress = {'grown': '已增长', 'unchanged': '未增长', 'failed': '暂未确认 · 读取失败',
                'stale': '暂未确认 · 数据已过期'}.get(official_state, '暂未确认')
    if auth != 'verified':
        wait = '等待认证'
    elif not running:
        wait = '账号观看任务尚未启动'
    elif queue.get('reason') != 'selected' and queue.get('message'):
        wait = queue['message']
    elif not room:
        wait = '正在查找合资格频道'
    elif not connected:
        wait = '正在连接选中的直播间'
    else:
        wait = '' if success else '等待当前直播间的成功心跳'
    return {'login': '登录状态：' + login, 'worker': 'Worker 运行中',
            'channel': '直播间：已连接 · ' + str(account.get('channelName') or room) if connected else '直播间：未连接',
            'heartbeat': '观看心跳：' + beat, 'official': '官方进度：' + progress, 'waitReason': wait}
