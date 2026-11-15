"""Bounded Twitch request routing. Diagnostics never contain headers/query/body."""
from __future__ import annotations
import asyncio
import json
import logging
import errno
import time
from contextlib import asynccontextmanager, suppress
from datetime import datetime, timedelta, timezone
import aiohttp
from yarl import URL
from exceptions import ExitRequest, RequestException, RequestInvalid

logger = logging.getLogger('TwitchDrops.network')
READ_QUERIES = frozenset({'Inventory','ViewerDropsDashboard','DropCampaignDetails',
    'DropsHighlightService_AvailableDrops','PlaybackAccessToken','VideoPlayerStreamInfoOverlayChannel',
    'DirectoryPage_Game','DirectoryGameRedirect','DropCurrentSessionContext',
    'ChannelPointsContext','OnsiteNotifications_ListNotifications'})


def safe_proxy(value):
    if not value: return ''
    try:
        url = URL(value)
        return f'{url.scheme}://{url.host}:{url.port}'
    except (ValueError, TypeError): return '<invalid>'


def replayable(method, url, kwargs):
    if method in {'GET','HEAD','OPTIONS'}: return not any(k in kwargs for k in ('data','json'))
    if method != 'POST' or URL(url).host != 'gql.twitch.tv' or 'data' in kwargs: return False
    ops = kwargs.get('json')
    ops = ops if isinstance(ops,list) else [ops]
    return bool(ops) and all(isinstance(op,dict) and op.get('operationName') in READ_QUERIES for op in ops)


def failure_stage(exc, route):
    if isinstance(exc, asyncio.CancelledError): return 'Cancelled'
    if isinstance(exc,(aiohttp.ClientConnectorCertificateError,aiohttp.ClientSSLError)): return 'TLS'
    if isinstance(exc,aiohttp.ClientHttpProxyError): return 'CONNECT'
    if isinstance(exc,aiohttp.ClientProxyConnectionError): return 'ProxyTCP'
    if isinstance(exc,aiohttp.ClientConnectorDNSError): return 'DNS'
    if isinstance(exc,aiohttp.ClientPayloadError): return 'ResponseBody'
    if isinstance(exc,asyncio.TimeoutError): return 'ConnectionOrTLS' if 'Connection' in type(exc).__name__ else 'Timeout'
    if isinstance(exc,aiohttp.ClientConnectorError):
        return 'TCP' if getattr(exc.os_error,'errno',None) in {errno.ECONNREFUSED,errno.ENETUNREACH,errno.EHOSTUNREACH} else 'EndpointConnectOrTLS'
    return 'Transport'


class RouteFailure(RequestException):
    category = 'network_route'
    def __init__(self, records, errors):
        self.records, self.errors = records, errors
        self.code = 'proxy_and_direct_failure'
        self.retryable = True
        super().__init__('Twitch 无法连接：代理和直连回退均失败；Session 尚未验证。')


class UnsafeReplayFailure(RequestException):
    category = 'network_route'
    code = 'unsafe_request_not_replayed'
    retryable = False
    def __init__(self):
        super().__init__('Twitch 请求结果无法确认；为避免重复提交，未自动重放。Session 保留，请手动重试。')


def report(client, record):
    history = getattr(client,'_cloudlight_network_diagnostics',[])
    client._cloudlight_network_diagnostics = [*history[-15:], record]
    logger.info('Twitch request %s', json.dumps(record,ensure_ascii=False))
    callback = getattr(client.gui,'network_request',None)
    if callable(callback): callback(record)


