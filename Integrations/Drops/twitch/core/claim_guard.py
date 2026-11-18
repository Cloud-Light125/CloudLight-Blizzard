"""One claim transport for manual, batch and automatic claiming, on the client loop."""
from __future__ import annotations

import asyncio
import hashlib
import json
import logging
import os
import tempfile
from contextlib import asynccontextmanager
from pathlib import Path
from time import monotonic

MESSAGES = {
    "claimed": "Twitch 已确认领取成功。",
    "already_claimed": "该奖励已领取，无需重复操作。",
    "not_claimable": "奖励尚不可领取；请刷新库存确认进度、前置条件或领取期限。",
    "not_found": "库存中未找到该奖励，请刷新后确认。",
    "not_running": "请先连接当前 Twitch 账号并启动服务。",
    "authentication_error": "Twitch 登录已失效，请重新登录。",
    "permission_error": "Twitch 拒绝领取：当前账号权限或资格不满足。可前往 Twitch 官方背包确认。",
    "rate_limited": "Twitch 请求限流，请稍后重试。",
    "failed": "Twitch 未确认领取，请刷新并等待至少 60 秒后重试，或前往 Twitch 官方背包。",
    "unconfirmed": "领取结果待确认；不会自动重发。请稍后刷新官方库存。",
    "inventory_error": "官方库存读取失败；未提交领取请求。请刷新后重试。",
    "not_submitted": "已停止本批次；该奖励未提交。",
}


class ClaimCoordinator:
    """One reentrant transaction gate for inventory, UI, batch and Core claims.

    Persist an intent before transport. A killed Worker cannot forget an ambiguous
    request. The journal contains hashes of instance IDs, never credentials.
    """
    def __init__(self, client):
        self.client = client
        self.lock = asyncio.Lock()
        self.owner = None
        self.tasks = set()
        self.batch = False
        self.attempts = 0
        self.journal = None
        store = getattr(client.settings, "_store", None)
        self.path = Path(store.path).with_name("claim-journal.json") if store else None
        client._claim_results = getattr(client, "_claim_results", {})

    @asynccontextmanager
    async def transaction(self):
        task = asyncio.current_task()
        if self.owner is task:
            yield self
            return
        self.tasks.add(task)
        try:
            async with self.lock:
                self.owner = task
                try:
                    yield self
                finally:
                    self.owner = None
        finally:
            self.tasks.discard(task)

    async def cancel_pending(self):
        tasks = [t for t in self.tasks if t is not asyncio.current_task()]
        for task in tasks:
            task.cancel()
        if tasks:
            await asyncio.gather(*tasks, return_exceptions=True)

    def instance_key(self, drop):
        return hashlib.sha256((drop.claim_id or drop.campaign.id + "/" + drop.id).encode()).hexdigest()

    @staticmethod
    def drop_key(drop):
        return drop.campaign.id + "/" + drop.id

    def pending(self, drop):
        if self.journal is None:
            # Invalid or unreadable journal fails closed, without overwriting it.
            journal = json.loads(self.path.read_text("utf-8")) if self.path and self.path.exists() else {}
            if (not isinstance(journal, dict)
                    or any(len(k) != 64 or any(ch not in "0123456789abcdef" for ch in k) or v != "unconfirmed"
                           for k, v in journal.items())):
                raise ValueError("领取记录无效；请保留文件并核对官方库存。")
            self.journal = journal
        key = self.instance_key(drop)
        drop_key = self.drop_key(drop)
        if key in self.journal and drop_key not in self.client._claim_results:
            self.client._claim_results[drop_key] = {"status": "unconfirmed", "at": monotonic()}
        return self.client._claim_results.get(drop_key)

    def record(self, drop, status):
        self.pending(drop)
        key = self.instance_key(drop)
        if status == "unconfirmed":
            self.journal[key] = status
        else:
            self.journal.pop(key, None)
        if self.path:
            fd, name = tempfile.mkstemp(prefix="claim-journal-", suffix=".tmp", dir=self.path.parent)
            try:
                with os.fdopen(fd, "w", encoding="utf-8") as stream:
                    json.dump(self.journal, stream)
                    stream.flush()
                    os.fsync(stream.fileno())
                os.replace(name, self.path)
            finally:
                if os.path.exists(name):
                    os.unlink(name)
        self.client._claim_results[self.drop_key(drop)] = {"status": status, "at": monotonic()}


def coordinator(client):
    if not hasattr(client, "_claim_coordinator"):
        client._claim_coordinator = ClaimCoordinator(client)
    return client._claim_coordinator


async def claim_once(drop, operation):
    client = drop._twitch
    gate = coordinator(client)
    async with gate.transaction():
        previous = gate.pending(drop)
        if drop.is_claimed:
            gate.record(drop, "already_claimed")
            return True
        if previous and (previous["status"] in {"claimed", "already_claimed", "unconfirmed"}
                         or monotonic() - previous["at"] < 60):
            return previous["status"] in {"claimed", "already_claimed"}
        status = "not_claimable"
        if (drop.can_claim and drop.watch_requirement_completed and drop.campaign.eligible
                and not getattr(client, "_claim_stopping", False) and not client.gui.close_requested):
            # Record uncertainty before the first await: cancellation must never enable replay.
            gate.record(drop, "unconfirmed")
            gate.attempts += 1
            logging.getLogger("TwitchDrops").info("手动/自动领取提交：Campaign %s · Drop %s", drop.campaign.id, drop.id)
            try:
                async with asyncio.timeout(30):
                    accepted = await operation()
                    # Transport acceptance is not Inventory confirmation.
                    if accepted:
                        return True
                    status = "failed"
            except Exception as exc:
                code = getattr(exc, "status", 0)
                status = ("authentication_error" if code == 401 or getattr(exc, "category", "") == "oauth" or type(exc).__name__ in {"LoginException", "CaptchaRequired"}
                          else "permission_error" if code == 403 else "rate_limited" if code == 429 else "unconfirmed")
        gate.record(drop, status)
        return status in {"claimed", "already_claimed"}
