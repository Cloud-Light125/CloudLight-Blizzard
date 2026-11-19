"""CloudLight-owned SOOP queue adapter. The pinned Core is never edited.

Only dropsIdx joins catalog and missions; only official game/category IDs join
games and channels. Account state and the current target live on each miner.
"""
from __future__ import annotations

import time
import json
import asyncio
from collections import Counter
from contextlib import asynccontextmanager, nullcontext
from typing import Any

QUEUE_DEFAULTS = {
    'queue_version': 1, 'campaign_selection_mode': 'legacy',
    'selected_games': [], 'game_priority': [], 'campaign_priorities': {}, 'campaign_order': {},
    'campaign_sort_mode': 'custom', 'channel_switch_cooldown': 60,
}
LIVE_QUEUE_SETTINGS = frozenset(QUEUE_DEFAULTS) | {'priority_mission_id'}
WAITING_RESCAN_SECONDS = 30
PROBE_BATCH_SIZE = 8


def ids(value):
    return list(dict.fromkeys(str(x).strip() for x in value if str(x).strip())) if isinstance(value, list) else []


def validate(settings):
    for key in ('selected_games', 'game_priority'):
        settings[key] = ids(settings.get(key))
    for key in ('campaign_priorities', 'campaign_order'):
        value = settings.get(key) or {}
        if not isinstance(value, dict): raise ValueError(key + ' 必须是对象')
        settings[key] = {str(k): ids(v) for k, v in value.items()}
    if settings.get('campaign_selection_mode') not in {'legacy', 'selected_only'}:
        raise ValueError('无效的 SOOP 活动选择模式')
    if settings.get('campaign_sort_mode') not in {'custom', 'ending', 'remaining', 'most', 'least'}:
        raise ValueError('无效的 SOOP 活动排序策略')
    settings['channel_switch_cooldown'] = max(0, min(1800, int(settings.get('channel_switch_cooldown', 60))))


def metadata(raw, activity_id):
    game = str(raw.get('gameNo') or '').strip()
    categories = [x for x in raw.get('cateList') or [] if isinstance(x, dict) and x.get('cateNo')]
    if not categories and raw.get('cateNo'):
        categories = [{'cateNo': raw['cateNo'], 'cateName': raw.get('cateName', '')}]
    # An unknown game stays in its own activity bucket; never group by title.
    game_id = 'game:' + game if game else ('category:' + str(categories[0]['cateNo']) if len(categories) == 1 else 'unmapped:' + activity_id)
    return {'gameId': game_id, 'gameName': str(raw.get('gameTitle') or raw.get('gameName') or raw.get('cateGroupName') or
            ' / '.join(str(x.get('cateName') or x['cateNo']) for x in categories) or ('游戏 ' + game if game else '未确认游戏')),
            'categoryIds': ids([str(x['cateNo']) for x in categories]),
            'categoryNames': [str(x.get('cateName') or x['cateNo']) for x in categories],
            'channelIds': ids([str(x.get('userId') or '') for x in raw.get('broadIdList') or [] if isinstance(x, dict)]),
            'giveCon': str(raw.get('giveCon') or ''), 'detailUrl': str(raw.get('url') or ''),
            'rules': str(raw.get('guideStr') or '')}


