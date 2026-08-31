from __future__ import annotations

import threading
from types import ModuleType

_ANYIO_BACKEND_LOCK = threading.Lock()
_ANYIO_ASYNCIO_BACKEND: ModuleType | None = None


def ensure_anyio_asyncio_backend_ready() -> None:
    """Warm up anyio's asyncio backend before concurrent httpx clients use it."""
    global _ANYIO_ASYNCIO_BACKEND

    if _ANYIO_ASYNCIO_BACKEND is not None:
        return

    with _ANYIO_BACKEND_LOCK:
        if _ANYIO_ASYNCIO_BACKEND is not None:
            return

        from anyio._backends import _asyncio as asyncio_backend

        if not hasattr(asyncio_backend, "CancelScope"):
            raise RuntimeError("anyio asyncio backend is missing CancelScope")

        _ANYIO_ASYNCIO_BACKEND = asyncio_backend
