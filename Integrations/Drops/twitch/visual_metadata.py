"""Serialize existing Core objects only. No fetching, API discovery, cache, or business decisions.

Sources: upstream inventory.Benefit.image_url / DropsCampaign.image_url;
gui.py displays every benefit, not just the first one. The host preserves this list.
"""
from __future__ import annotations

from typing import Any
from urllib.parse import urlsplit


def image_url(value: Any) -> str:
    text = str(value or "")
    try:
        parsed = urlsplit(text)
        if parsed.scheme == "https" and parsed.hostname and not parsed.username and not parsed.password:
            return text
    except ValueError:
        pass
    return ""


def campaign_visual(campaign: Any) -> dict[str, Any]:
    return {"gameImageUrl": image_url(getattr(campaign, "image_url", ""))}


def drop_visual(drop: Any, campaign: Any = None) -> dict[str, Any]:
    campaign = campaign if campaign is not None else getattr(drop, "campaign", None)
    benefits = [
        {"id": str(getattr(benefit, "id", "")), "name": str(getattr(benefit, "name", "")),
         "type": str(getattr(getattr(benefit, "type", None), "value", "UNKNOWN")),
         "imageUrl": image_url(getattr(benefit, "image_url", ""))}
        for benefit in (getattr(drop, "benefits", None) or [])
    ]
    return {
        "benefits": benefits,
        "benefitIds": [benefit["id"] for benefit in benefits],
        "gameImageUrl": image_url(getattr(campaign, "image_url", "")),
        "game": str(getattr(getattr(campaign, "game", None), "name", "")),
        "campaignId": str(getattr(campaign, "id", "")),
    }
