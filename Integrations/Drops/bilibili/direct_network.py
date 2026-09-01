"""Explicit networking policy for the Bilibili Drops worker.

This module is deliberately dependency-free until a client factory is called
and must be imported before any HTTP client library.  The worker is a
separate process, so removing proxy variables here cannot alter the proxy
behaviour of the other Drops workers.  Even in proxy mode, the proxy is
passed explicitly to httpx; environment proxy variables are never trusted.
"""

from __future__ import annotations

import os
from dataclasses import dataclass
from typing import Any, Mapping
from urllib.parse import urlsplit


PROXY_ENVIRONMENT_NAMES = (
    "HTTP_PROXY",
    "HTTPS_PROXY",
    "ALL_PROXY",
    "http_proxy",
    "https_proxy",
    "all_proxy",
)


def scrub_proxy_environment(environment: Mapping[str, str] | None = None) -> tuple[str, ...]:
    """Remove inherited proxy settings from the worker process.

    ``NO_PROXY=*`` is intentionally set in this child process as a second
    defence for libraries that inspect NO_PROXY even when their proxy option
    is not explicitly configured.  Proxy mode still works because the
    selected CloudLight proxy is passed explicitly to httpx with
    ``trust_env=False``.  The parent process and other workers are never
    changed.
    """

    source = os.environ if environment is None else environment
    removed = tuple(name for name in PROXY_ENVIRONMENT_NAMES if name in source)
    if environment is None:
        for name in PROXY_ENVIRONMENT_NAMES:
            os.environ.pop(name, None)
        os.environ["NO_PROXY"] = "*"
        os.environ["no_proxy"] = "*"
    return removed


def direct_httpx_kwargs() -> dict[str, Any]:
    """Common options for a direct httpx client used by the adapter."""

    return {"trust_env": False, "proxy": None}


@dataclass(frozen=True, slots=True)
class BilibiliNetworkPolicy:
    """The only network inputs accepted by Bilibili HTTP clients."""

    use_proxy: bool = False
    proxy_url: str = ""
    fallback_direct: bool = False

    @property
    def has_explicit_proxy(self) -> bool:
        return self.use_proxy and bool(self.proxy_url.strip())

    @property
    def mode(self) -> str:
        return "PROXY" if self.has_explicit_proxy else "DIRECT"

    @classmethod
    def direct(cls) -> "BilibiliNetworkPolicy":
        return cls()


def build_network_policy(
    *,
    use_proxy: object = False,
    proxy_url: object = "",
    fallback_direct: object = False,
) -> BilibiliNetworkPolicy:
    """Normalize the host-provided policy without reading process settings."""

    return BilibiliNetworkPolicy(
        use_proxy=bool(use_proxy),
        proxy_url=str(proxy_url or "").strip(),
        fallback_direct=bool(fallback_direct),
    )


def network_httpx_kwargs(policy: BilibiliNetworkPolicy | None = None) -> dict[str, Any]:
    """Return explicit httpx options for the requested policy."""

    selected = policy or BilibiliNetworkPolicy.direct()
    return {
        "trust_env": False,
        "proxy": selected.proxy_url if selected.has_explicit_proxy else None,
    }


def create_http_client(
    policy: BilibiliNetworkPolicy | None = None,
    **kwargs: Any,
):
    """Create a synchronous httpx client with an explicit route."""

    import httpx

    options = dict(kwargs)
    options.update(network_httpx_kwargs(policy))
    return httpx.Client(**options)


def create_async_client(
    policy: BilibiliNetworkPolicy | None = None,
    **kwargs: Any,
):
    """Create an asynchronous httpx client with an explicit route."""

    import httpx

    options = dict(kwargs)
    options.update(network_httpx_kwargs(policy))
    return httpx.AsyncClient(**options)


def safe_proxy_label(proxy_url: str) -> str:
    """Return a log/UI-safe proxy endpoint without user info or credentials."""

    raw = str(proxy_url or "").strip()
    try:
        parsed = urlsplit(raw)
        host = parsed.hostname
        if not host:
            return "未配置"
        host_text = f"[{host}]" if ":" in host and not host.startswith("[") else host
        return f"{host_text}:{parsed.port}" if parsed.port else host_text
    except (TypeError, ValueError):
        return "地址无效"


def proxy_environment_is_clean(environment: Mapping[str, str] | None = None) -> bool:
    source = os.environ if environment is None else environment
    return not any(name in source and source[name] for name in PROXY_ENVIRONMENT_NAMES)
