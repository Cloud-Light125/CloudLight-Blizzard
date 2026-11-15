import asyncio
import json
import ssl
import sys
import unittest
from pathlib import Path
from types import SimpleNamespace
from unittest.mock import AsyncMock, Mock
from contextlib import asynccontextmanager
import aiohttp
from yarl import URL

ROOT=Path(__file__).resolve().parents[1]
for path in (ROOT.parent,ROOT,ROOT/'core'): sys.path.insert(0,str(path))
from network_policy import request, replayable, RouteFailure, UnsafeReplayFailure, failure_stage
from oauth import read_oauth
from exceptions import OAuthException

class Backoff:
    def __init__(self,**kwargs): pass
    def __iter__(self): return iter([0,0,0])

class NetworkPolicyTests(unittest.IsolatedAsyncioTestCase):
    async def asyncSetUp(self):
        self.session=SimpleNamespace(timeout=SimpleNamespace(total=15),request=AsyncMock())
        async def unless(coro): return await coro
        async def wait(): await asyncio.sleep(3600)
        self.client=SimpleNamespace(settings=SimpleNamespace(proxy='http://user:private@127.0.0.1:7897',_cloudlight_fallback_direct=True),
            get_session=AsyncMock(return_value=self.session),
            gui=SimpleNamespace(close_requested=False,coro_unless_closed=unless,wait_until_closed=wait,
                                connection=SimpleNamespace(update=Mock()),network_request=Mock()))

    def response(self,status=200): return SimpleNamespace(status=status,read=AsyncMock(),release=Mock())
    async def get(self):
        async with request(self.client,'GET','https://id.twitch.tv/oauth2/validate?token=secret',None,{},Backoff) as response:
            return response.status

    async def test_proxy_success_never_direct(self):
        self.session.request.return_value=self.response()
        self.assertEqual(await self.get(),200)
        self.assertEqual(self.session.request.await_count,1)
        self.assertTrue(self.session.request.call_args.kwargs['proxy'])

    async def test_proxy_failure_direct_success(self):
        self.session.request.side_effect=[TimeoutError(),self.response()]
        self.assertEqual(await self.get(),200)
        self.assertIsNone(self.session.request.call_args_list[1].kwargs['proxy'])

    async def test_both_fail_then_proxy_recovers(self):
        self.session.request.side_effect=[TimeoutError(),TimeoutError(),self.response()]
        self.assertEqual(await self.get(),200)
        self.assertEqual([bool(c.kwargs['proxy']) for c in self.session.request.call_args_list],[True,False,True])

    async def test_all_fail_preserves_both_exceptions(self):
        self.session.request.side_effect=TimeoutError
        with self.assertRaises(RouteFailure) as error: await self.get()
        self.assertEqual(len(error.exception.errors),3)
        self.assertEqual(len(error.exception.records),3)

    async def test_real_backoff_cap_cannot_create_infinite_attempts(self):
        from utils import ExponentialBackoff
        async def closed(): return None
        self.client.gui.wait_until_closed=closed
        self.session.request.side_effect=TimeoutError
        with self.assertRaises(RouteFailure):
            async with request(self.client,'GET','https://id.twitch.tv/oauth2/validate',None,{},ExponentialBackoff): pass
        self.assertEqual(self.session.request.await_count,3)

    async def test_proxy_update_is_idempotent_and_preserves_inactive_intent(self):
        from worker import TwitchWorker
        worker=object.__new__(TwitchWorker)
        worker.proxy={'enableProxy':False,'proxyUrl':'','fallbackDirect':False}
        worker._proxy_configured=False
        worker.on_proxy_changed=Mock()
        policy={'enableProxy':True,'proxyUrl':'http://127.0.0.1:7897','fallbackDirect':True}
        worker.set_proxy(policy);worker.set_proxy(policy)
        self.assertEqual(worker.on_proxy_changed.call_count,1)
        with self.assertRaises(ValueError): worker.set_proxy({'enableProxy':True,'proxyUrl':'invalid'})
        self.assertEqual(worker.proxy,policy)

    async def test_fallback_forbidden(self):
        self.client.settings._cloudlight_fallback_direct=False
        self.session.request.side_effect=[TimeoutError(),self.response()]
        await self.get()
        self.assertTrue(all(c.kwargs['proxy'] for c in self.session.request.call_args_list))

    async def test_next_independent_request_proxy_first(self):
        self.session.request.side_effect=[TimeoutError(),self.response(),self.response()]
        await self.get(); await self.get()
        self.assertTrue(self.session.request.call_args_list[-1].kwargs['proxy'])

    async def test_407_does_not_fallback(self):
        error=aiohttp.ClientHttpProxyError(SimpleNamespace(real_url=URL('https://id.twitch.tv')),(),status=407,message='fixture')
        self.session.request.side_effect=error
        with self.assertRaises(aiohttp.ClientHttpProxyError): await self.get()
        self.assertEqual(self.session.request.await_count,1)

    async def test_certificate_never_downgrades(self):
        key=SimpleNamespace(host='id.twitch.tv',port=443,ssl=True)
        self.session.request.side_effect=aiohttp.ClientConnectorCertificateError(key,ssl.SSLCertVerificationError(1,'fixture'))
        with self.assertRaises(aiohttp.ClientConnectorCertificateError): await self.get()
        self.assertEqual(self.session.request.await_count,1)
        self.assertEqual(self.client._cloudlight_network_diagnostics[-1]['stage'],'TLS')

    async def test_http_401_is_reached_not_proxy_failure(self):
        self.session.request.return_value=self.response(401)
        self.assertEqual(await self.get(),401)
        self.assertEqual(self.session.request.await_count,1)

    async def test_http_403_not_proxy_failure(self):
        self.session.request.return_value=self.response(403)
        self.assertEqual(await self.get(),403)

    async def test_http_429_not_proxy_failure(self):
        self.session.request.return_value=self.response(429)
        self.assertEqual(await self.get(),429)
        self.assertEqual(self.session.request.await_count,1)

    async def test_http_5xx_bounded_same_route(self):
        responses=[self.response(503) for _ in range(3)]
        self.session.request.side_effect=responses
        with self.assertRaises(Exception):
            async with request(self.client,'GET','https://www.twitch.tv/',None,{},Backoff): pass
        self.assertEqual(self.session.request.await_count,3)
        self.assertTrue(all(c.kwargs['proxy'] for c in self.session.request.call_args_list))
        self.assertTrue(all(r.release.called for r in responses))

    async def test_oauth_post_transport_not_replayed(self):
        self.session.request.side_effect=TimeoutError
        with self.assertRaises(UnsafeReplayFailure) as error:
            async with request(self.client,'POST','https://id.twitch.tv/oauth2/token',None,{'data':{'secret':'private'}},Backoff): pass
        self.assertFalse(error.exception.retryable)
        self.assertEqual(self.session.request.await_count,1)

    async def test_claim_post_not_replayed(self):
        self.session.request.side_effect=TimeoutError
        with self.assertRaises(UnsafeReplayFailure):
            async with request(self.client,'POST','https://gql.twitch.tv/gql',None,{'json':{'operationName':'ClaimDrop'}},Backoff): pass
        self.assertEqual(self.session.request.await_count,1)

    async def test_gql_mutation_service_error_is_not_replayed_by_outer_layer(self):
        import headless_gui
        sys.modules['gui']=headless_gui
        import constants
        constants.LANG_PATH=ROOT/'core/lang'
        import twitch
        from exceptions import GQLException
        calls=[]
        @asynccontextmanager
        async def fake(*args,**kwargs):
            calls.append(1)
            yield SimpleNamespace(json=AsyncMock(return_value={'errors':[{'message':'service unavailable'}]}))
        client=SimpleNamespace(_qgl_limiter=asyncio.Semaphore(1),request=fake,
            _client_type=SimpleNamespace(USER_AGENT='fixture'),
            get_auth=AsyncMock(return_value=SimpleNamespace(headers=lambda **kw:{})))
        with self.assertRaises(GQLException):
            await twitch.Twitch.gql_request(client,constants.GQL_QUERIES['ClaimDrop'])
        self.assertEqual(len(calls),1)

    async def test_readonly_gql_can_fallback(self):
        self.session.request.side_effect=[TimeoutError(),self.response()]
        async with request(self.client,'POST','https://gql.twitch.tv/gql',None,{'json':{'operationName':'Inventory'}},Backoff): pass
        self.assertEqual(self.session.request.await_count,2)

    async def test_replay_policy_matches_actual_persisted_operations(self):
        from constants import GQL_QUERIES
        for key in ('Inventory','Campaigns','CampaignDetails','PlaybackAccessToken','GetStreamInfo','GameDirectory','AvailableDrops'):
            self.assertTrue(replayable('POST','https://gql.twitch.tv/gql',{'json':GQL_QUERIES[key]}),key)
        for key in ('ClaimDrop','ClaimCommunityPoints','NotificationsDelete','NotificationsView'):
            self.assertFalse(replayable('POST','https://gql.twitch.tv/gql',{'json':GQL_QUERIES[key]}),key)

    async def test_consumer_json_error_never_replays(self):
        response=self.response()
        self.session.request.return_value=response
        with self.assertRaises(ValueError):
            async with request(self.client,'GET','https://www.twitch.tv/',None,{},Backoff): raise ValueError('fixture')
        self.assertEqual(self.session.request.await_count,1)
        response.release.assert_called_once()

    async def test_cancel_does_not_fallback(self):
        self.session.request.side_effect=asyncio.CancelledError
        with self.assertRaises(asyncio.CancelledError): await self.get()
        self.assertEqual(self.session.request.await_count,1)
        self.assertEqual(self.client._cloudlight_network_diagnostics[-1]['stage'],'Cancelled')

    async def test_diagnostics_have_no_credentials_or_query(self):
        self.session.request.return_value=self.response()
        await self.get()
        text=json.dumps(self.client._cloudlight_network_diagnostics)
        for secret in ('private','secret','user:','token=','Authorization'): self.assertNotIn(secret,text)

    async def test_untrusted_401_keeps_session(self):
        with self.assertRaises(OAuthException) as error:
            await read_oauth(SimpleNamespace(status=401,json=AsyncMock(return_value={})), 'validate')
        self.assertEqual(error.exception.code,'oauth_invalid_response')
        self.assertTrue(error.exception.retryable)

    async def test_official_invalid_token_explicit(self):
        with self.assertRaises(OAuthException) as error:
            await read_oauth(SimpleNamespace(status=401,json=AsyncMock(return_value={'status':401,'message':'invalid access token'})), 'validate')
        self.assertEqual(error.exception.code,'invalid_token')

    async def test_oauth_post_5xx_not_replayed_by_auth_layer(self):
        import headless_gui
        sys.modules['gui']=headless_gui
        import constants
        constants.LANG_PATH=ROOT/'core/lang'
        import twitch
        response=SimpleNamespace(status=503,json=AsyncMock(return_value={}))
        calls=[]
        @asynccontextmanager
        async def fake(*args,**kwargs):
            calls.append(1)
            yield response
        auth=object.__new__(twitch._AuthState)
        auth._twitch=SimpleNamespace(request=fake)
        with self.assertRaises(OAuthException) as error:
            await auth._oauth_request('token','POST','https://id.twitch.tv/oauth2/token')
        self.assertEqual(error.exception.code,'oauth_post_not_replayed')
        self.assertEqual(len(calls),1)

if __name__=='__main__': unittest.main()