def decorate(row, raw, *, account_progress):
    row.update(metadata(raw, str(row['id'])))
    row['campaignId'] = str(row['id'])
    random = row['giveCon'] in {'none', 'draw'}
    row['randomNotice'] = '随机型 · 满足参与条件不代表必定获得奖励' if random else ''
    if not account_progress:
        row['items'] = [{'name': str(x.get('itemName') or ''), 'imageUrl': str(x.get('itemImage') or ''),
                         'requiredMinutes': int(x.get('giveTerm') or 0) or None,
                         'viewMinutes': None, 'percent': None, 'completed': False}
                        for x in raw.get('itemList') or [] if isinstance(x, dict)]
    else:
        raw_items = raw.get('itemList') or []
        for index, item in enumerate(row['items']):
            item['imageUrl'] = str(raw_items[index].get('itemImage') or '') if index < len(raw_items) else ''
            if random:
                item['percent'] = None
                item['viewMinutes'] = None
    row['officialProgressAvailable'] = account_progress and not random
    row['taskCount'] = len(row['items']) if account_progress else 0
    row['rewardCount'] = len(row['items'])
    reason = ''
    block = 'rules'
    if not str(row['id']).strip(): reason = '缺少官方活动 ID'
    elif not row.get('active'): reason = '活动尚未开放或已结束'
    elif row.get('completed'): reason = '当前账号已完成'
    elif row.get('gameAccountRequirement') == 'required' and row.get('gameAccountLinkState') != 'linked':
        block, reason = 'eligibility', '需要确认当前账号的游戏绑定'
    elif row.get('gameAccountRequirement') == 'unknown':
        block, reason = 'eligibility', '账号资格待确认'
    elif not account_progress and str(raw.get('dupFlag') or '').casefold() in {'true','1','y'}:
        block, reason = 'eligibility', '限次活动；当前账号参与资格待确认'
    elif row['giveCon'] not in {'term', 'none', 'draw'}: reason = '观看机制待确认'
    elif not row['channelIds'] and not row['categoryIds']: reason = '缺少可验证的官方频道规则'
    elif row['giveCon'] == 'term' and not any((x.get('requiredMinutes') or 0) > 0 for x in row['items']): reason = '没有官方观看时长任务'
    row['executionBlockReason'] = reason
    row['executionBlockKind'] = block if reason else ''
    row['executable'] = not reason
    return row


def queue(rows, settings, diagnostics=None):
    selected = settings.get('campaign_selection_mode') == 'selected_only'
    games = ids(settings.get('game_priority')) + ids(settings.get('selected_games'))
    priorities = settings.get('campaign_priorities') or {}
    candidates = []
    for row in rows:
        if row.get('completed'):
            stage = 'completed'
        elif not row.get('active'):
            stage = 'inactive'
        elif selected and row['gameId'] not in settings.get('selected_games', []):
            stage = 'gameFilter'
        elif selected and row['id'] not in priorities.get(row['gameId'], []):
            stage = 'activityFilter'
        elif not row.get('executable'):
            stage = row.get('executionBlockKind') or 'rules'
        else:
            candidates.append(row)
            continue
        if diagnostics is not None:
            diagnostics['activityFilters'][stage] += 1
            if stage in {'eligibility', 'rules'}:
                diagnostics['blockReasons'][row['executionBlockReason']] += 1
    legacy = str(settings.get('priority_mission_id') or 'auto')
    mode = settings.get('campaign_sort_mode', 'custom')
    def rank(row):
        game = row['gameId']
        order = priorities.get(game, [])
        custom = order.index(row['id']) if row['id'] in order else len(order)
        timed = row.get('giveCon') == 'term' and row.get('officialProgressAvailable')
        items = [x for x in row.get('items', []) if not x.get('completed') and (x.get('requiredMinutes') or 0) > 0]
        remaining = max([max(0, x['requiredMinutes'] - (x.get('viewMinutes') or 0)) for x in items], default=float('inf'))
        percents = [(x.get('viewMinutes') or 0) / x['requiredMinutes'] for x in items]
        percent = min(percents, default=0)
        strategy = {'ending': str(row.get('endTime') or row.get('endDate') or '9999'),
                    'remaining': remaining if timed else float('inf'),
                    'most': -percent if timed else float('inf'), 'least': percent if timed else float('inf')}.get(mode, custom)
        return (0 if not selected and row['id'] == legacy else 1,
                games.index(game) if game in games else len(games), strategy, custom, row['id'])
    return sorted(candidates, key=rank)


def category_id(value):
    text = str(value or '').strip()
    return text.lstrip('0') if text.isascii() and text.isdecimal() else ''


def channel_filter(row, channel):
    if not channel or not channel.on_air or not channel.broad_no:
        return 'notLive'
    if row['channelIds']:
        return '' if channel.user_id in row['channelIds'] else 'streamerMismatch'
    official = {category_id(value) for value in row['categoryIds']} - {''}
    if category_id(channel.category_no) not in official:
        return 'categoryMismatch'
    return '' if channel.has_drops else 'dropsDisabled'


def matches(row, channel):
    return channel_filter(row, channel) == ''


class DiscoveryError(RuntimeError):
    """An official channel read did not produce a valid discovery response."""


