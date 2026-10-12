from __future__ import annotations

import argparse
import importlib
import json
import logging
import logging.handlers
import queue
import os
import re
import sys
import threading
import time
import traceback
from pathlib import Path
from typing import Any, Callable
from urllib.parse import urlsplit


PROTOCOL_VERSION = 1
REQUIRED_COMMANDS = {
    "hello", "load_state", "start", "stop", "refresh", "save_settings",
    "login", "logout", "get_accounts", "get_tasks", "get_inventory", "get_logs",
}
_WRITE_LOCK = threading.Lock()
_OUTPUT_CLOSED = False
_SEQUENCE = 0
_SECRET = re.compile(
    r'''(?i)(["']?(?:authorization|authticket|bbsticket|userticket|oauth(?:[_ -]?token)?|access[_ -]?token|refresh[_ -]?token|device[_ -]?code|password|passwd|cookie|set-cookie|SESSDATA|bili_jct|DedeUserID(?:__ckMd5)?|buvid3|buvid4|b_nut|sid|LIVE_BUVID|csrf(?:_token)?)["']?\s*[:=]\s*["']?)([^"'\s,;}\]]+)'''
)
_BEARER = re.compile(r"(?i)(bearer\s+)[A-Za-z0-9._~+\-/=]+")
_SECRET_QUERY = re.compile(r"(?i)([?&](?:access_token|refresh_token|oauth_token|token|auth|csrf|csrf_token|SESSDATA|bili_jct|DedeUserID|DedeUserID__ckMd5)=)[^&#\s]+")
_URL_CREDENTIALS = re.compile(r"(?i)(https?://)[^/\s:@]+:[^@/\s]+@")


class RuntimeDependencyError(RuntimeError):
    """A local Worker runtime failure that cannot be repaired by network retry."""

    def __init__(
        self,
        component: str,
        code: str,
        public_message: str,
        technical_detail: str = "",
    ) -> None:
        super().__init__(public_message)
        self.component = component
        self.code = code
        self.public_message = public_message
        self.technical_detail = technical_detail


class TransientNetworkError(RuntimeError):
    """A retryable remote network failure; repeated supervisor attempts are logged at DEBUG."""

    def __init__(self, code: str, public_message: str) -> None:
        super().__init__(public_message)
        self.code = code
        self.public_message = public_message


def is_ssl_runtime_error(exc: BaseException) -> bool:
    current: BaseException | None = exc
    visited: set[int] = set()
    while current is not None and id(current) not in visited:
        visited.add(id(current))
        name = current.__class__.__name__.casefold()
        message = str(current).casefold()
        if (
            isinstance(current, RuntimeDependencyError)
            and current.component == "ssl"
        ) or (
            "_ssl" in message
            and any(marker in message for marker in ("dll load failed", "module", "import"))
        ) or "ssl is not supported" in message or name == "modulenotfounderror" and "_ssl" in message:
            return True
        current = current.__cause__ or current.__context__
    return False


def check_ssl_runtime(import_module: Callable[[str], Any] = importlib.import_module) -> dict[str, Any]:
    """Import the native SSL extension and create a real TLS context."""
    try:
        ssl_module = import_module("ssl")
        ssl_extension = import_module("_ssl")
        context = ssl_module.create_default_context()
        return {
            "available": True,
            "contextCreated": isinstance(context, ssl_module.SSLContext),
            "openssl": str(ssl_module.OPENSSL_VERSION),
            "sslModule": str(getattr(ssl_extension, "__file__", "")),
            "frozen": bool(getattr(sys, "frozen", False)),
            "python": sys.version.split()[0],
        }
    except Exception as exc:
        raise RuntimeDependencyError(
            "ssl",
            "ssl_runtime_unavailable",
            "Python SSL 组件无法加载，Drops 后台无法建立 HTTPS 连接。缺少或无法加载 SSL 运行库，请重新安装 CloudLight Blizzard。",
            redact(exc),
        ) from exc