@asynccontextmanager
async def request(client, method, url, invalidate_after, kwargs, backoff_factory):
    session = await client.get_session()
    method = method.upper()
    original = dict(kwargs)
    proxy = original.pop('proxy', client.settings.proxy)
    safe = replayable(method,url,original)
    fallback = bool(proxy and safe and getattr(client.settings,'_cloudlight_fallback_direct',False))
    original.setdefault('timeout',aiohttp.ClientTimeout(total=15,sock_connect=10))
    session_timeout = timedelta(seconds=session.timeout.total or 0)
    parsed = URL(url)
    path = parsed.path if parsed.path in {'/','/gql','/oauth2/validate','/oauth2/device','/oauth2/token'} else '/<redacted>'
    route = 'Proxy' if proxy else 'Direct'
    records, errors = [], []
    last_error = None
    for attempt, delay in zip(range(1,4 if safe else 2),backoff_factory(maximum=3)):
        if client.gui.close_requested: raise ExitRequest()
        if invalidate_after is not None and datetime.now(timezone.utc) >= invalidate_after-session_timeout: raise RequestInvalid()
        options = dict(original)
        if route == 'Proxy' and proxy: options['proxy'] = proxy
        # Explicit None also prevents a proxy accidentally leaking from kwargs.
        else: options['proxy'] = None
        started = time.monotonic()
        response = None
        successful = False
        record = {'host':parsed.host,'path':path,'method':method,'route':route,
                  'proxy':safe_proxy(proxy) if route=='Proxy' else '', 'attempt':attempt,'replayable':safe}
        next_route = route
        try:
            response = await client.gui.coro_unless_closed(session.request(method,url,**options))
            record['http'] = response.status
            await response.read()
            record['stage'] = 'HTTP'
            if response.status < 500 or parsed.host == 'id.twitch.tv' or not safe:
                successful = True
            else:
                last_error = RequestException(f'Twitch HTTP {response.status}: 服务器暂时不可用')
        except asyncio.CancelledError:
            record.update(stage='Cancelled',exception='CancelledError')
            raise
        except ExitRequest:
            record.update(stage='Stopped',exception='ExitRequest')
            raise
        except (aiohttp.ClientConnectionError,asyncio.TimeoutError,aiohttp.ClientPayloadError,aiohttp.ClientHttpProxyError) as exc:
            last_error = exc
            errors.append(exc)
            record.update(stage=failure_stage(exc,route),exception=type(exc).__name__)
            underlying = getattr(exc,'os_error',None) or exc.__cause__
            if underlying is not None:
                record['cause'] = type(underlying).__name__
                record['errno'] = getattr(underlying,'errno',None)
            if getattr(exc,'status',None): record['http'] = exc.status
            if isinstance(exc,(aiohttp.ClientConnectorCertificateError,aiohttp.ClientSSLError)):
                client.gui.connection.update('ssl_certificate_failed','certificate_verify_failed')
                raise
            if isinstance(exc,aiohttp.ClientHttpProxyError) and exc.status == 407:
                client.gui.connection.update('proxy_auth_failed','proxy_authentication_required')
                raise
            if route == 'Proxy' and fallback:
                next_route = 'DirectFallback'
                client.gui.connection.update('proxy_fallback','proxy_failure')
            elif route == 'DirectFallback':
                # A failed direct attempt must not strand recovery on Direct.
                next_route = 'Proxy'
        finally:
            record['elapsedMs'] = round((time.monotonic()-started)*1000)
            records.append(record)
            report(client,record)
            if response is not None and not successful: response.release()
        if successful:
            # Consumer exceptions (OAuth/JSON/GQL) must never replay a request.
            try: yield response
            finally: response.release()
            return
        if attempt >= (3 if safe else 1): break
        previous_route, route = route, next_route
        if previous_route == 'Proxy' and route == 'DirectFallback': continue
        with suppress(asyncio.TimeoutError):
            await asyncio.wait_for(client.gui.wait_until_closed(),timeout=delay)
    if not safe:
        client.gui.connection.update('unsafe_request_failed','unsafe_request_not_replayed')
        raise UnsafeReplayFailure() from last_error
    client.gui.connection.update('request_failed','proxy_and_direct_failure' if any(r['route']=='DirectFallback' for r in records) else 'connection_failed')
    if any(r['route']=='DirectFallback' for r in records): raise RouteFailure(records,errors) from last_error
    raise last_error or RequestException('Twitch 请求失败')