class DiscoverySession:
    """Validate the Core's read-only discovery responses before it parses them."""

    def __init__(self, session):
        self.session = session

    def get(self, url, **kwargs):
        search_kind = kwargs.get('params', {}).get('m')
        return self._read(self.session.get(url, **kwargs), search_kind)

    def post(self, url, **kwargs):
        return self._read(self.session.post(url, **kwargs), 'player')

    @asynccontextmanager
    async def _read(self, request, kind):
        async with request as response:
            response.raise_for_status()
            data = await response.json(content_type=None)
            if not isinstance(data, dict):
                raise DiscoveryError('SOOP 直播接口响应不是对象')
            if kind == 'player':
                channel = data.get('CHANNEL')
                if not isinstance(channel, dict) or str(channel.get('RESULT')) not in {'0', '1'}:
                    raise DiscoveryError('SOOP 直播间接口未确认在线或离线')
                result = int(channel['RESULT'])
                if result == 1 and not str(channel.get('BNO') or '').strip():
                    raise DiscoveryError('SOOP 在线直播间缺少场次 ID')
                data['CHANNEL'] = {**channel, 'RESULT': result}
            elif kind == 'stopWord' and not data.get('sessionKey'):
                raise DiscoveryError('SOOP 标签搜索未返回会话键')
            elif kind == 'liveSearch':
                rooms = data.get('REAL_BROAD', data.get('realBroad'))
                if not isinstance(rooms, list):
                    raise DiscoveryError('SOOP 标签搜索未返回直播列表')
                if any(not isinstance(room, dict) for room in rooms):
                    raise DiscoveryError('SOOP 标签搜索包含无效直播记录')
                data['REAL_BROAD'] = [{**room, 'is_drops': '1' if str(room.get('is_drops')).casefold() in {'1', 'true'} else '0'}
                                      for room in rooms]
                if 'HAS_MORE_LIST' in data:
                    data['HAS_MORE_LIST'] = str(data['HAS_MORE_LIST']).casefold() in {'true', '1', 'y'}
            yield DiscoveryResponse(data)


class DiscoveryResponse:
    def __init__(self, data):
        self.data = data

    async def json(self, **kwargs):
        return self.data


