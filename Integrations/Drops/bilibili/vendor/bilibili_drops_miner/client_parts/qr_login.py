from __future__ import annotations

from dataclasses import dataclass
from enum import Enum
from typing import Any, Callable, Mapping

import httpx

from direct_network import BilibiliNetworkPolicy, create_http_client


QR_GENERATE_URL = (
    "https://passport.bilibili.com/x/passport-login/web/qrcode/generate"
)
QR_POLL_URL = "https://passport.bilibili.com/x/passport-login/web/qrcode/poll"

LOGIN_COOKIE_NAMES = (
    "SESSDATA",
    "bili_jct",
    "DedeUserID",
    "DedeUserID__ckMd5",
    "buvid3",
    "b_nut",
    "sid",
)
REQUIRED_LOGIN_COOKIE_NAMES = ("SESSDATA", "DedeUserID", "bili_jct")

_HEADERS = {
    "User-Agent": (
        "Mozilla/5.0 (Windows NT 10.0; Win64; x64) "
        "AppleWebKit/537.36 (KHTML, like Gecko) "
        "Chrome/122.0.0.0 Safari/537.36"
    ),
    "Referer": "https://passport.bilibili.com/login",
    "Accept": "application/json, text/plain, */*",
}


class QrLoginError(RuntimeError):
    """A safe-to-display QR login failure that never contains credentials."""


class QrLoginStatus(Enum):
    UNSCANNED = 86101
    CONFIRMED_PENDING = 86090
    EXPIRED = 86038
    SUCCESS = 0


@dataclass(frozen=True, slots=True)
class QrLoginChallenge:
    url: str
    key: str


@dataclass(frozen=True, slots=True)
class QrPollResult:
    status: QrLoginStatus
    cookie: str = ""


def _payload_data(payload: Any, operation: str) -> Mapping[str, Any]:
    if not isinstance(payload, Mapping):
        raise QrLoginError(f"{operation}响应格式无效")
    outer_code = payload.get("code")
    if isinstance(outer_code, bool) or not isinstance(outer_code, int):
        raise QrLoginError(f"{operation}响应缺少有效状态码")
    if outer_code != 0:
        raise QrLoginError(f"{operation}失败（接口状态异常）")
    data = payload.get("data")
    if not isinstance(data, Mapping):
        raise QrLoginError(f"{operation}响应缺少 data")
    return data


def parse_generate_payload(payload: Any) -> QrLoginChallenge:
    data = _payload_data(payload, "生成二维码")
    url = data.get("url")
    key = data.get("qrcode_key")
    if not isinstance(url, str) or not url.startswith("https://"):
        raise QrLoginError("生成二维码响应缺少有效地址")
    if not isinstance(key, str) or not key.strip():
        raise QrLoginError("生成二维码响应缺少有效标识")
    return QrLoginChallenge(url=url, key=key)


def parse_poll_payload(payload: Any) -> QrLoginStatus:
    data = _payload_data(payload, "查询扫码状态")
    code = data.get("code")
    if isinstance(code, bool) or not isinstance(code, int):
        raise QrLoginError("查询扫码状态响应缺少有效状态码")
    try:
        return QrLoginStatus(code)
    except ValueError as exc:
        raise QrLoginError(f"未知扫码状态码：{code}") from exc


def build_login_cookie_string(
    response_cookies: httpx.Cookies,
    client_cookies: httpx.Cookies,
) -> str:
    """Select the safe login cookies obtained by the successful poll request."""

    selected: dict[str, str] = {}
    # The client jar contains cookies accepted from this session, while the
    # response jar lets the successful poll response take precedence.
    for cookie_jar in (client_cookies.jar, response_cookies.jar):
        for cookie in cookie_jar:
            if cookie.name in LOGIN_COOKIE_NAMES and cookie.value:
                selected[cookie.name] = cookie.value

    missing = [name for name in REQUIRED_LOGIN_COOKIE_NAMES if not selected.get(name)]
    if missing:
        raise QrLoginError("扫码成功，但登录 Cookie 不完整，请重新扫码")
    return "; ".join(
        f"{name}={selected[name]}" for name in LOGIN_COOKIE_NAMES if name in selected
    )


class QrLoginApi:
    """One Bilibili QR login session backed by one HTTP client/cookie jar."""

    def __init__(
        self,
        client: httpx.Client | None = None,
        *,
        network_policy: BilibiliNetworkPolicy | None = None,
        on_network_fallback: Callable[[], None] | None = None,
    ) -> None:
        self._network_policy = network_policy or BilibiliNetworkPolicy.direct()
        self._on_network_fallback = on_network_fallback
        self._network_fallback_active = False
        self._client = client or create_http_client(
            self._network_policy,
            headers=_HEADERS,
            timeout=httpx.Timeout(10.0, connect=10.0),
            follow_redirects=False,
        )
        self._fallback_client = (
            create_http_client(
                BilibiliNetworkPolicy.direct(),
                headers=_HEADERS,
                timeout=httpx.Timeout(10.0, connect=10.0),
                follow_redirects=False,
            )
            if client is None and self._network_policy.has_explicit_proxy and self._network_policy.fallback_direct
            else None
        )
        self._cookie_client = self._client

    @property
    def network_mode(self) -> str:
        return "DIRECT" if self._network_fallback_active else self._network_policy.mode

    def _get(self, url: str, **kwargs: Any) -> httpx.Response:
        try:
            return self._client.get(url, **kwargs)
        except (
            httpx.ConnectTimeout,
            httpx.ReadTimeout,
            httpx.ConnectError,
            httpx.RemoteProtocolError,
        ):
            if self._fallback_client is None:
                raise
            self._network_fallback_active = True
            self._fallback_client.cookies.update(self._client.cookies)
            self._cookie_client = self._fallback_client
            if self._on_network_fallback is not None:
                self._on_network_fallback()
            return self._fallback_client.get(url, **kwargs)

    def generate(self) -> QrLoginChallenge:
        response = self._get(QR_GENERATE_URL)
        response.raise_for_status()
        try:
            payload = response.json()
        except ValueError as exc:
            raise QrLoginError("生成二维码响应不是有效 JSON") from exc
        return parse_generate_payload(payload)

    def poll(self, key: str) -> QrPollResult:
        response = self._get(QR_POLL_URL, params={"qrcode_key": key})
        response.raise_for_status()
        try:
            payload = response.json()
        except ValueError as exc:
            raise QrLoginError("查询扫码状态响应不是有效 JSON") from exc
        status = parse_poll_payload(payload)
        if status is not QrLoginStatus.SUCCESS:
            return QrPollResult(status=status)
        cookie = build_login_cookie_string(response.cookies, self._cookie_client.cookies)
        return QrPollResult(status=status, cookie=cookie)

    def close(self) -> None:
        self._client.close()
        if self._fallback_client is not None:
            self._fallback_client.close()

    def __enter__(self) -> QrLoginApi:
        return self

    def __exit__(self, *_args: object) -> None:
        self.close()
