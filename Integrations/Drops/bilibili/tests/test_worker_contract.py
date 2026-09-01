from __future__ import annotations

import asyncio
import json
import os
import socket
import subprocess
import sys
import tempfile
import threading
import time
import unittest
from collections import deque
from pathlib import Path
from types import SimpleNamespace
from unittest.mock import patch

import httpx


BILIBILI_DIR = Path(__file__).resolve().parents[1]
DROPS_DIR = BILIBILI_DIR.parent
for path in (str(DROPS_DIR), str(BILIBILI_DIR)):
    if path not in sys.path:
        sys.path.insert(0, path)

import worker as bilibili_worker  # noqa: E402
from direct_network import (  # noqa: E402
    BilibiliNetworkPolicy,
    PROXY_ENVIRONMENT_NAMES,
    build_network_policy,
    create_http_client,
)
from Shared.protocol import redact  # noqa: E402
from bilibili_drops_miner.client import BilibiliClient  # noqa: E402
from bilibili_drops_miner.client_parts.models import (  # noqa: E402
    MissionRewardClaimResult,
    TaskProgress,
    TaskCheckpointProgress,
)
from bilibili_drops_miner.client_parts.profile import parse_self_info  # noqa: E402
from bilibili_drops_miner.client_parts.qr_login import (  # noqa: E402
    QrLoginApi,
    QrLoginChallenge,
    QrLoginStatus,
    build_login_cookie_string,
    parse_generate_payload,
    parse_poll_payload,
)
from bilibili_drops_miner.client_parts.rewards import (  # noqa: E402
    build_reward_receive_body,
    parse_mission_reward_info,
    reward_claim_result_from_payload,
)
from bilibili_drops_miner.client_parts.task_discovery import fetch_live_task_groups  # noqa: E402
from bilibili_drops_miner.client_parts.tasks import parse_task_progress_payload  # noqa: E402
from bilibili_drops_miner.client_parts.http import request_with_network_fallback  # noqa: E402