def install(worker, miner_module, multi_module, channel_module):
    """Replace the manager's class reference with a project-owned subclass."""
    base = getattr(miner_module.SoopMiner, '_cloudlight_queue_base', miner_module.SoopMiner)
    heartbeat_base = getattr(miner_module.WatchHeartbeat, '_cloudlight_heartbeat_base', miner_module.WatchHeartbeat)

    class QueueHeartbeat(heartbeat_base):
        _cloudlight_heartbeat_base = heartbeat_base
        queue_result_target = None
        def switch_channel(self, channel, **kwargs):
            result = super().switch_channel(channel, **kwargs)
            target = worker._queue_targets.get(str(self.uid), {})
            worker.evidence.target(self.uid, channel.user_id, channel.broad_no, target.get('campaignId', ''))
            return result

        async def send(self, session):
            target = worker._queue_targets.get(str(self.uid), {})
            token = worker.evidence.begin(self.uid, self.channel.user_id, self.channel.broad_no, target.get('campaignId', ''))
            try:
                result = await super().send(session)
            except (asyncio.TimeoutError, TimeoutError):
                worker.evidence.finish(self.uid, token, 'timeout')
                raise
            except Exception:
                worker.evidence.finish(self.uid, token, 'failure')
                raise
            # Re-read the target after awaiting; an A -> B -> A switch still
            # changes the epoch and cannot accept the old A response.
            current = worker._queue_targets.get(str(self.uid), {})
            worker.evidence.target(self.uid, self.channel.user_id, self.channel.broad_no, current.get('campaignId', ''))
            if not worker.evidence.finish(self.uid, token, getattr(result, 'value', str(result))):
                self.last_success_time = None
                self.connection_healthy = False
                self.consecutive_failures = 0
                self.last_response_status = miner_module.HeartbeatStatus.UNKNOWN
                self.last_response_result = '频道或活动已切换，旧心跳已失效'
                return miner_module.HeartbeatStatus.UNKNOWN
            self.queue_result_target = token[1][2]
            return result

    class QueueMiner(base):
        _cloudlight_queue_base = base

        def __init__(self, *args, **kwargs):
            super().__init__(*args, **kwargs)
            self._queue_wake = asyncio.Event()
            self._queue_last_scan = None
            self._queue_task_read_failed = False

        def request_queue_rescan(self):
            self._queue_wake.set()

        def _emit_state(self, status=None):
            # Core emits from inside _fetch_missions before our authenticated
            # response timestamp is attached. Publish one consistent payload.
            if getattr(self, '_reading_official', False):
                return
            super()._emit_state(status)

        def _record_official(self, missions):
            values = {}
            campaigns = {}
            for mission in missions:
                for index, item in enumerate(mission.items):
                    if int(item.give_term or 0) <= 0 or (getattr(mission, 'raw', {}) or {}).get('giveCon') in {'none', 'draw'}:
                        continue
                    key = worker._progress_key(str(self.uid), mission, index, item)
                    identity = json.dumps(key[1:], ensure_ascii=True, separators=(',', ':'))
                    values[identity] = int(item.view_time or 0)
                    campaigns[identity] = str(mission.drops_idx)
            worker.evidence.official(self.uid, values, campaigns)

        async def _fetch_missions(self):
            self._reading_official = True
            try:
                await super()._fetch_missions()
                self._record_official(self._missions)
            except Exception as exc:
                worker.evidence.failure(self.uid, '官方读取失败', worker._is_authentication_error(exc))
                raise
            finally:
                self._reading_official = False
                self._emit_state()

        async def run(self):
            self._running = True
            self._stop.clear()
            tasks = []
            try:
                await self._ensure_session()
                await self._drops.ensure_drops_ready(on_info=self._log.info)
                try:
                    await self._refresh_task_snapshot()
                    await self._refresh_inventory()
                except miner_module.DropsAuthenticationError:
                    raise
                except Exception as exc:
                    self._log.warning('初始队列刷新失败，保留账号运行意图并等待恢复: %s', exc)
                # Keep polling even when all chosen events are upcoming or no
                # room is live yet. The Core's run() exits in this situation.
                self._emit_state(self._queue_status())
                tasks = [asyncio.create_task(self._heartbeat_loop()), asyncio.create_task(self._poll_loop()),
                         asyncio.create_task(self._queue_refusal_loop())]
                await asyncio.gather(*tasks)
            except miner_module.DropsAuthenticationError as exc:
                self._handle_auth_expired(exc)
            finally:
                self._running = False
                for task in tasks:
                    task.cancel()
                await asyncio.gather(*tasks, return_exceptions=True)
                worker._queue_targets.pop(self.uid, None)
                if not self._auth_invalid:
                    self._emit_state('已停止')

        async def _queue_refusal_loop(self):
            while not self._stop.is_set():
                if self._refused_target() or self._queue_wake.is_set() or self._waiting_rescan_due():
                    async with self._refresh_lock:
                        refused = self._refused_target()
                        if not self._stop.is_set() and (refused or self._queue_wake.is_set() or self._waiting_rescan_due()):
                            if refused:
                                self._queue_rejected = getattr(self, '_queue_rejected', {})
                                self._queue_rejected[refused] = time.monotonic() + 300
                                self._log.warning('当前账号的官方观看响应明确拒绝目标 %s，暂时排除并重新调度。', refused)
                            self._queue_wake.clear()
                            try:
                                await self._sync_channel()
                            except miner_module.DropsAuthenticationError as exc:
                                self._handle_auth_expired(exc)
                                break
                            except Exception as exc:
                                self._log.warning('SOOP 重新选台失败，等待下一次刷新: %s', type(exc).__name__)
                if self._stop.is_set():
                    break
                try:
                    await asyncio.wait_for(self._queue_wake.wait(), timeout=5)
                except asyncio.TimeoutError:
                    pass

        def _refused_target(self):
            heartbeat = self._heartbeat
            target = getattr(self, '_queue_target', '')
            if (heartbeat and target and self._channel_config.mode != 'manual'
                    and heartbeat.last_response_result in {'明确失败码', '明确失败字段'}
                    and getattr(heartbeat, 'queue_result_target', target) == target
                    and getattr(self, '_queue_rejected', {}).get(target, 0) <= time.monotonic()):
                return target
            return ''

        def _waiting_rescan_due(self):
            return (self._channel_config.mode == 'smart' and self._current is None
                    and self._queue_last_scan is not None
                    and time.monotonic() - self._queue_last_scan >= WAITING_RESCAN_SECONDS)

        async def _refresh_task_snapshot(self):
            previous = (self._missions, self._events, self._available_channels)
            try:
                with getattr(self, '_coalesce_state_updates', nullcontext)():
                    await self._fetch_missions()
                    await self._fetch_events()
                    self._queue_task_read_failed = False
                    await self._sync_channel()
            except Exception:
                self._missions, self._events, self._available_channels = previous
                self._queue_task_read_failed = True
                diagnostics = self._new_queue_diagnostics()
                diagnostics['activityReadFailed'] = True
                self._publish_queue_diagnostics(diagnostics, 'activity_api_error', '官方活动接口读取失败，等待重新刷新')
                worker.evidence.failure(self.uid, '活动刷新失败，保留此前完整快照')
                raise
            self._emit_state()

        async def _fetch_events(self):
            await self._ensure_session()
            try:
                categories = await self._drops._request('POST', 'get_drops_game_category_list.php', json_body={'filter': 'none'})
            except miner_module.DropsAuthenticationError:
                raise
            except Exception:
                self._log.warning('官方游戏目录读取失败，保留已验证名称缓存')
                categories = {'data': []}
            worker.game_catalog.update(categories.get('data') or [])
            names = worker.game_catalog.names
            result = {}
            # 'none' is the official web app's all-lifecycle filter.
            for page in range(1, 201):
                events = await self._drops.get_events(filter='none', page_no=page, page_size=50)
                fresh = [e for e in events if str(e.drops_idx) not in result]
                for event in fresh:
                    if names.get(str(event.raw.get('gameNo'))):
                        event.raw = {**event.raw, 'gameTitle': names[str(event.raw['gameNo'])]}
                    result[str(event.drops_idx)] = event
                if len(events) < 50 or not fresh: break
            self._events = list(result.values())

        def _new_queue_diagnostics(self):
            return {'mode': self._channel_config.mode, 'totalActivities': 0, 'candidateActivities': 0,
                    'activitiesChecked': 0, 'onlineChannels': 0, 'officialProbes': 0,
                    'activityFilters': Counter(), 'channelFilters': Counter(), 'blockReasons': Counter(),
                    'sourceErrors': Counter(), 'activityReadFailed': self._queue_task_read_failed}

        def _publish_queue_diagnostics(self, diagnostics, reason, message):
            diagnostics.update(reason=reason, message=message)
            previous = worker._queue_diagnostics.get(self.uid)
            worker._queue_diagnostics[self.uid] = diagnostics
            if previous != diagnostics:
                self._log.info('SOOP 选台 · %s · %s', message, json.dumps(diagnostics, ensure_ascii=False))

        def _queue_status(self):
            if self._current:
                return '已选择直播间'
            return worker._queue_diagnostics.get(self.uid, {}).get('message', '等待可执行活动或合资格频道')

        async def _resolve_channel(self):
            if self._auth_invalid or self._stop.is_set():
                worker._queue_targets.pop(self.uid, None)
                return None
            self._queue_last_scan = time.monotonic()
            settings = worker._effective_settings()
            mode = self._channel_config.mode
            rows = worker._tasks_for_state(self.get_state(), include_ended=True)
            diagnostics = self._new_queue_diagnostics()
            diagnostics['totalActivities'] = len(rows)
            ranked = queue(rows, settings, diagnostics)
            rejected = getattr(self, '_queue_rejected', {})
            now = time.monotonic()
            candidates = [row for row in ranked if rejected.get(row['id'], 0) <= now]
            diagnostics['activityFilters']['refused'] = len(ranked) - len(candidates)
            diagnostics['candidateActivities'] = len(candidates)
            try:
                await self._ensure_session()
            except miner_module.DropsAuthenticationError:
                raise
            except Exception as exc:
                diagnostics['sourceErrors'][f'session:{type(exc).__name__}'] += 1
                self._publish_queue_diagnostics(diagnostics, 'channel_api_error', '直播间接口连接失败，等待重试')
                worker._queue_targets.pop(self.uid, None)
                worker.evidence.target(self.uid, '', '', '')
                return None

            session = DiscoverySession(self._session)
            seen_online = set()
            checked_streamers = {}
            search = None

            async def read(source, call, default=None):
                try:
                    return await call
                except miner_module.DropsAuthenticationError:
                    raise
                except Exception as exc:
                    diagnostics['sourceErrors'][f'{source}:{type(exc).__name__}'] += 1
                    return default

            def track_online(channel):
                if channel and channel.on_air and channel.broad_no:
                    seen_online.add(channel.user_id)

            async def search_once():
                nonlocal search
                if search is None:
                    search = await read('hashtag', channel_module.fetch_hashtag_drops_channels(session, self.cookies), [])
                    for channel in search:
                        track_online(channel)
                return search

            async def probe(uid):
                diagnostics['officialProbes'] += 1
                unread = object()
                channel = await read('player', channel_module.fetch_live_channel(session, self.cookies, uid), unread)
                if channel is None:
                    diagnostics['channelFilters']['officialOffline'] += 1
                if channel is unread:
                    channel = None
                track_online(channel)
                checked_streamers[uid] = channel

            chosen = target = None
            if mode in {'manual', 'owesports'}:
                picker = channel_module._pick_manual_channel if mode == 'manual' else channel_module._pick_owesports_channel
                chosen = await read(mode, picker(self._session if mode == 'manual' else session, self.cookies, self._channel_config))
                track_online(chosen)
                for row in candidates:
                    diagnostics['activitiesChecked'] += 1
                    if matches(row, chosen):
                        target = row
                        break
                if mode == 'owesports' and settings.get('campaign_selection_mode') == 'selected_only' and target is None:
                    chosen = None
            else:
                for row in candidates:
                    diagnostics['activitiesChecked'] += 1
                    if row['channelIds']:
                        room_ids = row['channelIds']
                        for start in range(0, len(room_ids), PROBE_BATCH_SIZE):
                            batch = room_ids[start:start + PROBE_BATCH_SIZE]
                            await asyncio.gather(*(probe(uid) for uid in batch if uid not in checked_streamers))
                            for uid in batch:
                                channel = checked_streamers[uid]
                                if channel is None:
                                    continue
                                rejected_by = channel_filter(row, channel)
                                if not rejected_by:
                                    chosen = channel
                                    break
                                diagnostics['channelFilters'][rejected_by] += 1
                            if chosen:
                                break
                    else:
                        for channel in await search_once():
                            rejected_by = channel_filter(row, channel)
                            if not rejected_by:
                                chosen = channel
                                break
                            diagnostics['channelFilters'][rejected_by] += 1
                    if chosen:
                        target = row
                        break
                # Keep the existing no-task policy for unmigrated automatic settings.
                if chosen is None and settings.get('campaign_selection_mode') == 'legacy' and not rows and self._channel_config.hang_without_missions and not self._queue_task_read_failed:
                    chosen = await read('legacy', super()._resolve_channel())
                    track_online(chosen)
            previous_id = getattr(self, '_queue_target', '')
            previous = next((r for r in candidates if r['id'] == previous_id), None)
            now = time.monotonic()
            if (mode == 'smart' and chosen and self._current and chosen.user_id != self._current.user_id
                    and previous and matches(previous, self._current)
                    and now - getattr(self, '_queue_switched_at', 0) < settings.get('channel_switch_cooldown', 60)):
                current_id = self._current.user_id
                if current_id not in checked_streamers:
                    await probe(current_id)
                current = checked_streamers[current_id]
                if current and not previous['channelIds']:
                    # The player endpoint proves liveness, not category or Drops status.
                    listed = await search_once()
                    current = next((channel for channel in listed if channel.user_id == current_id and channel.broad_no == current.broad_no), None)
                if matches(previous, current):
                    chosen, target = current, previous
                    diagnostics['cooldownHeld'] = True
            diagnostics['onlineChannels'] = len(seen_online)
            if self._stop.is_set() or self._auth_invalid:
                return None
            if any(settings.get(key) != worker.settings.get(key) for key in LIVE_QUEUE_SETTINGS):
                self.request_queue_rescan()
                chosen = target = None
                reason, message = 'settings_changed', '选台设置已更新，正在重新筛选'
            else:
                reason, message = self._queue_result(diagnostics, chosen, target, settings)
            self._publish_queue_diagnostics(diagnostics, reason, message)
            if chosen and (self._current is None or chosen.user_id != self._current.user_id):
                self._queue_switched_at = now
            self._queue_target = target['id'] if target else ''
            worker.evidence.target(self.uid, chosen.user_id if chosen else '', chosen.broad_no if chosen else '', self._queue_target)
            worker._queue_targets[self.uid] = {'campaignId': self._queue_target, 'gameId': target['gameId'] if target else '',
                'campaignTitle': target['title'] if target else '',
                'channelId': chosen.user_id if chosen else '', 'mode': mode,
                'status': message}
            return chosen

        @staticmethod
        def _queue_result(diagnostics, chosen, target, settings):
            filters = diagnostics['activityFilters']
            if target:
                if diagnostics.get('cooldownHeld'):
                    return 'selected', '当前频道仍合资格，切换冷却中'
                return 'selected', '已找到合资格频道'
            if chosen:
                return 'channel_only', '已选择频道，尚未确认可计入所选活动'
            if diagnostics['activityReadFailed']:
                return 'activity_api_error', '官方活动接口读取失败，等待重新刷新'
            if diagnostics['sourceErrors']:
                return 'channel_api_error', '直播间接口读取失败，暂不能确认是否有合资格频道'
            if diagnostics['mode'] != 'smart':
                return 'restricted_channel', '指定频道未上线或不符合当前活动规则'
            if diagnostics['candidateActivities']:
                if diagnostics['onlineChannels']:
                    return 'no_matching_channel', '发现在线频道，但没有符合当前活动规则的直播间'
                offline = diagnostics['channelFilters'].get('officialOffline', 0)
                if offline and offline == diagnostics.get('officialProbes'):
                    return 'no_online_channel', f'已检查的 {offline} 位指定主播当前均未开播'
                return 'no_online_channel', '当前活动的官方频道均无可用直播'
            if diagnostics['blockReasons'].get('需要确认当前账号的游戏绑定'):
                return 'game_account_required', '需要确认当前账号的游戏绑定'
            if filters['eligibility']:
                return 'account_ineligible', '当前所选活动的账号资格不足或尚未确认'
            if filters['refused']:
                return 'official_refusal', '当前活动收到官方观看拒绝，等待重新核验'
            if filters['rules']:
                return 'rules_unverified', '无法验证所选活动的官方频道或观看规则'
            if settings.get('campaign_selection_mode') == 'selected_only':
                return 'selection_filtered', '当前仅观看所选活动，暂无符合条件的已选活动'
            return 'no_active_activity', '暂无可执行的官方活动'

        async def _sync_channel(self):
            with getattr(self, '_coalesce_state_updates', nullcontext)():
                await super()._sync_channel()
                # Core retains a stale channel when resolve returns None.
                # Publish only after its heartbeat/bridge have been closed.
                status = worker._queue_targets.get(self.uid, {})
                if not status.get('channelId') and self._current is not None:
                    if self._bridge:
                        await self._bridge.close()
                        self._bridge = None
                    self._current = None
                    self._heartbeat = None
            self._emit_state(self._queue_status())

        async def force_refresh(self):
            # The UI refresh must remain read-only (Core's version enables Drops
            # and reads redeem codes through a state-changing detail endpoint).
            async with self._refresh_lock:
                previous = (self._missions, self._events, self._inventory, self._available_channels)
                try:
                    await self._ensure_session()
                    missions = await self._drops.get_missions()
                    await self._fetch_events()
                    inventory = await self._drops.get_inventory(with_codes=False)
                    live, _ = await channel_module.fetch_live_drops_channels(self._session, self.cookies, missions)
                    self._missions, self._inventory, self._available_channels = missions, inventory, live
                    self._record_official(missions)
                    if self._running:
                        await self._sync_channel()
                except Exception as exc:
                    self._missions, self._events, self._inventory, self._available_channels = previous
                    worker.evidence.failure(self.uid, '官方读取失败，保留此前完整快照', worker._is_authentication_error(exc))
                    raise
                self._emit_state()
                return self.get_state()

    miner_module.WatchHeartbeat = QueueHeartbeat
    miner_module.SoopMiner = QueueMiner
    multi_module.SoopMiner = QueueMiner