if hasattr(sys.stdin, "reconfigure"):
    sys.stdin.reconfigure(encoding="utf-8")
if hasattr(sys.stdout, "reconfigure"):
    sys.stdout.reconfigure(encoding="utf-8", newline="\n")
if hasattr(sys.stderr, "reconfigure"):
    sys.stderr.reconfigure(encoding="utf-8")


def redact(value: object) -> str:
    text = str(value)
    text = _BEARER.sub(r"\1<redacted>", text)
    text = _SECRET.sub(r"\1<redacted>", text)
    text = _SECRET_QUERY.sub(r"\1<redacted>", text)
    return _URL_CREDENTIALS.sub(r"\1<redacted>@", text)


def emit(message: dict[str, Any]) -> None:
    global _OUTPUT_CLOSED, _SEQUENCE
    if _OUTPUT_CLOSED:
        return
    with _WRITE_LOCK:
        if _OUTPUT_CLOSED:
            return
        _SEQUENCE += 1
        envelope = dict(message)
        envelope["sequence"] = _SEQUENCE
        envelope["_emittedAt"] = time.monotonic_ns()
        encoded = json.dumps(envelope, ensure_ascii=False, separators=(",", ":"))
        try:
            sys.stdout.write(encoded + "\n")
            sys.stdout.flush()
        except (BrokenPipeError, ValueError):
            _OUTPUT_CLOSED = True
        except OSError as exc:
            if exc.errno == 22:
                _OUTPUT_CLOSED = True
            else:
                raise


def close_protocol_output() -> None:
    """Stop all later JSONL writes after the host side of the pipe is gone."""
    global _OUTPUT_CLOSED
    with _WRITE_LOCK:
        _OUTPUT_CLOSED = True


def event(name: str, payload: dict[str, Any] | None = None) -> None:
    emit({"event": name, "payload": payload or {}})


def validate_proxy_url(value: str) -> str:
    value = value.strip()
    parsed = urlsplit(value)
    if parsed.scheme.lower() not in {"http", "https"} or not parsed.hostname:
        raise ValueError("代理地址只支持 http:// 或 https://")
    try:
        parsed.port
    except ValueError as exc:
        raise ValueError("代理端口无效") from exc
    if parsed.path not in {"", "/"} or parsed.query or parsed.fragment:
        raise ValueError("代理地址不能包含路径、查询或片段")
    return value.rstrip("/")


def read_json(path: Path, default: dict[str, Any]) -> dict[str, Any]:
    if not path.exists():
        return json.loads(json.dumps(default, ensure_ascii=False))
    value = json.loads(path.read_text(encoding="utf-8"))
    if not isinstance(value, dict):
        raise ValueError(f"{path.name} 顶层必须是对象")
    merged = json.loads(json.dumps(default, ensure_ascii=False))
    merged.update(value)
    return merged


def parse_protocol_request(line: str) -> dict[str, Any]:
    # Windows PowerShell 5.1 may prepend a UTF-8 BOM when piping text to a
    # native executable. Accept it at the JSONL boundary without weakening
    # normal JSON validation.
    request = json.loads(line.lstrip("\ufeff"))
    if not isinstance(request, dict):
        raise ValueError("请求必须是 JSON 对象")
    return request


def atomic_write_json(path: Path, value: dict[str, Any]) -> None:
    path.parent.mkdir(parents=True, exist_ok=True)
    temporary = path.with_name(path.name + ".tmp")
    with temporary.open("w", encoding="utf-8", newline="\n") as handle:
        json.dump(value, handle, ensure_ascii=False, indent=2)
        handle.write("\n")
        handle.flush()
        os.fsync(handle.fileno())
    os.replace(temporary, path)


class _RedactingFormatter(logging.Formatter):
    def format(self, record: logging.LogRecord) -> str:
        return redact(super().format(record))


