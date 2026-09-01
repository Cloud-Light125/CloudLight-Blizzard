from __future__ import annotations

from typing import Any, Callable

import httpx

from bilibili_drops_miner.client_parts.cookies import DEFAULT_USER_AGENT
from bilibili_drops_miner.utils import extract_bili_live_task_groups
from direct_network import BilibiliNetworkPolicy, create_http_client

LIVE_ROOM_URL = "https://live.bilibili.com/{room_id}"


def fetch_live_task_groups(
    room_id: int,
    *,
    transport: httpx.BaseTransport | None = None,
    timeout: httpx.Timeout | float | None = None,
    network_policy: BilibiliNetworkPolicy | None = None,
    on_network_fallback: Callable[[], None] | None = None,
) -> list[dict[str, Any]]:
    """Fetch and parse task groups from a live room's static HTML."""
    if room_id <= 0:
        raise ValueError("room_id must be greater than zero")

    url = LIVE_ROOM_URL.format(room_id=room_id)
    request_timeout = timeout or httpx.Timeout(15.0, connect=5.0)
    headers = {
        "User-Agent": DEFAULT_USER_AGENT,
        "Referer": "https://live.bilibili.com/",
    }
    client_options: dict[str, Any] = {
        "headers": headers,
        "follow_redirects": True,
        "timeout": request_timeout,
    }
    if transport is not None:
        client_options["transport"] = transport
    policy = network_policy or BilibiliNetworkPolicy.direct()
    with create_http_client(
        policy,
        **client_options,
    ) as client:
        try:
            response = client.get(url)
            response.raise_for_status()
        except (
            httpx.ConnectTimeout,
            httpx.ReadTimeout,
            httpx.ConnectError,
            httpx.RemoteProtocolError,
        ):
            if not (policy.has_explicit_proxy and policy.fallback_direct):
                raise
            if on_network_fallback is not None:
                on_network_fallback()
            with create_http_client(
                BilibiliNetworkPolicy.direct(),
                **client_options,
            ) as fallback_client:
                response = fallback_client.get(url)
                response.raise_for_status()
    return extract_bili_live_task_groups(response.text)
