from __future__ import annotations

import asyncio
import sys
import unittest
from contextlib import asynccontextmanager
from pathlib import Path
from types import SimpleNamespace
from unittest.mock import AsyncMock, patch

ROOT = Path(__file__).resolve().parents[1]
for p in (ROOT.parent, ROOT, ROOT / "core"):
    sys.path.insert(0, str(p))
import headless_gui
sys.modules["gui"] = headless_gui
import constants
constants.LANG_PATH = ROOT / "core" / "lang"
import twitch
from oauth import read_oauth, validate_device
from exceptions import OAuthException


class OAuthRecoveryTests(unittest.IsolatedAsyncioTestCase):
    async def test_smartbox_watch_page_and_background_failure_are_not_silent(self):
        from channel import Channel, Stream
        from exceptions import MinerException
        reporter = headless_gui.WatchRequestStatus()
        with patch.object(headless_gui, "event") as reported:
            reporter.update({"channelId": "fixture", "lastResult": "accepted", "attempts": 1})
            reporter.update({"channelId": "fixture", "lastResult": "accepted", "attempts": 2})
            reporter.update({"channelId": "fixture", "lastResult": "failed", "failure": {"stage": "media_segment", "httpStatus": 403}})
            reporter.update({"channelId": "fixture", "lastResult": "accepted", "attempts": 4})
            self.assertEqual(reported.call_count, 3)
            self.assertTrue(all(call.args[0] == "watch_request" for call in reported.call_args_list))
        client = SimpleNamespace(_client_type=constants.ClientType.SMARTBOX,
                                 settings=SimpleNamespace(available_drops_check=True))
        channel = object.__new__(Channel)
        channel._twitch = client
        channel._login = "fixture-channel"
        client.gql_request = AsyncMock()
        for invalid_token in (None, [], {}, {"data": None}, {"data": []},
                              {"data": {"streamPlaybackAccessToken": {"value": 1, "signature": "fixture"}}}):
            client.gql_request.return_value = invalid_token
            stream = Stream(channel, id=1, game=None, viewers=1, title="fixture")
            self.assertIsNone(await stream.get_stream_url())
            self.assertEqual(client._cloudlight_watch_failure["stage"], "playback_token")
        self.assertEqual(str(channel.url), "https://www.twitch.tv/fixture-channel")
        self.assertIs(client._client_type, constants.ClientType.SMARTBOX)
        client._client_type = constants.ClientType.ANDROID_APP
        self.assertEqual(str(channel.url), str(constants.ClientType.ANDROID_APP.CLIENT_URL) + "/fixture-channel")
        channel._stream = SimpleNamespace(get_stream_url=AsyncMock(return_value="https://fixture.invalid/live.m3u8"))
        playlist = "#EXTM3U\nhttps://fixture.invalid/segment-1.ts\nhttps://fixture.invalid/segment-2.ts\n#EXT-X-ENDLIST"
        calls = []
        head_status = 200
        @asynccontextmanager
        async def watch_request(method, url, **kwargs):
            calls.append((method, str(url)))
            yield SimpleNamespace(status=200 if method == "GET" else head_status, text=AsyncMock(return_value=playlist))
        client.request = watch_request
        with patch.object(Channel, "_send_watch_spade", new=AsyncMock()) as spade:
            self.assertTrue(await channel.send_watch())
            self.assertEqual([call[0] for call in calls], ["GET", "HEAD", "HEAD"])
            spade.assert_not_awaited()
        for invalid in ("[]", "{}", "", "#EXTM3U\n#EXT-X-ENDLIST"):
            playlist = invalid
            self.assertFalse(await channel.send_watch())
        playlist = "#EXTM3U\nhttps://fixture.invalid/segment-1.ts"
        head_status = 403
        self.assertFalse(await channel.send_watch())
        old_stream = channel._stream
        @asynccontextmanager
        async def offline_request(*args, **kwargs):
            channel._stream = None
            yield SimpleNamespace(status=403)
        client.request = offline_request
        self.assertFalse(await channel.send_watch())
        self.assertIsNone(old_stream._stream_url)
        channel._stream = old_stream
        @asynccontextmanager
        async def cancelled_request(*args, **kwargs):
            raise asyncio.CancelledError()
            yield
        client.request = cancelled_request
        with self.assertRaises(asyncio.CancelledError):
            await channel.send_watch()
        core = object.__new__(twitch.Twitch)
        core.settings = SimpleNamespace(dump=False)
        core._run = AsyncMock()
        core._watching_task = asyncio.get_running_loop().create_future()
        core._watching_task.set_exception(MinerException("watch fixture failed"))
        core._mnt_task = None
        with self.assertRaisesRegex(MinerException, "watch fixture failed"):
            await core.run()

    async def test_cached_inventory_is_preserved_until_success_and_scoped_to_identity(self):
        from worker import TwitchWorker
        worker = object.__new__(TwitchWorker)
        worker._last_snapshot = {"inventory": [{"id": "old-drop"}]}
        worker._snapshot_user_id = 1
        worker._authenticated_user_id = None
        worker._client = SimpleNamespace(inventory=[], _auth_state=SimpleNamespace())
        self.assertEqual(worker.get_inventory({}), [{"id": "old-drop"}])
        worker._client._cloudlight_inventory_loaded = True
        self.assertEqual(worker.get_inventory({}), [])
        worker._client._cloudlight_inventory_loaded = False
        worker._client._auth_state.user_id = 2
        self.assertEqual(worker.get_inventory({}), [])
        worker._last_snapshot = {"inventory": [{"id": "old-drop"}]}
        worker._snapshot_user_id = 1
        worker._client = None
        worker._authenticated_user_id = 2
        self.assertEqual(worker.get_inventory({}), [])
        error = OAuthException("invalid_client", "授权接口拒绝 Client ID")
        self.assertEqual(TwitchWorker._connection_error(error), ("oauth", "invalid_client", "授权接口拒绝 Client ID", False))
        import threading
        worker._require_core = lambda: None
        worker._client_stopped = threading.Event()
        worker._client = SimpleNamespace(_cloudlight_login_state="logged_in", _auth_state=SimpleNamespace())
        worker._authenticated_user_id = 2
        worker.running = False
        worker.load_state = lambda _: {"running": False}
        self.assertTrue(worker.get_accounts({})[0]["loggedIn"])
        self.assertTrue(worker.start({})["startPending"])
        self.assertTrue(worker._continue_after_login)

    async def test_error_shapes_and_trusted_device_response(self):
        for status, body, expected in (
            (400, {"status": 400, "message": "invalid client"}, "invalid_client"),
            (429, {}, "oauth_rate_limited"), (503, {}, "oauth_server_error"),
            (200, [], "oauth_invalid_response"),
            (400, {"error": "access_denied"}, "access_denied"),
        ):
            with self.subTest(status=status, body=body):
                with self.assertRaises(OAuthException) as caught:
                    await read_oauth(SimpleNamespace(status=status, json=AsyncMock(return_value=body)), "device")
                self.assertEqual(caught.exception.code, expected)
        for body in ({}, {"device_code": "secret", "user_code": "ABC", "interval": 5,
                          "expires_in": 1800, "verification_uri": "https://www.twitch.tv.evil/activate"}):
            with self.assertRaises(OAuthException):
                validate_device(body)
        with self.assertRaises(OAuthException):
            await read_oauth(SimpleNamespace(status=200, json=AsyncMock(side_effect=ValueError("HTML"))), "device")

    async def test_pending_slow_down_success_and_expiration(self):
        device = {"device_code": "secret", "user_code": "ABC", "interval": 1,
                  "expires_in": 1800, "verification_uri": "https://www.twitch.tv/activate"}
        async def run(token_responses):
            responses = [(200, device)] + token_responses
            @asynccontextmanager
            async def request(*args, **kwargs):
                status, data = responses.pop(0)
                yield SimpleNamespace(status=status, json=AsyncMock(return_value=data))
            login = SimpleNamespace(ask_enter_code=AsyncMock())
            async def unless(coro):
                return await coro
            client = SimpleNamespace(_client_type=constants.ClientType.SMARTBOX, request=request,
                                     gui=SimpleNamespace(login=login, connection=SimpleNamespace(update=lambda *a: None),
                                                         coro_unless_closed=unless))
            auth = twitch._AuthState(client)
            auth.device_id = "test"
            with patch.object(twitch.asyncio, "sleep", new=AsyncMock()) as sleep:
                result = await auth._oauth_login()
                self.assertEqual([c.args[0] for c in sleep.call_args_list], [1, 1, 6])
            login.ask_enter_code.assert_awaited_once()
            return result
        token = await run([(400, {"error": "authorization_pending"}),
                           (400, {"error": "slow_down"}), (200, {"access_token": "test-token"})])
        self.assertEqual(token, "test-token")
        # A saved legacy session may be replaced once, while a newly granted
        # token rejected by /validate requires user action, not another loop.
        import aiohttp
        import tempfile
        from unittest.mock import Mock
        async def identity_check(saved, statuses):
            jar = aiohttp.CookieJar()
            if saved:
                jar.update_cookies({"auth-token": "old-test-token"}, constants.ClientType.WEB.CLIENT_URL)
            @asynccontextmanager
            async def request(*args, **kwargs):
                status = statuses.pop(0)
                yield SimpleNamespace(status=status, json=AsyncMock(return_value={
                    "client_id": constants.ClientType.SMARTBOX.CLIENT_ID, "user_id": "1"}))
            client = SimpleNamespace(_client_type=constants.ClientType.SMARTBOX, request=request,
                                     get_session=AsyncMock(return_value=SimpleNamespace(cookie_jar=jar)),
                                     gui=SimpleNamespace(login=SimpleNamespace(phase=Mock(), update=Mock()),
                                                         help=SimpleNamespace(_invalidate_button=SimpleNamespace(config=Mock()))))
            auth = twitch._AuthState(client)
            auth.device_id = "test-device"
            auth._oauth_login = AsyncMock(return_value="new-test-token")
            with tempfile.TemporaryDirectory() as temporary, patch.object(twitch, "COOKIES_PATH", Path(temporary) / "cookies.jar"):
                if saved:
                    await auth.validate()
                    self.assertEqual(auth.user_id, 1)
                else:
                    with self.assertRaises(OAuthException) as rejected:
                        await auth.validate()
                    self.assertEqual(rejected.exception.code, "invalid_token")
            auth._oauth_login.assert_awaited_once()
        await identity_check(True, [401, 200])
        await identity_check(False, [401])
        # A fresh grant must match its requested Smartbox client; only restored
        # legacy sessions may select their validated Android/Web client.
        for saved_client in (None, constants.ClientType.SMARTBOX,
                             constants.ClientType.WEB, constants.ClientType.ANDROID_APP):
            with tempfile.TemporaryDirectory() as temporary:
                cookie_path = Path(temporary) / "cookies.jar"
                jar = aiohttp.CookieJar()
                if saved_client:
                    jar.update_cookies({"auth-token": "fixture-token"}, saved_client.CLIENT_URL)
                    jar.save(cookie_path)
                identity = saved_client or constants.ClientType.SMARTBOX
                @asynccontextmanager
                async def request(*args, **kw):
                    yield SimpleNamespace(status=200, json=AsyncMock(return_value={
                        "user_id": "2", "client_id": identity.CLIENT_ID}))
                client = SimpleNamespace(_client_type=constants.ClientType.SMARTBOX, request=request,
                    get_session=AsyncMock(return_value=SimpleNamespace(cookie_jar=jar)),
                    gui=SimpleNamespace(login=SimpleNamespace(phase=Mock(), update=Mock()),
                        help=SimpleNamespace(_invalidate_button=SimpleNamespace(config=Mock()))))
                auth = twitch._AuthState(client)
                auth.device_id = "fixture-device"
                auth._oauth_login = AsyncMock(return_value="fixture-token")
                with patch.object(twitch, "COOKIES_PATH", cookie_path):
                    await auth.validate()
                    self.assertEqual(auth.user_id, 2)
                    self.assertEqual(auth.headers(gql=True)["Client-Id"], identity.CLIENT_ID)
                    auth.clear()
                    await auth.validate()  # restore the jar saved by the first login
                    self.assertEqual(auth.user_id, 2)
                self.assertTrue(cookie_path.is_file())
        # A rejected new grant must never be adopted as a legacy session.
        with tempfile.TemporaryDirectory() as temporary:
            jar = aiohttp.CookieJar()
            client.get_session = AsyncMock(return_value=SimpleNamespace(cookie_jar=jar))
            client._client_type = constants.ClientType.SMARTBOX
            identity = constants.ClientType.WEB
            auth = twitch._AuthState(client)
            auth.device_id = "fixture-device"
            auth._oauth_login = AsyncMock(return_value="fixture-token")
            with patch.object(twitch, "COOKIES_PATH", Path(temporary) / "cookies.jar"):
                with self.assertRaises(OAuthException) as rejected:
                    await auth.validate()
                self.assertEqual(rejected.exception.code, "invalid_client")
                self.assertFalse(auth._logged_in.is_set())
                self.assertFalse(hasattr(auth, "access_token"))

        responses = [429, 503, 200]
        @asynccontextmanager
        async def temporary_request(*a, **kw):
            yield SimpleNamespace(status=responses.pop(0), json=AsyncMock(return_value={"access_token": "fixture-token"}))
        client.request = temporary_request
        async def unless(coro): return await coro
        client.gui.coro_unless_closed = unless
        with patch.object(twitch.asyncio, "sleep", new=AsyncMock()) as sleep:
            result = await auth._oauth_request("token", "POST", "https://id.twitch.tv/oauth2/token")
            self.assertEqual(result["access_token"], "fixture-token")
            self.assertEqual([c.args[0] for c in sleep.call_args_list], [5, 10])
        responses[:] = [429, 429, 429]
        with patch.object(twitch.asyncio, "sleep", new=AsyncMock()):
            with self.assertRaises(OAuthException):
                await auth._oauth_request("token", "POST", "https://id.twitch.tv/oauth2/token")
        responses[:] = [429]
        async def cancel(coro):
            coro.close()
            raise asyncio.CancelledError()
        client.gui.coro_unless_closed = cancel
        with self.assertRaises(asyncio.CancelledError):
            await auth._oauth_request("token", "POST", "https://id.twitch.tv/oauth2/token")

        from worker import TwitchWorker
        worker = object.__new__(TwitchWorker)
        drop = SimpleNamespace(id="new-phase", name="reward", campaign=SimpleNamespace(name="campaign"),
                               real_current_minutes=10, extra_current_minutes=2, required_minutes=60)
        worker._client = SimpleNamespace(gui=SimpleNamespace(progress=SimpleNamespace(drop=drop)))
        worker.running = True
        result = worker._current_drop()
        self.assertEqual(result["currentMinutes"], 10)
        self.assertEqual(result["estimatedExtraMinutes"], 2)
        self.assertAlmostEqual(result["progress"], 10 / 60)

    async def test_timeout_releases_response_and_next_attempt_can_run(self):
        from Shared.protocol import redact
        self.assertNotIn("fixture-signature", redact("https://fixture.invalid/?sig=fixture-signature&signature=fixture-signature"))
        class Backoff:
            steps = 0
            def __init__(self, **kw): pass
            def __iter__(self): return self
            def __next__(self):
                self.steps += 1
                return 0
        client = object.__new__(twitch.Twitch)
        client.settings = SimpleNamespace(proxy=None)
        from unittest.mock import Mock
        client.print = Mock()
        client.gui = SimpleNamespace(close_requested=False, coro_unless_closed=AsyncMock(side_effect=lambda coro: coro),
                                     wait_until_closed=AsyncMock(), connection=SimpleNamespace(update=lambda *a: None))
        async def unless(coro): return await coro
        client.gui.coro_unless_closed = unless
        session = SimpleNamespace(timeout=SimpleNamespace(total=15), request=AsyncMock(side_effect=TimeoutError))
        client.get_session = AsyncMock(return_value=session)
        with patch.object(twitch, "ExponentialBackoff", Backoff):
            with self.assertRaises(TimeoutError):
                async with client.request("GET", "https://id.twitch.tv/oauth2/validate?sig=fixture-signature&token=fixture-playback-token"):
                    self.fail("timeout cannot succeed")
        self.assertEqual(session.request.await_count, 3)
        self.assertNotIn("fixture-signature", str(client.print.call_args_list))
        self.assertNotIn("fixture-playback-token", str(client.print.call_args_list))
        response = SimpleNamespace(status=200, read=AsyncMock(), release=lambda: None)
        session.request = AsyncMock(return_value=response)
        async with client.request("GET", "https://id.twitch.tv/oauth2/validate") as result:
            self.assertIs(result, response)
        import aiohttp
        from yarl import URL
        proxy_error = aiohttp.ClientHttpProxyError(SimpleNamespace(real_url=URL("https://id.twitch.tv/oauth2/validate")),
                                                  (), status=407, message="proxy authentication required")
        client.settings = SimpleNamespace(proxy="http://127.0.0.1:9", _cloudlight_fallback_direct=True)
        session.request = AsyncMock(side_effect=[proxy_error, response])
        async with client.request("GET", "https://id.twitch.tv/oauth2/validate") as result:
            self.assertIs(result, response)
        self.assertIn("proxy", session.request.call_args_list[0].kwargs)
        self.assertNotIn("proxy", session.request.call_args_list[1].kwargs)


if __name__ == "__main__":
    unittest.main()
