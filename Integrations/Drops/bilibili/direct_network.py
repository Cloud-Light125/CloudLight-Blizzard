"""Direct-only networking policy for the Bilibili Drops worker.

This module is deliberately dependency-free and must be imported before any
HTTP client library.  The worker is a separate process, so removing proxy
variables here cannot alter the proxy behaviour of the other Drops workers.
"""

from __future__ import annotations

import os
from typing import Mapping


PROXY_ENVIRONMENT_NAMES = (
    "HTTP_PROXY",
    "HTTPS_PROXY",
    "ALL_PROXY",
    "http_proxy",
    "https_proxy",
    "all_proxy",
)


def scrub_proxy_environment(environment: Mapping[str, str] | None = None) -> tuple[str, ...]:
    """Remove inherited proxy settings and make the process direct-only.

    ``NO_PROXY=*`` is intentionally set in this child process as a second
    defence for libraries that inspect NO_PROXY even when their proxy option
    is not explicitly configured.  The parent process and other workers are
    never changed.
    """

    source = os.environ if environment is None else environment
    removed = tuple(name for name in PROXY_ENVIRONMENT_NAMES if name in source)
    if environment is None:
        for name in PROXY_ENVIRONMENT_NAMES:
            os.environ.pop(name, None)
        os.environ["NO_PROXY"] = "*"
        os.environ["no_proxy"] = "*"
    return removed


def direct_httpx_kwargs() -> dict[str, bool]:
    """Common options for every httpx client used by the adapter."""

    return {"trust_env": False}


def proxy_environment_is_clean(environment: Mapping[str, str] | None = None) -> bool:
    source = os.environ if environment is None else environment
    return not any(name in source and source[name] for name in PROXY_ENVIRONMENT_NAMES)