class BilibiliWorkerContractTests(unittest.TestCase):
    @staticmethod
    def _close_worker(worker: bilibili_worker.BilibiliWorker) -> None:
        worker._close_qr()
        worker._pool.stop(timeout=0.5)
        for logger_name in (
            "cloudlight.drops",
            "bilibili_drops_miner",
            "bilibili_drops_miner.client_parts",
        ):
            logger = __import__("logging").getLogger(logger_name)
            for handler in list(logger.handlers):
                logger.removeHandler(handler)
                if logger_name == "cloudlight.drops":
                    handler.close()

    def test_direct_environment_scrub_removes_upper_and_lower_proxy_names(self) -> None:
        names = PROXY_ENVIRONMENT_NAMES
        supplied = {name: "http://127.0.0.1:9" for name in names}
        removed = bilibili_worker.scrub_proxy_environment(supplied)
        self.assertEqual(set(removed), set(names))
        self.assertFalse(bilibili_worker.proxy_environment_is_clean(supplied))

        previous = {name: os.environ.get(name) for name in names}
        previous_no_proxy = {name: os.environ.get(name) for name in ("NO_PROXY", "no_proxy")}
        try:
            for name in names:
                os.environ[name] = "http://127.0.0.1:9"
            removed = bilibili_worker.scrub_proxy_environment()
            self.assertEqual(set(removed), set(names))
            self.assertTrue(bilibili_worker.proxy_environment_is_clean())
            self.assertEqual(os.environ["NO_PROXY"], "*")
            self.assertEqual(os.environ["no_proxy"], "*")
        finally:
            for name, value in previous.items():
                if value is None:
                    os.environ.pop(name, None)
                else:
                    os.environ[name] = value
            for name, value in previous_no_proxy.items():
                if value is None:
                    os.environ.pop(name, None)
                else:
                    os.environ[name] = value

    def test_direct_clients_and_static_discovery_never_trust_environment_proxy(self) -> None:
        factory_source = (BILIBILI_DIR / "direct_network.py").read_text(encoding="utf-8")
        self.assertIn('"trust_env": False', factory_source)
        self.assertIn('"proxy": None', factory_source)
        for path in (
            BILIBILI_DIR / "vendor" / "bilibili_drops_miner" / "client_parts" / "core.py",
            BILIBILI_DIR / "vendor" / "bilibili_drops_miner" / "client_parts" / "qr_login.py",
            BILIBILI_DIR / "vendor" / "bilibili_drops_miner" / "client_parts" / "task_discovery.py",
        ):
            self.assertIn("create_", path.read_text(encoding="utf-8"), str(path))

        client = BilibiliClient("SESSDATA=s; bili_jct=j; DedeUserID=1")
        try:
            self.assertFalse(client._http._trust_env)
        finally:
            asyncio.run(client.close())

        qr = QrLoginApi()
        try:
            self.assertFalse(qr._client._trust_env)
        finally:
            qr.close()

        html = "window.__initialState = {\"EraTasklistPc\":[],\"EvaTabs.Panel\":[],\"EvaTabs\":[]};"
        seen_urls: list[str] = []

        def handler(request: httpx.Request) -> httpx.Response:
            seen_urls.append(str(request.url))
            return httpx.Response(200, text=html, request=request)

        groups = fetch_live_task_groups(123, transport=httpx.MockTransport(handler))
        self.assertEqual(groups, [])
        self.assertEqual(seen_urls, ["https://live.bilibili.com/123"])

    def test_explicit_proxy_and_network_fallback_never_use_environment_proxy(self) -> None:
        class ProxyHandler(__import__("http.server", fromlist=["BaseHTTPRequestHandler"]).BaseHTTPRequestHandler):
            def do_GET(self) -> None:  # noqa: N802
                proxy_requests.append(self.path)
                body = b"proxy-ok"
                self.send_response(200)
                self.send_header("Content-Length", str(len(body)))
                self.end_headers()
                self.wfile.write(body)

            def log_message(self, *_args: object) -> None:
                return

        from http.server import ThreadingHTTPServer

        proxy_requests: list[str] = []
        proxy_server = ThreadingHTTPServer(("127.0.0.1", 0), ProxyHandler)
        proxy_thread = threading.Thread(target=proxy_server.serve_forever, daemon=True)
        proxy_thread.start()
        previous_http_proxy = os.environ.get("HTTP_PROXY")
        os.environ["HTTP_PROXY"] = "http://127.0.0.1:9"
        proxy_client = None
        fallback_client = None
        direct_server = None
        try:
            policy = build_network_policy(
                use_proxy=True,
                proxy_url=f"http://127.0.0.1:{proxy_server.server_port}",
                fallback_direct=False,
            )
            proxy_client = create_http_client(policy, timeout=3.0)
            response = proxy_client.get("http://bilibili.test/explicit")
            self.assertEqual(response.text, "proxy-ok")
            self.assertFalse(proxy_client._trust_env)
            self.assertTrue(proxy_requests and proxy_requests[-1].startswith("http://bilibili.test/"))

            class DirectHandler(__import__("http.server", fromlist=["BaseHTTPRequestHandler"]).BaseHTTPRequestHandler):
                def do_GET(self) -> None:  # noqa: N802
                    direct_requests.append(self.path)
                    body = b"direct-ok"
                    self.send_response(200)
                    self.send_header("Content-Length", str(len(body)))
                    self.end_headers()
                    self.wfile.write(body)

                def log_message(self, *_args: object) -> None:
                    return

            direct_requests: list[str] = []
            direct_server = ThreadingHTTPServer(("127.0.0.1", 0), DirectHandler)
            direct_thread = threading.Thread(target=direct_server.serve_forever, daemon=True)
            direct_thread.start()
            with socket.socket() as reserved:
                reserved.bind(("127.0.0.1", 0))
                unavailable_port = reserved.getsockname()[1]
            fallback_policy = BilibiliNetworkPolicy(
                use_proxy=True,
                proxy_url=f"http://127.0.0.1:{unavailable_port}",
                fallback_direct=True,
            )
            fallback_client = BilibiliClient(
                "SESSDATA=s; bili_jct=j; DedeUserID=1",
                network_policy=fallback_policy,
            )
            fallback_url = f"http://127.0.0.1:{direct_server.server_port}/fallback"
            response = asyncio.run(fallback_client._request_with_transient_retry(
                lambda: fallback_client._http.get(fallback_url),
                method="GET",
                url=fallback_url,
                fallback_request_coro=lambda: fallback_client._fallback_http.get(fallback_url),
            ))
            self.assertEqual(response.text, "direct-ok")
            self.assertTrue(direct_requests)
            self.assertEqual(fallback_client.network_mode, "DIRECT")
            self.assertTrue(fallback_client.network_fallback_active)
            asyncio.run(fallback_client.close())
            fallback_client = None

            no_fallback = BilibiliClient(
                "SESSDATA=s; bili_jct=j; DedeUserID=1",
                network_policy=BilibiliNetworkPolicy(
                    use_proxy=True,
                    proxy_url=f"http://127.0.0.1:{unavailable_port}",
                    fallback_direct=False,
                ),
            )
            try:
                with self.assertRaises((httpx.ConnectError, httpx.ConnectTimeout, httpx.RemoteProtocolError)):
                    asyncio.run(no_fallback._request_with_transient_retry(
                        lambda: no_fallback._http.get(fallback_url),
                        method="GET",
                        url=fallback_url,
                    ))
            finally:
                asyncio.run(no_fallback.close())

            for status_code in (401, 403, 412, 429):
                fallback_calls = 0

                async def business_error(status: int = status_code) -> httpx.Response:
                    return httpx.Response(status)

                async def should_not_be_called() -> httpx.Response:
                    nonlocal fallback_calls
                    fallback_calls += 1
                    return httpx.Response(200)

                business_response = asyncio.run(request_with_network_fallback(
                    business_error,
                    should_not_be_called,
                    method="GET",
                    url="https://bilibili.test/business",
                    logger=__import__("logging").getLogger(__name__),
                ))
                self.assertEqual(business_response.status_code, status_code)
                self.assertEqual(fallback_calls, 0)
        finally:
            if proxy_client is not None:
                proxy_client.close()
            if fallback_client is not None:
                asyncio.run(fallback_client.close())
            proxy_server.shutdown()
            proxy_server.server_close()
            if direct_server is not None:
                direct_server.shutdown()
                direct_server.server_close()
            if previous_http_proxy is None:
                os.environ.pop("HTTP_PROXY", None)
            else:
                os.environ["HTTP_PROXY"] = previous_http_proxy

    def test_room_url_and_session_validation(self) -> None:
        self.assertEqual(bilibili_worker.parse_room_reference("123"), 123)
        self.assertEqual(
            bilibili_worker.parse_room_reference("https://live.bilibili.com/blanc/456?from=search"),
            456,
        )
        self.assertEqual(bilibili_worker.parse_room_reference("live.bilibili.com/789"), 789)
        with self.assertRaises(ValueError):
            bilibili_worker.parse_room_reference("https://example.com/123")
        with self.assertRaises(ValueError):
            bilibili_worker.positive_int(129, "sessions", maximum=128)
        self.assertEqual(bilibili_worker.positive_int("80", "sessions", maximum=128), 80)

    def test_discover_after_adding_room_uses_internal_room_id_schema(self) -> None:
        with tempfile.TemporaryDirectory() as temporary:
            root = Path(temporary)
            worker = bilibili_worker.BilibiliWorker(root / "data", root / "worker.log")
            try:
                added = worker.add_room({
                    "roomId": "https://live.bilibili.com/101",
                    "name": "发现测试房间",
                })
                self.assertEqual(added["rooms"][0]["id"], 101)
                with worker._state_lock:
                    worker._state["rooms"][0]["liveStatus"] = 0
                    worker._state["rooms"][0]["lastError"] = "上一次发现失败"

                async def fetch_statuses(room_ids: list[int]) -> dict[int, int]:
                    self.assertEqual(room_ids, [101])
                    return {101: 1}

                worker._fetch_room_statuses = fetch_statuses
                with patch.object(bilibili_worker, "fetch_live_task_groups", return_value=[]):
                    result = worker.discover({})

                self.assertEqual(result["activities"], [])
                self.assertEqual(result["taskIds"], [])
                self.assertIn("未自动发现活动", result["message"])
                room = result["rooms"][0]
                self.assertEqual(room["id"], 101)
                self.assertEqual(room["liveStatus"], 1)
                self.assertEqual(room["lastError"], "")
                self.assertNotIn("roomId", worker._state["rooms"][0])

                log = worker.log_file.read_text(encoding="utf-8")
                self.assertIn("活动发现：rooms=1 groups=0 tasks=0", log)
                self.assertNotIn("discovery_failed", log)
                self.assertNotIn("ERROR", log)
            finally:
                self._close_worker(worker)

    def test_discover_activity_uses_room_id_only_in_activity_wire_dto(self) -> None:
        with tempfile.TemporaryDirectory() as temporary:
            root = Path(temporary)
            worker = bilibili_worker.BilibiliWorker(root / "data", root / "worker.log")
            try:
                worker.add_room({"roomId": "https://live.bilibili.com/101", "name": "活动测试房间"})

                async def fetch_statuses(room_ids: list[int]) -> dict[int, int]:
                    self.assertEqual(room_ids, [101])
                    return {101: 2}

                worker._fetch_room_statuses = fetch_statuses
                groups = [{
                    "task_ids": ["task-a", "task-b"],
                    "label": "测试活动",
                    "active": True,
                }]
                with patch.object(bilibili_worker, "fetch_live_task_groups", return_value=groups):
                    result = worker.discover({})

                self.assertEqual(result["taskIds"], ["task-a", "task-b"])
                self.assertEqual(len(result["activities"]), 1)
                activity = result["activities"][0]
                self.assertEqual(activity["roomId"], 101)
                self.assertEqual(activity["taskIds"], ["task-a", "task-b"])
                self.assertEqual(activity["name"], "测试活动")
                self.assertTrue(activity["active"])
                self.assertEqual(worker._state["rooms"][0]["id"], 101)
                self.assertNotIn("roomId", worker._state["rooms"][0])
                self.assertEqual(result["rooms"][0]["liveStatus"], 2)
                self.assertEqual(result["rooms"][0]["lastError"], "")

                log = worker.log_file.read_text(encoding="utf-8")
                self.assertIn("活动发现：rooms=1 groups=1 tasks=2", log)
                self.assertNotIn("discovery_failed", log)
            finally:
                self._close_worker(worker)

    def test_refresh_after_adding_room_does_not_report_discovery_failed(self) -> None:
        with tempfile.TemporaryDirectory() as temporary:
            root = Path(temporary)
            worker = bilibili_worker.BilibiliWorker(root / "data", root / "worker.log")
            try:
                worker.add_room({"roomId": "https://live.bilibili.com/101", "name": "刷新测试房间"})

                async def fetch_statuses(room_ids: list[int]) -> dict[int, int]:
                    self.assertEqual(room_ids, [101])
                    return {101: 1}

                worker._fetch_room_statuses = fetch_statuses
                with patch.object(bilibili_worker, "fetch_live_task_groups", return_value=[]):
                    result = worker.refresh({})

                self.assertEqual(result["activities"], [])
                self.assertEqual(result["taskIds"], [])
                self.assertEqual(result["rooms"][0]["id"], 101)
                self.assertEqual(result["rooms"][0]["liveStatus"], 1)
                self.assertEqual(result["rooms"][0]["lastError"], "")
                self.assertEqual(result["lastError"], "")

                log = worker.log_file.read_text(encoding="utf-8")
                self.assertIn("活动发现：rooms=1 groups=0 tasks=0", log)
                self.assertNotIn("discovery_failed", log)
                self.assertNotIn("后台请求失败：'roomId'", log)
            finally:
                self._close_worker(worker)

    def test_qr_status_account_and_safe_cookie_parsing(self) -> None:
        challenge = parse_generate_payload({
            "code": 0,
            "data": {"url": "https://passport.bilibili.com/qr/abc", "qrcode_key": "key"},
        })
        self.assertEqual(challenge, QrLoginChallenge("https://passport.bilibili.com/qr/abc", "key"))
        self.assertEqual(parse_poll_payload({"code": 0, "data": {"code": 86101}}), QrLoginStatus.UNSCANNED)
        self.assertEqual(parse_poll_payload({"code": 0, "data": {"code": 86090}}), QrLoginStatus.CONFIRMED_PENDING)
        self.assertEqual(parse_poll_payload({"code": 0, "data": {"code": 86038}}), QrLoginStatus.EXPIRED)
        self.assertEqual(parse_poll_payload({"code": 0, "data": {"code": 0}}), QrLoginStatus.SUCCESS)
        self.assertEqual(bilibili_worker.classify_qr_status(QrLoginStatus.UNSCANNED)[0], "waiting_scan")
        self.assertEqual(bilibili_worker.classify_qr_status(QrLoginStatus.CONFIRMED_PENDING)[0], "scanned_pending")
        self.assertEqual(parse_self_info({"code": 0, "data": {"isLogin": True, "mid": 42, "uname": "Cloudlight"}}), (42, "Cloudlight"))
        self.assertEqual(parse_self_info({"code": 0, "data": {"isLogin": False}}), (None, ""))

        client_cookies = httpx.Cookies()
        client_cookies.set("SESSDATA", "old")
        client_cookies.set("bili_jct", "csrf")
        client_cookies.set("DedeUserID", "42")
        client_cookies.set("unrelated", "must-not-leak")
        response_cookies = httpx.Cookies()
        response_cookies.set("SESSDATA", "new")
        safe_cookie = build_login_cookie_string(response_cookies, client_cookies)
        self.assertIn("SESSDATA=new", safe_cookie)
        self.assertIn("bili_jct=csrf", safe_cookie)
        self.assertIn("DedeUserID=42", safe_cookie)
        self.assertNotIn("unrelated", safe_cookie)

    def test_qr_api_uses_mocked_direct_transport_and_returns_cookie(self) -> None:
        calls = deque([
            (200, {"code": 0, "data": {"url": "https://passport.bilibili.com/qr/abc", "qrcode_key": "key"}}, {}),
            (200, {"code": 0, "data": {"code": 86101}}, {}),
            (200, {"code": 0, "data": {"code": 86090}}, {}),
            (200, {"code": 0, "data": {"code": 0}}, [("set-cookie", "SESSDATA=new; Path=/")]),
        ])

        def handler(request: httpx.Request) -> httpx.Response:
            status, payload, headers = calls.popleft()
            return httpx.Response(status, json=payload, headers=headers, request=request)

        client = httpx.Client(transport=httpx.MockTransport(handler), trust_env=False)
        client.cookies.set("bili_jct", "csrf")
        client.cookies.set("DedeUserID", "42")
        client.cookies.set("unrelated", "secret")
        with QrLoginApi(client) as api:
            self.assertEqual(api.generate().key, "key")
            self.assertEqual(api.poll("key").status, QrLoginStatus.UNSCANNED)
            self.assertEqual(api.poll("key").status, QrLoginStatus.CONFIRMED_PENDING)
            # The required SESSDATA is supplied by the successful response.
            success = api.poll("key")
            self.assertEqual(success.status, QrLoginStatus.SUCCESS)
            self.assertIn("SESSDATA=new", success.cookie)
            self.assertNotIn("unrelated", success.cookie)

    def test_manual_credential_replacement_is_transactional(self) -> None:
        with tempfile.TemporaryDirectory() as temporary:
            root = Path(temporary)
            worker = bilibili_worker.BilibiliWorker(root / "data", root / "worker.log")
            try:
                worker._cookie = "SESSDATA=old; bili_jct=old-csrf; DedeUserID=7"
                worker._uid = 7
                worker._uname = "old-user"

                async def invalid_probe() -> tuple[None, str]:
                    return None, ""

                worker.probe_account = invalid_probe
                with patch.object(bilibili_worker, "write_protected") as write:
                    with self.assertRaises(ValueError):
                        worker.set_credentials({"cookie": "SESSDATA=new; bili_jct=new-csrf; DedeUserID=8"})
                    write.assert_not_called()
                self.assertEqual(worker.cookie, "SESSDATA=old; bili_jct=old-csrf; DedeUserID=7")
                self.assertEqual((worker.uid, worker._uname), (7, "old-user"))

                async def valid_probe() -> tuple[int, str]:
                    return 8, "new-user"

                worker.probe_account = valid_probe
                with patch.object(bilibili_worker, "write_protected") as write:
                    result = worker.set_credentials({"cookie": "SESSDATA=new; bili_jct=new-csrf; DedeUserID=8"})
                    write.assert_called_once_with(
                        worker._credential_path,
                        "SESSDATA=new; bili_jct=new-csrf; DedeUserID=8",
                    )
                self.assertEqual(result["account"]["uid"], 8)
                self.assertEqual(worker.uid, 8)
            finally:
                self._close_worker(worker)

    def test_static_task_discovery_and_official_multiple_task_progress(self) -> None:
        html = (
            "window.__initialState = "
            + json.dumps({
                "EraTasklistPc": [
                    {"tasklist": [{"taskId": "task-a"}]},
                    {"tasklist": [{"taskId": "task-b"}]},
                ],
                "EvaTabs.Panel": [
                    {"id": "p1", "tabItem": {"tabItemProps": {"textContent": {"content": "官方"}}}},
                    {"id": "p2", "tabItem": {"tabItemProps": {"textContent": {"content": "合作"}}}},
                ],
                "EvaTabs": [{"activatedTabPanelId": "p1"}],
            }, ensure_ascii=False)
            + ";"
        )
        groups = bilibili_worker.fetch_live_task_groups
        with httpx.Client(transport=httpx.MockTransport(
            lambda request: httpx.Response(200, text=html, request=request),
        ), trust_env=False) as client:
            response = client.get("https://live.bilibili.com/123")
            response.raise_for_status()
        # The upstream parser is the source of the product's static discovery.
        parsed_groups = __import__(
            "bilibili_drops_miner.utils", fromlist=["extract_bili_live_task_groups"],
        ).extract_bili_live_task_groups(response.text)
        self.assertEqual([group["task_ids"] for group in parsed_groups], [["task-a"], ["task-b"]])
        self.assertTrue(parsed_groups[0]["active"])

        progresses = parse_task_progress_payload({
            "code": 0,
            "data": {"list": [
                {"task_id": "task-a", "task_name": "观看 300 分钟", "status": 0,
                 "indicators": [{"type": "watch_time", "cur_value": 204, "limit": 300}],
                 "check_points": [{"sid": "reward-a", "alias": "奖励 A", "status": 0,
                                    "cur_value": 60, "limit": 60, "award_name": "宝箱"}]},
                {"task_id": "task-b", "task_name": "观看 120 分钟", "status": 6,
                 "indicators": [{"type": "watch_time", "cur_value": 120, "limit": 120}]},
            ]},
        })
        self.assertEqual(len(progresses), 2)
        self.assertEqual((progresses[0].cur_value, progresses[0].limit_value), (204, 300))
        self.assertTrue(progresses[1].is_completed)
        record = bilibili_worker.task_record(progresses[0], set())
        self.assertEqual(record["percent"], 68.0)
        self.assertTrue(record["checkpoints"][0]["claimable"])
        self.assertEqual(record["checkpoints"][0]["reward"], "宝箱")

    def test_reward_claim_success_failure_and_csrf_body(self) -> None:
        info = parse_mission_reward_info({
            "code": 0,
            "data": {"task_id": "task-a", "task_name": "观看任务", "status": 0,
                      "act_id": "activity", "act_name": "活动", "reward_info": {"award_name": "奖励"}},
        }, normalized_id="task-a")
        body = build_reward_receive_body(info, csrf="csrf-value")
        self.assertEqual(body["task_id"], "task-a")
        self.assertEqual(body["csrf_token"], "csrf-value")
        success = reward_claim_result_from_payload(info, {"code": 0, "message": "领取成功"})
        failure = reward_claim_result_from_payload(info, {"code": -412, "message": "暂不可领取"})
        self.assertTrue(success.success)
        self.assertFalse(failure.success)
        self.assertEqual(failure.status, 0)

    def test_multi_room_multi_session_pool_scales_and_aggregates(self) -> None:
        changes: list[dict[str, object]] = []
        pool = bilibili_worker.BilibiliSessionPool(
            lambda: "",
            lambda: 0.01,
            lambda: True,
            lambda: 42,
            lambda: changes.append(pool.snapshot()),
        )
        # Keep this structural test network-free while retaining the real pool
        # reconciliation, keying, stagger metadata, and graceful stop paths.
        pool._thread_entry = lambda record, uid: record.stop_event.wait()
        try:
            pool.reconcile([101, 202], 3, 42)
            snapshot = pool.snapshot()
            self.assertEqual(snapshot["configuredSessions"], 6)
            self.assertEqual({item["roomId"] for item in snapshot["sessions"]}, {101, 202})
            self.assertEqual({item["sessionNo"] for item in snapshot["sessions"]}, {1, 2, 3})
            self.assertEqual(
                [pool._records[f"r101-s{i}"].start_delay for i in (1, 2, 3)],
                [0.0, 1.0, 2.0],
            )
            pool.reconcile([101], 2, 42)
            snapshot = pool.snapshot()
            self.assertEqual(snapshot["configuredSessions"], 2)
            self.assertTrue(all(item["roomId"] == 101 for item in snapshot["sessions"]))

            with pool._lock:
                records = list(pool._records.values())
            pool._set_state(records[0], "active")
            pool._set_state(records[1], "retrying", "429")
            snapshot = pool.snapshot()
            self.assertEqual(snapshot["activeSessions"], 1)
            self.assertEqual(snapshot["retryingSessions"], 1)
        finally:
            pool.stop(timeout=2)
        self.assertEqual(pool.snapshot()["configuredSessions"], 0)
        self.assertGreater(len(changes), 0)

    def test_session_reconnect_and_partial_failure_state(self) -> None:
        pool = bilibili_worker.BilibiliSessionPool(
            lambda: "",
            lambda: 0.001,
            lambda: True,
            lambda: 42,
            lambda: None,
        )
        record = bilibili_worker.SessionRecord("r101-s1", 101, 1, 0)
        with pool._lock:
            pool._records[record.key] = record
        attempts = 0

        async def fake_run(current: bilibili_worker.SessionRecord, uid: int) -> None:
            nonlocal attempts
            attempts += 1
            if attempts == 1:
                raise RuntimeError("simulated disconnect")
            current.stop_event.set()

        pool._run_session = fake_run
        record.thread = threading.Thread(target=pool._thread_entry, args=(record, 42), daemon=True)
        record.thread.start()
        record.thread.join(timeout=2)
        self.assertFalse(record.thread.is_alive())
        self.assertEqual(attempts, 2)
        self.assertEqual(record.reconnect_count, 1)
        self.assertEqual(record.state, "stopped")
        pool.stop(timeout=1)

    def test_session_reconnect_setting_can_disable_retry(self) -> None:
        pool = bilibili_worker.BilibiliSessionPool(
            lambda: "",
            lambda: 0.001,
            lambda: False,
            lambda: 42,
            lambda: None,
        )
        record = bilibili_worker.SessionRecord("r101-s1", 101, 1, 0)
        with pool._lock:
            pool._records[record.key] = record
        attempts = 0

        async def fake_run(current: bilibili_worker.SessionRecord, uid: int) -> None:
            nonlocal attempts
            attempts += 1
            raise RuntimeError("simulated disconnect")

        pool._run_session = fake_run
        record.thread = threading.Thread(target=pool._thread_entry, args=(record, 42), daemon=True)
        record.thread.start()
        record.thread.join(timeout=2)
        self.assertFalse(record.thread.is_alive())
        self.assertEqual(attempts, 1)
        self.assertEqual(record.reconnect_count, 0)
        self.assertEqual(record.state, "stopped")
        pool.stop(timeout=1)

    def test_worker_start_stop_shutdown_with_mocked_account_and_sessions(self) -> None:
        class FakePool:
            def __init__(self) -> None:
                self.revived = 0
                self.reconciled: tuple[list[int], int, int] | None = None
                self.stopped = 0

            def snapshot(self) -> dict[str, object]:
                return {"configuredSessions": 0, "activeSessions": 0, "connectingSessions": 0,
                        "retryingSessions": 0, "failedSessions": 0, "sessions": []}

            def revive(self) -> None:
                self.revived += 1

            def reconcile(self, rooms: list[int], sessions: int, uid: int) -> None:
                self.reconciled = (rooms, sessions, uid)

            def stop(self, timeout: float = 12.0) -> None:
                self.stopped += 1

        class FakeLoop:
            def __init__(self) -> None:
                self.started = 0
                self.stopped = 0

            def start(self) -> None:
                self.started += 1

            def stop(self) -> None:
                self.stopped += 1

        with tempfile.TemporaryDirectory() as temporary:
            root = Path(temporary)
            worker = bilibili_worker.BilibiliWorker(root / "data", root / "worker.log")
            try:
                pool = FakePool()
                progress = FakeLoop()
                login = FakeLoop()
                worker._pool = pool
                worker._progress_poller = progress
                worker._login_watchdog = login
                worker._cookie = "SESSDATA=s; bili_jct=j; DedeUserID=42"
                worker._uid = 42
                worker._uname = "Cloudlight"
                worker._state["rooms"] = [bilibili_worker.room_record(101)]
                worker._state["settings"]["sessionsPerRoom"] = 4

                async def probe() -> tuple[int, str]:
                    return 42, "Cloudlight"

                worker.probe_account = probe
                started = worker.start({})
                self.assertTrue(started["running"])
                self.assertEqual(pool.reconciled, ([101], 4, 42))
                self.assertEqual(progress.started, 1)
                self.assertEqual(login.started, 1)
                stopped = worker.stop({})
                self.assertFalse(stopped["running"])
                self.assertEqual(pool.stopped, 1)
                self.assertEqual(progress.stopped, 1)
                self.assertEqual(login.stopped, 1)
                shutdown = worker.shutdown({})
                self.assertTrue(shutdown["shutdown"])
                self.assertTrue(worker.shutdown_requested)
            finally:
                self._close_worker(worker)

    def test_settings_persistence_and_claim_deduplication(self) -> None:
        with tempfile.TemporaryDirectory() as temporary:
            root = Path(temporary)
            worker = bilibili_worker.BilibiliWorker(root / "data", root / "worker.log")
            try:
                worker.add_room({"roomId": "https://live.bilibili.com/101", "name": "OWCS"})
                worker.add_room({"roomId": 202, "name": "第二直播间"})
                state = worker.save_settings({"settings": {
                    "enabled": True, "autoResumeDrops": True, "watchMode": "multi",
                    "sessionsPerRoom": 80, "reconnectDelay": 12, "taskInterval": 10,
                    "autoTaskProgress": False,
                    "autoClaim": True, "taskNotifications": True, "reconnectEnabled": True,
                    "taskIds": ["task-a", "task-b", "task-a"],
                }})
                self.assertEqual(state["settings"]["sessionsPerRoom"], 80)
                self.assertFalse(state["settings"]["autoTaskProgress"])
                self.assertEqual(state["taskIds"], ["task-a", "task-b"])
            finally:
                self._close_worker(worker)

            restored = bilibili_worker.BilibiliWorker(root / "data", root / "worker.log")
            try:
                self.assertEqual(len(restored.get_rooms({})["rooms"]), 2)
                self.assertEqual(restored.load_state({})["settings"]["sessionsPerRoom"], 80)
                self.assertFalse(restored.load_state({})["settings"]["autoTaskProgress"])
                self.assertEqual(restored.load_state({})["taskIds"], ["task-a", "task-b"])
            finally:
                self._close_worker(restored)

            worker = bilibili_worker.BilibiliWorker(root / "data-claim", root / "claim.log")
            try:
                calls = 0

                class FakeClient:
                    async def receive_all_mission_rewards(self, task_ids: list[str]) -> list[MissionRewardClaimResult]:
                        nonlocal calls
                        calls += 1
                        return [MissionRewardClaimResult(
                            task_id=task_ids[0], task_name="任务", reward_name="奖励",
                            status=6, message="领取成功", success=True, skipped=False,
                        )]

                first = asyncio.run(worker._claim_with_client(FakeClient(), ["task-a"], automatic=True))
                second = asyncio.run(worker._claim_with_client(FakeClient(), ["task-a"], automatic=True))
                self.assertEqual(len(first), 1)
                self.assertEqual(second, [])
                self.assertEqual(calls, 1)
                self.assertIn("task-a", worker._claimed_ids)
            finally:
                self._close_worker(worker)

    def test_dpapi_protected_file_does_not_contain_plaintext(self) -> None:
        if os.name != "nt":
            self.skipTest("Windows CurrentUser DPAPI is required")
        from dpapi import protect, read_protected, write_protected

        secret = "SESSDATA=fake-secret; bili_jct=fake-csrf; DedeUserID=42"
        with tempfile.TemporaryDirectory() as temporary:
            path = Path(temporary) / "credential.dpapi"
            encoded = protect(secret)
            self.assertNotIn(secret, encoded)
            write_protected(path, secret)
            file_text = path.read_text(encoding="ascii")
            self.assertNotIn("fake-secret", file_text)
            self.assertEqual(read_protected(path), secret)

    def test_worker_protocol_uses_explicit_bilibili_network_policy(self) -> None:
        worker_path = BILIBILI_DIR / "worker.py"
        with tempfile.TemporaryDirectory() as temporary:
            root = Path(temporary)
            environment = os.environ.copy()
            for name in PROXY_ENVIRONMENT_NAMES:
                environment[name] = "http://127.0.0.1:9"
            process = subprocess.Popen(
                [sys.executable, str(worker_path), "--data-dir", str(root / "data"),
                 "--log-file", str(root / "worker.log")],
                cwd=str(DROPS_DIR),
                env=environment,
                stdin=subprocess.PIPE,
                stdout=subprocess.PIPE,
                stderr=subprocess.PIPE,
                text=True,
                encoding="utf-8",
            )

            def request(request_id: str, command: str, payload: dict[str, object] | None = None) -> dict[str, object]:
                assert process.stdin is not None and process.stdout is not None
                process.stdin.write(json.dumps({"id": request_id, "command": command, "payload": payload or {}}) + "\n")
                process.stdin.flush()
                while True:
                    line = process.stdout.readline()
                    if not line:
                        raise AssertionError(f"worker exited while waiting for {request_id}")
                    message = json.loads(line)
                    if message.get("id") == request_id:
                        if not message.get("ok"):
                            raise AssertionError(message)
                        return message.get("result", {})

            hello = request("hello", "hello", {"protocol": 1})
            self.assertEqual(hello["platform"], "bilibili")
            self.assertEqual(hello["networkMode"], "DIRECT")
            self.assertEqual(hello["proxyPolicy"], "direct")
            proxy_result = request("proxy", "set_proxy", {
                "enableProxy": True, "proxyUrl": "http://127.0.0.1:9", "fallbackDirect": False,
                "bilibiliUseProxy": True,
            })
            self.assertEqual(proxy_result["networkMode"], "PROXY")
            self.assertTrue(proxy_result["useGlobalProxy"])
            self.assertTrue(proxy_result["globalProxyEnabled"])
            self.assertNotIn("proxyUrl", proxy_result)
            state = request("state", "load_state")
            self.assertEqual(state["networkMode"], "PROXY")
            self.assertTrue(state["useGlobalProxy"])
            self.assertFalse(state["credentialAvailable"])
            shutdown = request("shutdown", "shutdown")
            self.assertTrue(shutdown["shutdown"])
            self.assertEqual(process.wait(timeout=10), 0)
            worker_log = (root / "worker.log").read_text(encoding="utf-8")
            self.assertIn("network=PROXY", worker_log)
            self.assertIn("proxy=127.0.0.1:9", worker_log)
            self.assertNotIn("user:password", worker_log)
            if process.stdin is not None:
                process.stdin.close()
            if process.stdout is not None:
                process.stdout.close()
            if process.stderr is not None:
                process.stderr.close()


if __name__ == "__main__":
    unittest.main()
