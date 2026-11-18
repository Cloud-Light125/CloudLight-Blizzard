"""Campaign scheduling policy, separate from game preference and UI sorting."""
from __future__ import annotations

MODES = {"ending", "remaining", "progress_high", "progress_low", "custom"}


def validate_priorities(value):
    if not isinstance(value, dict):
        raise ValueError("活动观看优先级必须是对象")
    for game_id, entry in value.items():
        if (not isinstance(game_id, str) or not game_id or not isinstance(entry, dict)
                or entry.get("mode", "ending") not in MODES
                or not isinstance(entry.get("campaignIds", []), list)
                or not all(isinstance(cid, str) and cid for cid in entry.get("campaignIds", []))
                or len(set(entry.get("campaignIds", []))) != len(entry.get("campaignIds", []))):
            raise ValueError("活动观看优先级格式无效")
    return value


def remote_remaining(drop, seen=frozenset()):
    if drop.id in seen or drop.is_claimed:
        return 0
    return max(0, drop.required_minutes - drop.real_current_minutes) + max(
        (remote_remaining(drop.campaign.timed_drops[pid], seen | {drop.id})
         for pid in drop.precondition_drops), default=0)


def remaining(campaign):
    return max((remote_remaining(d) for d in campaign.drops), default=0)


def campaign_key(campaign, priorities):
    entry = priorities.get(str(campaign.game.id), {})
    mode = entry.get("mode", "ending")
    end = campaign.ends_at
    left = remaining(campaign)
    required = max((d.total_required_minutes for d in campaign.drops), default=0)
    progress = (required - left) / required if required else 0
    ids = entry.get("campaignIds", [])
    primary = (ids.index(campaign.id) if campaign.id in ids else len(ids)) if mode == "custom" else (
        left if mode == "remaining" else -progress if mode == "progress_high" else
        progress if mode == "progress_low" else end.timestamp())
    return primary, end, campaign.id


def ordered(campaigns, priorities):
    return sorted(campaigns, key=lambda c: campaign_key(c, priorities))
