from __future__ import annotations

import argparse
import json
import logging
import logging.handlers
import os
import re
import sys
import threading
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
_SECRET = re.compile(
    r'''(?i)(["']?(?:authorization|authticket|bbsticket|userticket|oauth(?:[_ -]?token)?|access[_ -]?token|refresh[_ -]?token|password|passwd|cookie|set-cookie)["']?\s*[:=]\s*["']?)([^"'\s,;}\]]+)'''
)
_BEARER = re.compile(r"(?i)(bearer\s+)[A-Za-z0-9._~+\-/=]+")
_SECRET_QUERY = re.compile(r"(?i)([?&](?:access_token|refresh_token|oauth_token|token|auth)=)[^&#\s]+")

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
    return _SECRET_QUERY.sub(r"\1<redacted>", text)


def emit(message: dict[str, Any]) -> None:
    global _OUTPUT_CLOSED
    if _OUTPUT_CLOSED:
        return
    encoded = json.dumps(message, ensure_ascii=False, separators=(",", ":"))
    with _WRITE_LOCK:
        if _OUTPUT_CLOSED:
            return
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

    def __init__(self, data_dir: Path, log_file: Path) -> None:
        self.data_dir = data_dir.resolve()
        self.log_file = log_file.resolve()
        self.data_dir.mkdir(parents=True, exist_ok=True)
        self.logger = configure_logging(self.log_file)
        self.running = False
        self.shutdown_requested = False
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
            "shutdown": self.shutdown,
        }

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
        }

    def load_state(self, payload: dict[str, Any]) -> dict[str, Any]:
        return {"running": self.running, "proxy": self.proxy}

    def start(self, payload: dict[str, Any]) -> dict[str, Any]:
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
        payload = request.get("payload") or {}
        if not request_id:
            raise ValueError("请求缺少 id")
        if not isinstance(payload, dict):
            raise ValueError("payload 必须是对象")
        handler = self.commands.get(command)
        if handler is None:
            raise ValueError(f"未知命令: {command}")
        result = handler(payload)
        return {"id": request_id, "ok": True, "result": result if result is not None else {}}

    def run(self) -> int:
        self.status("就绪")
        for line in sys.stdin:
            request_id = ""
            try:
                request = json.loads(line)
                if not isinstance(request, dict):
                    raise ValueError("请求必须是 JSON 对象")
                request_id = str(request.get("id", ""))
                response = self.dispatch(request)
            except Exception as exc:
                self.logger.error("Worker command failed: %s", redact(exc))
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
            self.logger.exception("Worker stop failed")
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
