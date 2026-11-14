"""Targeted SOOP API updates without modifying the licensed/pinned Core.

Based on tom1230123/soop-drops-miner f003ba7c0e (2026-11-08): the official
inventory use endpoint accepts both itemCodeIdx and itemIdx. Keep CloudLight's
response + inventory-transition verification and fresh account binding checks.
"""
from __future__ import annotations

from typing import Any, Callable
from datetime import datetime, timedelta, timezone


def inventory_unavailable(raw: dict, parse_datetime: Callable = datetime.fromisoformat) -> bool:
    if str(raw.get("renewFlag") or "").upper() == "Y":
        return True
    if str(raw.get("expFlag") or "").upper() != "Y":
        return False
    try:
        expiry = parse_datetime(raw.get("expDate") or raw.get("useExpDate") or "")
        if expiry is None:
            return True
        if expiry.tzinfo is None:
            expiry = expiry.replace(tzinfo=timezone(timedelta(hours=9)))
        return expiry <= datetime.now(timezone.utc)
    except (TypeError, ValueError, OverflowError):
        return True


def install_drops_compat(module: Any, binding_block: Callable[[dict], str | None]) -> None:
    if getattr(module.DropsClient, "_cloudlight_api_compat", False):
        return
    base = module.DropsClient

    class CompatibleDropsClient(base):
        _cloudlight_api_compat = True

        async def _request(self, method: str, path: str, **kwargs: Any) -> dict:
            if path != "get_drops_use_info.php":
                return await super()._request(method, path, **kwargs)
            headers = {"User-Agent": module.USER_AGENT, "Accept": "application/json",
                       "Origin": module.DROPS_ORIGIN,
                       "Referer": kwargs.get("referer") or f"{module.DROPS_ORIGIN}/inventory"}
            async with self._session.request(method, f"{module.DROPS_API}/{path}",
                                             headers=headers, json=kwargs.get("json_body")) as response:
                data = await response.json(content_type=None)
                if response.status == 401:
                    raise module.DropsAuthenticationError("Drops API 认证失败（HTTP 401）")
                if response.status >= 400:
                    raise RuntimeError(f"Drops API HTTP {response.status}")
                if not isinstance(data, dict):
                    raise ValueError("SOOP 领取响应不是 JSON 对象")
                if data.get("result") in (-1, "-1"):
                    # A business rejection is not proof that the session expired.
                    raise RuntimeError("SOOP 拒绝领取，请到官方背包检查奖励状态")
                return data

        async def get_item_detail_response(self, item_code_idx: str) -> dict:
            # The session belongs to exactly one UID. Never reuse another
            # account's cache or an old row to build a state-changing request.
            items = await self.get_inventory(with_codes=False)
            item = next((row for row in items if str(row.item_code_idx) == str(item_code_idx)), None)
            if item is None:
                raise ValueError("所属账号的最新背包中不存在该物品，已停止领取")
            raw = getattr(item, "raw", {}) or {}
            if not item.claimed:
                blocked = binding_block(raw)
                if blocked:
                    raise ValueError("奖励账号绑定状态不允许领取：" + blocked)
                if inventory_unavailable(raw):
                    raise ValueError("奖励已过期、有效期不明或处于二次发放等待状态，已停止领取")
            body: dict[str, Any] = {"itemCodeIdx": str(item_code_idx)}
            item_idx = raw.get("itemIdx")
            if item_idx is not None and str(item_idx).strip():
                text = str(item_idx).strip()
                body["itemIdx"] = int(text) if text.isascii() and text.isdigit() else text
            return await self._request(
                "POST", "get_drops_use_info.php", json_body=body,
                referer=f"{module.DROPS_ORIGIN}/inventory",
            )

    module.DropsClient = CompatibleDropsClient
