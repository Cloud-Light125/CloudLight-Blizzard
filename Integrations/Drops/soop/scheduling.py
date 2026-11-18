"""CloudLight-owned SOOP queue adapter. The pinned Core is never edited.

Only dropsIdx joins catalog and missions; only official game/category IDs join
games and channels. Account state and the current target live on each miner.
"""
from __future__ import annotations

import time
import asyncio
from contextlib import nullcontext
from typing import Any

QUEUE_DEFAULTS = {
    'queue_version': 1, 'campaign_selection_mode': 'legacy',
    'selected_games': [], 'game_priority': [], 'campaign_priorities': {}, 'campaign_order': {},
    'campaign_sort_mode': 'custom', 'channel_switch_cooldown': 60,
}


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
    if not str(row['id']).strip(): reason = '缺少官方活动 ID'
    elif not row.get('active'): reason = '活动尚未开放或已结束'
    elif row.get('completed'): reason = '当前账号已完成'
    elif row.get('gameAccountRequirement') == 'required' and row.get('gameAccountLinkState') != 'linked': reason = '需要确认当前账号的游戏绑定'
    elif row.get('gameAccountRequirement') == 'unknown': reason = '账号资格待确认'
    elif not account_progress and str(raw.get('dupFlag') or '').casefold() in {'true','1','y'}: reason = '限次活动；当前账号参与资格待确认'
    elif row['giveCon'] not in {'term', 'none', 'draw'}: reason = '观看机制待确认'
    elif not row['channelIds'] and not row['categoryIds']: reason = '缺少可验证的官方频道规则'
    elif row['giveCon'] == 'term' and not any((x.get('requiredMinutes') or 0) > 0 for x in row['items']): reason = '没有官方观看时长任务'
    row['executionBlockReason'] = reason
    row['executable'] = not reason
    return row


def queue(rows, settings):
    candidates = [x for x in rows if x.get('executable')]
    selected = settings.get('campaign_selection_mode') == 'selected_only'
    games = ids(settings.get('game_priority')) + ids(settings.get('selected_games'))
    priorities = settings.get('campaign_priorities') or {}
    if selected:
        candidates = [x for x in candidates if x['gameId'] in settings.get('selected_games', []) and x['id'] in priorities.get(x['gameId'], [])]
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


def matches(row, channel):
    if not channel or not channel.on_air or not channel.broad_no:
        return False
    if row['channelIds']:
        return channel.user_id in row['channelIds']
    return bool(channel.has_drops and str(channel.category_no or '') in row['categoryIds'])


