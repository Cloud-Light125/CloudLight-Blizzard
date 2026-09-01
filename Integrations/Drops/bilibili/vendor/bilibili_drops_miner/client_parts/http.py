from __future__ import annotations

import asyncio
import logging
from collections.abc import Awaitable, Callable
from typing import Any

import httpx


def is_rate_limited_payload(payload: dict[str, Any]) -> bool:
    code = str(payload.get("code") or "")
    message = str(payload.get("message") or "")
    return code in {"-702", "-509"} or "频率" in message or "频繁" in message


def _should_retry_with_fresh_wbi(
    payload: dict[str, Any],
    *,
    retry: int,
    retry_on_wbi_miss: bool,
) -> bool:
    # 限频需要由调用方按退避策略处理，不能在刷新 WBI 时立即重复请求。
    return (
        retry == 0
        and retry_on_wbi_miss
        and not is_rate_limited_payload(payload)
    )


async def request_with_transient_retry(
    request_coro: Callable[[], Awaitable[httpx.Response]],
    *,
    method: str,
    url: str,
    logger: logging.Logger,
) -> httpx.Response:
    # 高并发时，x25Kn/live API 偶发 ConnectTimeout/ReadTimeout。
    # 对这类瞬时网络异常做短退避重试，避免单次抖动就打断会话。
    delays = (0.35, 0.8)
    attempt_total = len(delays) + 1
    for attempt in range(1, attempt_total + 1):
        try:
            return await request_coro()
        except (
            httpx.ConnectTimeout,
            httpx.ReadTimeout,
            httpx.ConnectError,
            httpx.RemoteProtocolError,
        ) as exc:
            if attempt >= attempt_total:
                raise
            delay = delays[attempt - 1]
            logger.debug(
                "%s %s 网络瞬时异常(%s/%s): %s，%.2fs 后重试",
                method,
                url,
                attempt,
                attempt_total,
                type(exc).__name__,
                delay,
            )
            await asyncio.sleep(delay)

    raise RuntimeError("unreachable retry state")


async def request_with_network_fallback(
    request_coro: Callable[[], Awaitable[httpx.Response]],
    fallback_request_coro: Callable[[], Awaitable[httpx.Response]] | None,
    *,
    method: str,
    url: str,
    logger: logging.Logger,
    on_fallback: Callable[[], None] | None = None,
) -> httpx.Response:
    """Retry a proxy connection through direct only for network failures.

    HTTP responses are deliberately not caught here.  In particular, 401,
    403, 412, 429 and other Bilibili business responses must never trigger a
    route change.
    """

    try:
        return await request_with_transient_retry(
            request_coro,
            method=method,
            url=url,
            logger=logger,
        )
    except (
        httpx.ConnectTimeout,
        httpx.ReadTimeout,
        httpx.ConnectError,
        httpx.RemoteProtocolError,
    ):
        if fallback_request_coro is None:
            raise
        if on_fallback is not None:
            on_fallback()
        logger.warning("%s %s 代理连接失败，回退直连", method, url)
        return await request_with_transient_retry(
            fallback_request_coro,
            method=method,
            url=url,
            logger=logger,
        )


async def signed_get_json(
    *,
    http: httpx.AsyncClient,
    sign_wbi: Callable[[dict[str, Any]], Awaitable[dict[str, Any]]],
    clear_wbi_cache: Callable[[], None],
    logger: logging.Logger,
    url: str,
    params: dict[str, Any],
    headers: dict[str, str] | None = None,
    follow_redirects: bool = False,
    retry_on_wbi_miss: bool = True,
    fallback_http: httpx.AsyncClient | None = None,
    on_network_fallback: Callable[[], None] | None = None,
) -> dict[str, Any]:
    payload: dict[str, Any] = {}
    retries = 2 if retry_on_wbi_miss else 1
    for retry in range(retries):
        signed_params = await sign_wbi(params)
        request = lambda client=http: client.get(
            url,
            params=signed_params,
            headers=headers,
            follow_redirects=follow_redirects,
        )
        fallback_request = None if fallback_http is None else lambda: fallback_http.get(
            url,
            params=signed_params,
            headers=headers,
            follow_redirects=follow_redirects,
        )
        response = await request_with_network_fallback(
            request,
            fallback_request,
            method="GET",
            url=url,
            logger=logger,
            on_fallback=on_network_fallback,
        )
        response.raise_for_status()
        payload = response.json()
        if payload.get("code") == 0:
            return payload
        if _should_retry_with_fresh_wbi(
            payload,
            retry=retry,
            retry_on_wbi_miss=retry_on_wbi_miss,
        ):
            clear_wbi_cache()
            continue
        break
    return payload


