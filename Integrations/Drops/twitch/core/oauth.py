"""Device grant validation. Never log response values or authorization secrets."""
from __future__ import annotations

import logging
from urllib.parse import urlsplit

from exceptions import OAuthException

logger = logging.getLogger("TwitchDrops")
_ERROR_MESSAGES = {
    "invalid_client": "Twitch 授权接口拒绝当前 Client ID，请更新程序后重试授权。",
    "access_denied": "已取消 Twitch 授权，请点击重试授权。",
    "expired_token": "Twitch 授权码已过期，请点击重试授权。",
    "invalid_grant": "Twitch 授权码无效或已过期，请点击重试授权。",
    "invalid_token": "Twitch 登录信息已失效，请重新授权。",
    "authorization_pending": "等待用户完成 Twitch 授权。",
    "slow_down": "Twitch 要求降低授权确认频率。",
}


async def read_oauth(response, stage: str) -> dict:
    try:
        data = await response.json()
    except (ValueError, UnicodeError, TypeError):
        data = None
    # aiohttp.ContentTypeError is a ClientResponseError, not ValueError.
    except Exception as exc:
        if exc.__class__.__name__ != "ContentTypeError":
            raise
        data = None
    code = ""
    if isinstance(data, dict):
        raw = data.get("error") or data.get("message") or data.get("error_description")
        normalized = str(raw or "").strip().lower().replace(" ", "_")
        code = normalized if normalized in _ERROR_MESSAGES else ""
    logger.info("Twitch OAuth stage=%s HTTP=%s shape=%s fields=%s error=%s",
                stage, response.status, type(data).__name__,
                sorted(data) if isinstance(data, dict) else [], code or "unspecified")
    if response.status == 429:
        raise OAuthException("oauth_rate_limited", "Twitch 授权服务器限流（HTTP 429），稍后自动重试。", retryable=True)
    if response.status >= 500:
        raise OAuthException("oauth_server_error", f"Twitch 授权服务器暂时故障（HTTP {response.status}），稍后自动重试。", retryable=True)
    if stage == "token" and code in {"authorization_pending", "slow_down"} and response.status == 400:
        return {"error": code}
    if response.status != 200 or code:
        raise OAuthException(code or "oauth_rejected",
                             _ERROR_MESSAGES.get(code, f"Twitch 授权请求被拒绝（HTTP {response.status}），请重试授权或检查代理。"))
    if not isinstance(data, dict):
        raise OAuthException("oauth_invalid_response", "Twitch 授权响应格式异常（不是 JSON 对象），请检查网络或代理。", retryable=True)
    return data


def validate_device(data: dict) -> None:
    for name in ("device_code", "user_code", "verification_uri"):
        if not isinstance(data.get(name), str) or not data[name].strip():
            raise OAuthException("oauth_missing_fields", "Twitch 授权响应缺少必需字段，请重试授权或检查代理。", retryable=True)
    for name in ("interval", "expires_in"):
        if type(data.get(name)) is not int or not 0 < data[name] <= 86400:
            raise OAuthException("oauth_missing_fields", "Twitch 授权有效期或轮询间隔异常，请重试授权。", retryable=True)
    try:
        url = urlsplit(data["verification_uri"])
        trusted = url.scheme == "https" and url.hostname in {"www.twitch.tv", "twitch.tv", "id.twitch.tv"} and not url.username and not url.password and url.port in {None, 443}
    except ValueError:
        trusted = False
    if not trusted:
        raise OAuthException("oauth_untrusted_url", "Twitch 返回的授权地址不是可信官方 HTTPS 地址，已停止授权。")