def configure_logging(path: Path) -> logging.Logger:
    path.parent.mkdir(parents=True, exist_ok=True)
    logger = logging.getLogger("cloudlight.drops")
    logger.setLevel(logging.INFO)
    logger.handlers.clear()
    handler = logging.handlers.RotatingFileHandler(
        path, maxBytes=2_000_000, backupCount=3, encoding="utf-8"
    )
    handler.setFormatter(_RedactingFormatter(
        "[%(asctime)s] %(levelname)s %(name)s: %(message)s", "%Y-%m-%d %H:%M:%S"
    ))
    logger.addHandler(handler)
    logger.propagate = False
    return logger


class WorkerBase:
    platform = "unknown"
    needs_https = True

    def __init__(self, data_dir: Path, log_file: Path) -> None:
        self.data_dir = data_dir.resolve()
        self.log_file = log_file.resolve()
        self.data_dir.mkdir(parents=True, exist_ok=True)
        self.logger = configure_logging(self.log_file)
        self.running = False
        self.shutdown_requested = False
        self._control_lock = threading.Lock()
        self._active_command = ""
        self._dispatch_thread = None
        self._active_read_future = None
        self._read_interrupted = False
        self._controls_pending = 0
        self.runtime_error: RuntimeDependencyError | None = None
        self._reported_runtime_errors: set[str] = set()
        self.proxy = {"enableProxy": False, "proxyUrl": "", "fallbackDirect": False}
        self.commands: dict[str, Callable[[dict[str, Any]], Any]] = {
            "hello": self.hello,
            "load_state": self.load_state,
            "start": self.start,
            "stop": self.stop,
            "refresh": self.refresh,
            "save_settings": self.save_settings,
            "login": self.login,
            "logout": self.logout,
            "get_accounts": self.get_accounts,
            "get_tasks": self.get_tasks,
            "get_inventory": self.get_inventory,
            "get_logs": self.get_logs,
            "set_proxy": self.set_proxy,
            "ssl_check": self.ssl_check,
            "provenance_check": self.provenance_check,
            "network_check": self.network_check,
            "shutdown": self.shutdown,
        }
        if self.needs_https:
            self._initialize_ssl_runtime()

    @property
    def runtime_available(self) -> bool:
        return self.runtime_error is None

    def runtime_state(self) -> dict[str, Any]:
        if self.runtime_error is None:
            return {"available": True}
        return {
            "available": False,
            "component": self.runtime_error.component,
            "code": self.runtime_error.code,
            "message": self.runtime_error.public_message,
            "retryable": False,
        }

    def _initialize_ssl_runtime(self) -> None:
        try:
            diagnostic = check_ssl_runtime()
            self.runtime_error = None
            self.logger.debug(
                "SSL runtime ready: python=%s frozen=%s openssl=%s _ssl=%s _MEIPASS=%s",
                diagnostic["python"], diagnostic["frozen"], diagnostic["openssl"],
                diagnostic["sslModule"], getattr(sys, "_MEIPASS", ""),
            )
        except RuntimeDependencyError as exc:
            self.runtime_error = exc
            self._report_runtime_error(exc)

    def _report_runtime_error(self, exc: RuntimeDependencyError) -> None:
        fingerprint = f"{exc.component}:{exc.code}:{exc.technical_detail}"
        first = fingerprint not in self._reported_runtime_errors
        self._reported_runtime_errors.add(fingerprint)
        if first:
            self.logger.error(
                "Python SSL 运行库无法加载，网络功能不可用。技术原因：%s",
                exc.technical_detail or exc.__class__.__name__,
            )
        else:
            self.logger.debug("已抑制重复的运行时依赖错误：%s", exc.code)
        event("runtime_error", {
            "component": exc.component,
            "code": exc.code,
            "message": exc.public_message,
            "retryable": False,
            "firstOccurrence": first,
        })

    def require_runtime(self) -> None:
        if self.runtime_error is not None:
            self._report_runtime_error(self.runtime_error)
            raise self.runtime_error

    def ssl_check(self, payload: dict[str, Any]) -> dict[str, Any]:
        try:
            result = check_ssl_runtime()
            recovered = self.runtime_error is not None
            self.runtime_error = None
            if recovered:
                self.logger.info("Python SSL 组件已恢复。")
                event("runtime_recovered", {"component": "ssl"})
            return result
        except RuntimeDependencyError as exc:
            self.runtime_error = exc
            self._report_runtime_error(exc)
            raise

    def provenance_check(self, payload: dict[str, Any]) -> dict[str, Any]:
        root = Path(getattr(sys, "_MEIPASS", ""))
        manifest = read_json(root / "build-manifest.json", {}) if getattr(sys, "frozen", False) else {}
        result = {"frozen": bool(getattr(sys, "frozen", False)),
                  "commit": manifest.get("commit"), "dirty": manifest.get("dirty"),
                  "adapterSha256": manifest.get("adapterSources", {}).get("sha256"),
                  "purpose": manifest.get("purpose"), "protocol": PROTOCOL_VERSION}
        if self.platform == "soop" and manifest.get("soopCore"):
            from Shared.build_provenance import sources
            actual = sources(root / "core")
            expected = manifest["soopCore"]
            result["core"] = {"commit": expected["commit"], "dirty": expected["dirty"],
                              "sha256": actual["sha256"], "expectedSha256": expected["sha256"],
                              "matches": actual["sha256"] == expected["sha256"],
                              "capabilities": expected["capabilities"], "capabilityVersion": expected["capabilityVersion"],
                              "redistributionAuthorized": expected["redistributionAuthorized"]}
        return result

    def status(self, status: str, summary: str = "") -> None:
        event("status", {"status": status, "summary": summary, "running": self.running})

    def hello(self, payload: dict[str, Any]) -> dict[str, Any]:
        requested = int(payload.get("protocol", PROTOCOL_VERSION))
        if requested != PROTOCOL_VERSION:
            raise ValueError(f"不支持协议版本 {requested}")
        return {
            "platform": self.platform,
            "protocol": PROTOCOL_VERSION,
            "commands": sorted(self.commands),
            "pid": os.getpid(),
            "runtime": self.runtime_state(),
        }

    def load_state(self, payload: dict[str, Any]) -> dict[str, Any]:
        return {"_snapshotStarted": time.monotonic_ns(), "running": self.running, "proxy": self.proxy, "runtime": self.runtime_state()}

    def network_check(self, payload: dict[str, Any]) -> dict[str, Any]:
        """Probe official endpoints without credentials using the configured route."""
        import ssl
        import socket
        endpoints = {
            "twitch": ["https://id.twitch.tv/oauth2/validate", "https://gql.twitch.tv/gql", "https://www.twitch.tv"],
            "soop": ["https://login.sooplive.co.kr", "https://drops.sooplive.com", "https://exlogcollector.sooplive.com/gather"],
            "youtube": ["https://www.youtube.com"],
            "bilibili": ["https://passport.bilibili.com", "https://api.live.bilibili.com"],
        }
        proxy = self.proxy["proxyUrl"] if self.proxy["enableProxy"] else ""
        context = ssl.create_default_context()
        results = []
        for url in endpoints.get(self.platform, []):
            host = urlsplit(url).hostname
            item = {"host": host, "route": "proxy" if proxy else "direct", "reachable": False}
            # This is local DNS only. A CONNECT proxy may resolve independently.
            if not proxy:
                try:
                    item["localDns"] = sorted({entry[4][0] for entry in socket.getaddrinfo(host, 443)})
                except OSError:
                    item["dnsError"] = True
            routes = [proxy]
            if proxy and self.proxy["fallbackDirect"]:
                routes.append("")
            for route in routes:
                item["route"] = "proxy" if route else "direct"
                try:
                    if self.platform == "bilibili":
                        import httpx
                        with httpx.Client(proxy=route or None, trust_env=False, verify=context, timeout=8) as client:
                            status = client.get(url).status_code
                    elif self.platform == "youtube":
                        import requests
                        with requests.Session() as client:
                            client.trust_env = False
                            response = client.get(url, proxies={"https": route, "http": route} if route else {}, timeout=8)
                            status = response.status_code
                    else:
                        import asyncio
                        import aiohttp
                        async def check():
                            async with aiohttp.ClientSession(timeout=aiohttp.ClientTimeout(total=8), trust_env=False) as client:
                                async with client.get(url, proxy=route or None, ssl=context) as response:
                                    return response.status
                        status = asyncio.run(check())
                    # 401/403/405 proves HTTPS connectivity, not authentication.
                    item.update(reachable=True, status=status, stage="http")
                    if status in {401, 403, 405, 412, 429}:
                        item["statusRemark"] = {401: "接口需要登录（探测未携带凭据）", 403: "接口拒绝请求",
                                                405: "接口不接受 GET 探测", 412: "接口拒绝请求或限制访问",
                                                429: "接口限流"}[status]
                except Exception as exc:
                    item.update(errorType=type(exc).__name__, detail=redact(exc), stage="connect_or_tls")
                    continue
                break
            results.append(item)
        return {"results": results, "tlsVerification": True, "authenticationTested": False,
                "ok": all(item["reachable"] and item.get("status", 0) not in {403, 412, 429} and item.get("status", 0) < 500 for item in results)}

    def start(self, payload: dict[str, Any]) -> dict[str, Any]:
        self.require_runtime()
        self.running = True
        self.status("运行中")
        return self.load_state({})

    def stop(self, payload: dict[str, Any]) -> dict[str, Any]:
        self.running = False
        self.status("已停止")
        return self.load_state({})

    def refresh(self, payload: dict[str, Any]) -> dict[str, Any]:
        return self.load_state(payload)

    def save_settings(self, payload: dict[str, Any]) -> dict[str, Any]:
        raise NotImplementedError

    def login(self, payload: dict[str, Any]) -> dict[str, Any]:
        raise NotImplementedError

    def logout(self, payload: dict[str, Any]) -> dict[str, Any]:
        raise NotImplementedError

    def get_accounts(self, payload: dict[str, Any]) -> list[dict[str, Any]]:
        return []

    def get_tasks(self, payload: dict[str, Any]) -> list[dict[str, Any]]:
        return []

    def get_inventory(self, payload: dict[str, Any]) -> list[dict[str, Any]]:
        return []

    def get_logs(self, payload: dict[str, Any]) -> dict[str, Any]:
        limit = max(1, min(int(payload.get("limit", 300)), 2000))
        if not self.log_file.exists():
            return {"lines": []}
        lines = self.log_file.read_text(encoding="utf-8", errors="replace").splitlines()
        return {"lines": [redact(line) for line in lines[-limit:]]}

    def set_proxy(self, payload: dict[str, Any]) -> dict[str, Any]:
        enabled = bool(payload.get("enableProxy", payload.get("EnableProxy", False)))
        url = str(payload.get("proxyUrl", payload.get("ProxyUrl", ""))).strip()
        fallback = bool(payload.get("fallbackDirect", payload.get("FallbackDirect", False)))
        if enabled:
            url = validate_proxy_url(url)
        self.proxy = {"enableProxy": enabled, "proxyUrl": url, "fallbackDirect": fallback}
        self.on_proxy_changed()
        return self.proxy

    def on_proxy_changed(self) -> None:
        pass

    def shutdown(self, payload: dict[str, Any]) -> dict[str, Any]:
        if self.shutdown_requested:
            return {"shutdown": True}
        self.stop({})
        self.shutdown_requested = True
        return {"shutdown": True}

    def dispatch(self, request: dict[str, Any]) -> dict[str, Any]:
        request_id = str(request.get("id", ""))
        command = str(request.get("command", ""))
        payload = request.get("payload")
        if payload is None:
            payload = {}
        if not request_id:
            raise ValueError("请求缺少 id")
        if not isinstance(payload, dict):
            raise ValueError("payload 必须是对象")
        handler = self.commands.get(command)
        if handler is None:
            raise ValueError(f"未知命令: {command}")
        with self._control_lock:
            self._active_command = command
            self._dispatch_thread = threading.get_ident()
            if command in {"stop", "stop_account", "shutdown"}:
                self._controls_pending = max(0, self._controls_pending - 1)
            self._read_interrupted = command == "refresh" and self._controls_pending > 0
        try:
            result = handler(payload)
        finally:
            with self._control_lock:
                self._active_command = ""
                self._dispatch_thread = None
                self._active_read_future = None
        return {"id": request_id, "ok": True, "result": result if result is not None else {}}

    def wait_future(self, future: Any, timeout: float) -> Any:
        if not hasattr(self, "_control_lock"):
            return future.result(timeout=timeout)
        tracked = False
        with self._control_lock:
            if self._active_command == "refresh" and self._dispatch_thread == threading.get_ident():
                self._active_read_future = future
                tracked = True
                if self._read_interrupted:
                    future.cancel()
        try:
            return future.result(timeout=timeout)
        finally:
            if tracked:
                with self._control_lock:
                    if self._active_read_future is future:
                        self._active_read_future = None

    def _interrupt_refresh_for_control(self) -> None:
        with self._control_lock:
            self._controls_pending += 1
            if self._active_command != "refresh":
                return
            self._read_interrupted = True
            future = self._active_read_future
        if future is not None:
            future.cancel()

    def run(self) -> int:
        if self.runtime_error is not None:
            self.status("后台组件异常", self.runtime_error.public_message)
        else:
            self.status("就绪")
        # Keep reading control commands during a blocking read-only refresh.
        # Business handlers still execute on one thread, in order. Only the
        # active refresh coroutine is cancelled; claim operations are untouched.
        inbox = queue.Queue()
        def read_input():
            for line in sys.stdin:
                try:
                    request = parse_protocol_request(line)
                    command = request.get("command")
                    if (command in {"stop", "stop_account", "shutdown"}
                            and command in self.commands and str(request.get("id", ""))
                            and (request.get("payload") is None or isinstance(request.get("payload"), dict))):
                        self._interrupt_refresh_for_control()
                except (ValueError, TypeError):
                    pass
                inbox.put(line)
            inbox.put(None)
        threading.Thread(target=read_input, daemon=True, name="drops-control-input").start()
        while True:
            line = inbox.get()
            if line is None:
                break
            request_id = ""
            try:
                request = parse_protocol_request(line)
                request_id = str(request.get("id", ""))
                response = self.dispatch(request)
            except Exception as exc:
                if isinstance(exc, RuntimeDependencyError):
                    self._report_runtime_error(exc)
                    response = {
                        "id": request_id, "ok": False,
                        "error": exc.public_message, "code": exc.code,
                    }
                elif isinstance(exc, TransientNetworkError):
                    self.logger.debug("后台网络请求暂时失败：%s", exc.code)
                    response = {
                        "id": request_id, "ok": False,
                        "error": exc.public_message, "code": exc.code,
                        "retryable": True,
                    }
                else:
                    self.logger.error("后台请求失败：%s", redact(exc))
                    self.logger.debug("%s", traceback.format_exc())
                    response = {"id": request_id, "ok": False, "error": redact(exc)}
            emit(response)
            if self.shutdown_requested:
                break
        if self.shutdown_requested:
            # shutdown() already performed core cleanup before its response was
            # emitted. Do not run stop() twice or write another status event.
            return 0
        # stdin EOF means the host pipe has already gone away. Cleanup still has
        # to run, but no cleanup status may be written to the invalid pipe.
        close_protocol_output()
        try:
            self.stop({})
        except Exception:
            self.logger.exception("后台服务停止异常")
        return 0


def parse_worker_args() -> argparse.Namespace:
    parser = argparse.ArgumentParser()
    parser.add_argument("--data-dir", required=True, type=Path)
    parser.add_argument("--log-file", required=True, type=Path)
    return parser.parse_args()


def run_worker(factory: Callable[[Path, Path], WorkerBase]) -> int:
    args = parse_worker_args()
    worker = factory(args.data_dir, args.log_file)
    return worker.run()