async def signed_post_json(
    *,
    http: httpx.AsyncClient,
    sign_wbi: Callable[[dict[str, Any]], Awaitable[dict[str, Any]]],
    clear_wbi_cache: Callable[[], None],
    logger: logging.Logger,
    url: str,
    params: dict[str, Any],
    body: dict[str, Any],
    headers: dict[str, str] | None = None,
    retry_on_wbi_miss: bool = True,
    fallback_http: httpx.AsyncClient | None = None,
    on_network_fallback: Callable[[], None] | None = None,
) -> dict[str, Any]:
    payload: dict[str, Any] = {}
    retries = 2 if retry_on_wbi_miss else 1
    for retry in range(retries):
        signed_params = await sign_wbi(params)
        request = lambda client=http: client.post(
            url,
            params=signed_params,
            json=body,
            headers=headers,
        )
        fallback_request = None if fallback_http is None else lambda: fallback_http.post(
            url,
            params=signed_params,
            json=body,
            headers=headers,
        )
        response = await request_with_network_fallback(
            request,
            fallback_request,
            method="POST",
            url=url,
            logger=logger,
            on_fallback=on_network_fallback,
        )
        response.raise_for_status()
        payload = response.json()
        if payload.get("code") == 0:
            return payload
        if _should_retry_with_fresh_wbi(
            payload,
            retry=retry,
            retry_on_wbi_miss=retry_on_wbi_miss,
        ):
            clear_wbi_cache()
            continue
        break
    return payload


async def signed_post_query_json(
    *,
    http: httpx.AsyncClient,
    sign_wbi: Callable[[dict[str, Any]], Awaitable[dict[str, Any]]],
    clear_wbi_cache: Callable[[], None],
    logger: logging.Logger,
    url: str,
    params: dict[str, Any],
    headers: dict[str, str] | None = None,
    follow_redirects: bool = False,
    retry_on_wbi_miss: bool = True,
    fallback_http: httpx.AsyncClient | None = None,
    on_network_fallback: Callable[[], None] | None = None,
) -> dict[str, Any]:
    payload: dict[str, Any] = {}
    retries = 2 if retry_on_wbi_miss else 1
    for retry in range(retries):
        signed_params = await sign_wbi(params)
        request = lambda client=http: client.post(
            url,
            params=signed_params,
            headers=headers,
            follow_redirects=follow_redirects,
        )
        fallback_request = None if fallback_http is None else lambda: fallback_http.post(
            url,
            params=signed_params,
            headers=headers,
            follow_redirects=follow_redirects,
        )
        response = await request_with_network_fallback(
            request,
            fallback_request,
            method="POST",
            url=url,
            logger=logger,
            on_fallback=on_network_fallback,
        )
        response.raise_for_status()
        payload = response.json()
        if payload.get("code") == 0:
            return payload
        if _should_retry_with_fresh_wbi(
            payload,
            retry=retry,
            retry_on_wbi_miss=retry_on_wbi_miss,
        ):
            clear_wbi_cache()
            continue
        break
    return payload


async def signed_post_form_json(
    *,
    http: httpx.AsyncClient,
    sign_wbi: Callable[[dict[str, Any]], Awaitable[dict[str, Any]]],
    clear_wbi_cache: Callable[[], None],
    logger: logging.Logger,
    url: str,
    params: dict[str, Any],
    body: dict[str, Any],
    headers: dict[str, str] | None = None,
    retry_on_wbi_miss: bool = True,
    fallback_http: httpx.AsyncClient | None = None,
    on_network_fallback: Callable[[], None] | None = None,
) -> dict[str, Any]:
    payload: dict[str, Any] = {}
    retries = 2 if retry_on_wbi_miss else 1
    for retry in range(retries):
        signed_params = await sign_wbi(params)
        request = lambda client=http: client.post(
            url,
            params=signed_params,
            data=body,
            headers=headers,
        )
        fallback_request = None if fallback_http is None else lambda: fallback_http.post(
            url,
            params=signed_params,
            data=body,
            headers=headers,
        )
        response = await request_with_network_fallback(
            request,
            fallback_request,
            method="POST",
            url=url,
            logger=logger,
            on_fallback=on_network_fallback,
        )
        response.raise_for_status()
        payload = response.json()
        if payload.get("code") == 0:
            return payload
        if _should_retry_with_fresh_wbi(
            payload,
            retry=retry,
            retry_on_wbi_miss=retry_on_wbi_miss,
        ):
            clear_wbi_cache()
            continue
        break
    return payload