def install(worker, miner_module, multi_module, channel_module):
    """Replace the manager's class reference with a project-owned subclass."""
    base = getattr(miner_module.SoopMiner, '_cloudlight_queue_base', miner_module.SoopMiner)
    heartbeat_base = getattr(miner_module.WatchHeartbeat, '_cloudlight_heartbeat_base', miner_module.WatchHeartbeat)

    class QueueHeartbeat(heartbeat_base):
        _cloudlight_heartbeat_base = heartbeat_base
        async def send(self, session):
            target = (self.channel.user_id, self.channel.broad_no)
            result = await super().send(session)
            if target != (self.channel.user_id, self.channel.broad_no):
                # A delayed old-room response cannot certify or reject the new
                # channel after a scheduler switch on the same heartbeat object.
                self.last_success_time = None
                self.connection_healthy = False
                self.consecutive_failures = 0
                self.last_response_status = miner_module.HeartbeatStatus.UNKNOWN
                self.last_response_result = '频道已切换，等待当前频道心跳'
                return miner_module.HeartbeatStatus.UNKNOWN
            return result

    class QueueMiner(base):
        _cloudlight_queue_base = base
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
                self._emit_state('挂机中' if self._current else '等待可执行活动或合资格频道')
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
                heartbeat = self._heartbeat
                if heartbeat and heartbeat.last_response_result in {'明确失败码', '明确失败字段'}:
                    async with self._refresh_lock:
                        target = getattr(self, '_queue_target', '')
                        if target and self._channel_config.mode != 'manual':
                            self._queue_rejected = getattr(self, '_queue_rejected', {})
                            self._queue_rejected[target] = time.monotonic() + 300
                            self._log.warning('当前账号的官方观看响应明确拒绝目标 %s，暂时排除并重新调度。', target)
                            try:
                                await self._sync_channel()
                            except miner_module.DropsAuthenticationError:
                                raise
                            except Exception as exc:
                                self._log.warning('拒绝后的重新调度暂时失败，等待下一次刷新: %s', exc)
                try:
                    await asyncio.wait_for(self._stop.wait(), timeout=5)
                except asyncio.TimeoutError:
                    pass

        async def _refresh_task_snapshot(self):
            previous = (self._missions, self._events, self._available_channels)
            try:
                with getattr(self, '_coalesce_state_updates', nullcontext)():
                    await self._fetch_missions()
                    await self._fetch_events()
                    await self._sync_channel()
            except Exception:
                self._missions, self._events, self._available_channels = previous
                raise
            self._emit_state()

        async def _fetch_events(self):
            await self._ensure_session()
            categories = await self._drops._request('POST', 'get_drops_game_category_list.php', json_body={'filter': 'none'})
            names = {str(x['gameIdx']): str(x.get('gameTitle') or '') for x in categories.get('data') or [] if isinstance(x, dict) and x.get('gameIdx')}
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

        async def _resolve_channel(self):
            await self._ensure_session()
            if self._auth_invalid or self._stop.is_set():
                worker._queue_targets.pop(self.uid, None)
                return None
            settings = worker._effective_settings()
            mode = self._channel_config.mode
            rows = worker._tasks_for_state(self.get_state(), include_ended=True)
            candidates = queue(rows, settings)
            candidates = [row for row in candidates if getattr(self, '_queue_rejected', {}).get(row['id'], 0) <= time.monotonic()]
            chosen = None
            target = None
            if mode == 'manual':
                chosen = await channel_module._pick_manual_channel(self._session, self.cookies, self._channel_config)
                target = next((r for r in candidates if matches(r, chosen)), None)
            elif mode == 'owesports':
                chosen = await channel_module._pick_owesports_channel(self._session, self.cookies, self._channel_config)
                target = next((r for r in candidates if matches(r, chosen)), None)
                if settings.get('selected_games'):
                    self._log.info('仅守望先锋赛事频道模式限制当前队列；其他游戏将等待用户更改频道模式。')
                if settings.get('campaign_selection_mode') == 'selected_only' and target is None: chosen = None
            else:
                live = None
                for row in candidates:
                    if row['channelIds']:
                        for uid in row['channelIds']:
                            channel = await channel_module.fetch_live_channel(self._session, self.cookies, uid)
                            if matches(row, channel):
                                chosen, target = channel, row
                                break
                    else:
                        if live is None:
                            live = await channel_module.fetch_hashtag_drops_channels(self._session, self.cookies)
                        chosen = next((ch for ch in live if matches(row, ch)), None)
                        if chosen: target = row
                    if chosen: break
                # Preserve the previous no-task waiting policy only for unmigrated
                # automatic settings. An explicit empty queue means no watching.
                if chosen is None and settings.get('campaign_selection_mode') == 'legacy' and not rows and self._channel_config.hang_without_missions:
                    chosen = await super()._resolve_channel()
            previous_id = getattr(self, '_queue_target', '')
            previous = next((r for r in candidates if r['id'] == previous_id), None)
            now = time.monotonic()
            if mode == 'smart' and chosen and self._current and chosen.user_id != self._current.user_id and previous and matches(previous, self._current) and now - getattr(self, '_queue_switched_at', 0) < settings.get('channel_switch_cooldown', 60):
                # Confirm the old room is still live before retaining it.
                current = await channel_module.fetch_live_channel(self._session, self.cookies, self._current.user_id)
                if matches(previous, current): chosen, target = current, previous
            if chosen and (self._current is None or chosen.user_id != self._current.user_id):
                self._queue_switched_at = now
            self._queue_target = target['id'] if target else ''
            worker._queue_targets[self.uid] = {'campaignId': self._queue_target, 'gameId': target['gameId'] if target else '',
                'channelId': chosen.user_id if chosen else '', 'mode': mode,
                'status': '手动频道；所选活动资格未确认' if chosen and not target else ('执行所选活动' if target else '等待可执行活动或合资格频道')}
            return chosen

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
            self._emit_state('挂机中' if self._current else '等待可执行活动或合资格频道')

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
                    if self._running:
                        await self._sync_channel()
                except Exception:
                    self._missions, self._events, self._inventory, self._available_channels = previous
                    raise
                self._emit_state()
                return self.get_state()

    miner_module.WatchHeartbeat = QueueHeartbeat
    miner_module.SoopMiner = QueueMiner
    multi_module.SoopMiner = QueueMiner
