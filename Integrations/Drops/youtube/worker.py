from __future__ import annotations

import json
import importlib
import os
import re
import shutil
import socket
import subprocess
import sys
import threading
import time
from datetime import datetime, timedelta
from pathlib import Path
from typing import Any
from urllib.parse import parse_qs, urlparse

sys.path.insert(0, str(Path(__file__).resolve().parents[1]))
from Shared.protocol import WorkerBase, atomic_write_json, event, read_json, run_worker


DEFAULT_CONFIG: dict[str, Any] = {
    "browser": "chrome",
    "browser_path": "",
    "headless": False,
    "mute": True,
    "mode": "auto",
    "manual_url": "",
    "check_interval": 300,
    "channels": [
        {
            "name": "Overwatch Esports",
            "id": "UCiAInBL9kUzz1XRxk66v-gw",
            "url": "https://www.youtube.com/channel/UCiAInBL9kUzz1XRxk66v-gw/live",
            "enabled": True,
        },
        {
            "name": "Overwatch Contenders",
            "id": "UCWPW0pjx6gncOEnTW8kYzrg",
            "url": "https://www.youtube.com/channel/UCWPW0pjx6gncOEnTW8kYzrg/live",
            "enabled": False,
        },
    ],
    "profiles": ["主账号"],
}
INVALID_PROFILE = re.compile(r'[<>:"/\\|?*\x00-\x1f]')
RESERVED = {"CON", "PRN", "AUX", "NUL", *(f"COM{i}" for i in range(1, 10)), *(f"LPT{i}" for i in range(1, 10))}
NO_LIVE_MARKERS = (
    "not currently live", "is not live", "this live event will begin",
    "premieres in", "has ended", "live event has ended",
)


class _YtDlpLogBridge:
    """Keep yt-dlp diagnostics in DEBUG instead of writing raw text to the user log."""

    def __init__(self, logger: Any) -> None:
        self._logger = logger

    def debug(self, message: object) -> None:
        self._logger.debug("yt-dlp: %s", message)

    def warning(self, message: object) -> None:
        self._logger.debug("yt-dlp warning: %s", message)

    def error(self, message: object) -> None:
        self._logger.debug("yt-dlp error: %s", message)


def normalize_profile(value: object) -> str:
    name = str(value).strip()
    if not name or len(name) > 40 or name in {".", ".."} or name.endswith((".", " ")):
        raise ValueError("观看账号名称无效")
    if INVALID_PROFILE.search(name) or name.upper() in RESERVED:
        raise ValueError("观看账号名称包含 Windows 不允许的字符")
    return name


def normalize_url(value: object) -> str:
    url = str(value).strip()
    parsed = urlparse(url)
    host = (parsed.hostname or "").lower()
    if parsed.scheme not in {"http", "https"} or not (host == "youtu.be" or host == "youtube.com" or host.endswith(".youtube.com")):
        raise ValueError("只支持 http(s)://youtube.com 或 youtu.be 地址")
    return url


def video_id(url: str) -> str:
    parsed = urlparse(url)
    host = (parsed.hostname or "").lower()
    candidate = ""
    if host == "youtu.be":
        candidate = parsed.path.strip("/").split("/", 1)[0]
    elif parsed.path.rstrip("/") == "/watch":
        candidate = parse_qs(parsed.query).get("v", [""])[0]
    else:
        parts = [part for part in parsed.path.split("/") if part]
        if len(parts) > 1 and parts[0] in {"live", "embed", "shorts"}:
            candidate = parts[1]
    return candidate if re.fullmatch(r"[A-Za-z0-9_-]{11}", candidate) else ""


def free_local_port() -> int:
    with socket.socket(socket.AF_INET, socket.SOCK_STREAM) as sock:
        sock.bind(("127.0.0.1", 0))
        return int(sock.getsockname()[1])


class BrowserSession:
    def __init__(self, profile: str, process: subprocess.Popen[Any], port: int, url: str) -> None:
        self.profile = profile
        self.process = process
        self.port = port
        self.url = url

    def alive(self) -> bool:
        return self.process.poll() is None


class YouTubeWorker(WorkerBase):
    platform = "youtube"

    def __init__(self, data_dir: Path, log_file: Path) -> None:
        super().__init__(data_dir, log_file)
        self.config_path = self.data_dir / "config.json"
        self.profiles_dir = self.data_dir / "profiles"
        self.logs_dir = self.data_dir / "logs"
        self.history_path = self.data_dir / "watch_history.json"
        self.profiles_dir.mkdir(parents=True, exist_ok=True)
        self.logs_dir.mkdir(parents=True, exist_ok=True)
        self.config = self._load_config()
        self.history = read_json(self.history_path, {"records": {}})
        self.sessions: dict[str, BrowserSession] = {}
        self._stop = threading.Event()
        self._thread: threading.Thread | None = None
        self._lock = threading.RLock()
        self.current_stream: dict[str, Any] | None = None
        self.started_at: datetime | None = None
        self.last_browser_status = "未启动"
        self._last_reported_stream_key = ""
        self._last_scan_failed = False
        self._proxy_fallback_reported = False
        self.commands.update({
            "get_profiles": self.get_profiles,
            "add_profile": self.add_profile,
            "delete_profile": self.delete_profile,
            "open_login": self.open_login,
            "get_channels": self.get_channels,
            "add_channel": self.add_channel,
            "update_channel": self.update_channel,
            "delete_channel": self.delete_channel,
            "get_history": self.get_history,
            "clear_history": self.clear_history,
            "detect_browser": self.detect_browser,
        })

    def _load_config(self) -> dict[str, Any]:
        config = read_json(self.config_path, DEFAULT_CONFIG)
        config["browser"] = config.get("browser") if config.get("browser") in {"chrome", "brave"} else "chrome"
        config["mode"] = config.get("mode") if config.get("mode") in {"auto", "manual"} else "auto"
        config["headless"] = bool(config.get("headless", False))
        config["mute"] = bool(config.get("mute", True))
        config["browser_path"] = str(config.get("browser_path", ""))
        config["manual_url"] = str(config.get("manual_url", ""))
        config["check_interval"] = max(180, min(3600, int(config.get("check_interval", 300))))
        channels = []
        for raw in config.get("channels", []):
            if not isinstance(raw, dict):
                continue
            url, channel_id = str(raw.get("url", "")).strip(), str(raw.get("id", "")).strip()
            if not url and not channel_id:
                continue
            channels.append({
                "name": str(raw.get("name") or channel_id or url), "id": channel_id,
                "url": url, "enabled": bool(raw.get("enabled", True)),
            })
        config["channels"] = channels or json.loads(json.dumps(DEFAULT_CONFIG["channels"], ensure_ascii=False))
        profiles: list[str] = []
        for raw in config.get("profiles", []):
            try:
                name = normalize_profile(raw)
            except ValueError:
                continue
            if name not in profiles:
                profiles.append(name)
        config["profiles"] = profiles or ["主账号"]
        atomic_write_json(self.config_path, config)
        return config

    def _state(self) -> dict[str, Any]:
        with self._lock:
            sessions = [
                {"profile": profile, "running": session.alive(), "url": session.url, "debugPort": session.port}
                for profile, session in self.sessions.items()
            ]
        try:
            detected_browser_path = str(self._browser_binary())
        except (FileNotFoundError, OSError):
            detected_browser_path = ""
        return {
            "running": self.running,
            "status": "正在观看" if self.current_stream and self.running else ("正在检测" if self.running else "已停止"),
            "stream": self.current_stream,
            "browserStatus": self.last_browser_status,
            "detectedBrowserPath": detected_browser_path,
            "sessions": sessions,
            "config": self.config,
            "history": self._history_snapshot(),
            "proxy": self.proxy,
        }

    def load_state(self, payload: dict[str, Any]) -> dict[str, Any]:
        return self._state()

    def save_settings(self, payload: dict[str, Any]) -> dict[str, Any]:
        merged = dict(self.config)
        merged.update(payload.get("settings", payload))
        self.config = merged
        self.config = self._load_from_value(merged)
        atomic_write_json(self.config_path, self.config)
        event("state", self._state())
        return self.config

    def _load_from_value(self, value: dict[str, Any]) -> dict[str, Any]:
        temporary = self.config_path.with_name(".config.validate.json")
        try:
            atomic_write_json(temporary, value)
            original = self.config_path
            self.config_path = temporary
            normalized = self._load_config()
            self.config_path = original
            return normalized
        finally:
            self.config_path = self.data_dir / "config.json"
            temporary.unlink(missing_ok=True)

    def start(self, payload: dict[str, Any]) -> dict[str, Any]:
        if self._thread and self._thread.is_alive():
            return self._state()
        self._validate_runtime_dependencies()
        self.config = self._load_config()
        if self.config["mode"] == "manual":
            normalize_url(self.config.get("manual_url", ""))
        self.running = True
        self._stop.clear()
        self.started_at = datetime.now().astimezone()
        self._thread = threading.Thread(target=self._watch_loop, name="youtube-watcher", daemon=True)
        self._thread.start()
        enabled_channels = sum(1 for channel in self.config["channels"] if channel.get("enabled", True))
        self.logger.info("正在启动 YouTube 观看服务，已启用 %d 个频道", enabled_channels)
        self.status("正在检测", f"{len(self.config['profiles'])} 个观看账号")
        return self._state()

    def _validate_runtime_dependencies(self) -> None:
        try:
            for module in ("yt_dlp", "requests", "websocket"):
                importlib.import_module(module)
        except ModuleNotFoundError as exc:
            self.logger.exception("YouTube 观看服务缺少运行组件")
            raise RuntimeError("YouTube 观看服务组件不完整，请重新构建或安装后台组件。") from exc

    def stop(self, payload: dict[str, Any]) -> dict[str, Any]:
        was_running = self.running
        self.running = False
        self._stop.set()
        thread = self._thread
        if thread and thread is not threading.current_thread():
            thread.join(timeout=8)
        self._thread = None
        self._close_all_browsers()
        self.current_stream = None
        self.last_browser_status = "已停止"
        if was_running:
            self.logger.info("YouTube 观看服务已停止")
        self.status("已停止", f"{len(self.config['profiles'])} 个观看账号")
        return self._state()

    def refresh(self, payload: dict[str, Any]) -> dict[str, Any]:
        self.config = self._load_config()
        return self._state()

    def login(self, payload: dict[str, Any]) -> dict[str, Any]:
        return self.open_login(payload)

    def logout(self, payload: dict[str, Any]) -> dict[str, Any]:
        name = normalize_profile(payload.get("profile", ""))
        self._close_profile(name)
        return {"profile": name, "message": "已关闭该观看账号的浏览器；登录数据仍保留。"}

    def get_accounts(self, payload: dict[str, Any]) -> list[dict[str, Any]]:
        return self.get_profiles(payload)

    def get_tasks(self, payload: dict[str, Any]) -> list[dict[str, Any]]:
        return [self.current_stream] if self.current_stream else []

    def get_inventory(self, payload: dict[str, Any]) -> list[dict[str, Any]]:
        return self._history_snapshot()["rows"]

    def get_profiles(self, payload: dict[str, Any]) -> list[dict[str, Any]]:
        with self._lock:
            return [
                {
                    "name": name,
                    "path": str(self.profiles_dir / name),
                    "browserRunning": bool(self.sessions.get(name) and self.sessions[name].alive()),
                    "currentUrl": self.sessions[name].url if name in self.sessions else "",
                }
                for name in self.config["profiles"]
            ]

    def detect_browser(self, payload: dict[str, Any]) -> dict[str, Any]:
        browser = str(payload.get("browser", self.config.get("browser", "chrome"))).lower()
        if browser not in {"chrome", "brave"}:
            raise ValueError("只支持 Google Chrome 或 Brave")
        previous = self.config.get("browser", "chrome")
        try:
            self.config["browser"] = browser
            path = str(self._browser_binary())
            label = "Google Chrome" if browser == "chrome" else "Brave"
            self.logger.info("已找到 %s：%s", label, path)
            return {"browser": browser, "path": path}
        finally:
            self.config["browser"] = previous

    def add_profile(self, payload: dict[str, Any]) -> dict[str, Any]:
        name = normalize_profile(payload.get("name", ""))
        if name not in self.config["profiles"]:
            self.config["profiles"].append(name)
        (self.profiles_dir / name).mkdir(parents=True, exist_ok=True)
        atomic_write_json(self.config_path, self.config)
        return {"name": name}

    def delete_profile(self, payload: dict[str, Any]) -> dict[str, Any]:
        name = normalize_profile(payload.get("name", ""))
        self._close_profile(name)
        if name in self.config["profiles"]:
            self.config["profiles"].remove(name)
        if not self.config["profiles"]:
            self.config["profiles"] = ["主账号"]
        atomic_write_json(self.config_path, self.config)
        if bool(payload.get("deleteData", False)):
            shutil.rmtree(self.profiles_dir / name, ignore_errors=False)
        return {"name": name, "deletedData": bool(payload.get("deleteData", False))}

    def open_login(self, payload: dict[str, Any]) -> dict[str, Any]:
        name = normalize_profile(payload.get("profile") or self.config["profiles"][0])
        session = self._launch(name, "https://www.youtube.com/", login=True)
        self.logger.info("浏览器登录窗口已打开")
        return {"profile": name, "pid": session.process.pid, "profilePath": str(self.profiles_dir / name)}

    def get_channels(self, payload: dict[str, Any]) -> list[dict[str, Any]]:
        return list(self.config["channels"])

    def add_channel(self, payload: dict[str, Any]) -> dict[str, Any]:
        channel = self._normalize_channel(payload.get("channel", payload))
        self.config["channels"].append(channel)
        atomic_write_json(self.config_path, self.config)
        return channel

    def update_channel(self, payload: dict[str, Any]) -> dict[str, Any]:
        index = int(payload.get("index", -1))
        if not 0 <= index < len(self.config["channels"]):
            raise ValueError("频道索引无效")
        channel = self._normalize_channel(payload.get("channel", payload))
        self.config["channels"][index] = channel
        atomic_write_json(self.config_path, self.config)
        return channel

    def delete_channel(self, payload: dict[str, Any]) -> dict[str, Any]:
        index = int(payload.get("index", -1))
        if not 0 <= index < len(self.config["channels"]):
            raise ValueError("频道索引无效")
        removed = self.config["channels"].pop(index)
        atomic_write_json(self.config_path, self.config)
        return removed

    @staticmethod
    def _normalize_channel(value: Any) -> dict[str, Any]:
        if not isinstance(value, dict):
            raise ValueError("频道必须是对象")
        url, channel_id = str(value.get("url", "")).strip(), str(value.get("id", "")).strip()
        if url:
            normalize_url(url)
        if not url and not channel_id:
            raise ValueError("频道 URL 或 ID 至少填写一项")
        return {"name": str(value.get("name") or channel_id or url), "id": channel_id, "url": url, "enabled": bool(value.get("enabled", True))}

    def get_history(self, payload: dict[str, Any]) -> dict[str, Any]:
        return self._history_snapshot()

    def clear_history(self, payload: dict[str, Any]) -> dict[str, Any]:
        self.history = {"records": {}}
        atomic_write_json(self.history_path, self.history)
        return self._history_snapshot()

    def on_proxy_changed(self) -> None:
        if self.running and any(session.alive() for session in self.sessions.values()):
            event("notice", {"message": "代理设置将在下次重新启动观看窗口后生效。"})

    def _watch_loop(self) -> None:
        try:
            while not self._stop.is_set():
                enabled_channels = sum(
                    1 for channel in self.config["channels"] if channel.get("enabled", True)
                )
                self.logger.info("正在检查 %d 个已启用频道", enabled_channels)
                stream = self._detect_stream()
                if stream:
                    self.current_stream = stream
                    for profile in list(self.config["profiles"]):
                        if self._stop.is_set():
                            break
                        self._ensure_session(profile, stream["url"])
                    stream_key = str(stream.get("videoId") or stream.get("url", ""))
                    if stream_key != self._last_reported_stream_key:
                        self.logger.info("发现直播：%s", stream.get("channel") or stream.get("title", "YouTube"))
                        self.logger.info("开始观看：%s", stream.get("title", "YouTube 直播"))
                        self._last_reported_stream_key = stream_key
                    self.last_browser_status = "浏览器运行中"
                    self.status("正在观看", f"{stream.get('channel', '')} · {len(self.sessions)} 个观看账号")
                    event("stream", stream)
                    self._sample_until_next_check(stream)
                else:
                    if self._last_reported_stream_key:
                        self.logger.info("直播已结束，等待下一次检查")
                        self._last_reported_stream_key = ""
                    elif not self._last_scan_failed:
                        self.logger.info("本轮未发现正在直播的频道")
                    self.current_stream = None
                    if self._last_scan_failed:
                        self.status("检测异常", "将在下一轮自动重试")
                    else:
                        self.status("等待直播", f"{enabled_channels} 个频道")
                    self._stop.wait(self.config["check_interval"])
        except Exception as exc:
            self.logger.exception("YouTube 观看服务运行异常")
            self.running = False
            message = "YouTube 检测失败，请检查网络或浏览器设置"
            self.status("运行异常", message)
            event("error", {"message": message})

    def _detect_stream(self) -> dict[str, Any] | None:
        self._last_scan_failed = False
        if self.config["mode"] == "manual":
            url = normalize_url(self.config["manual_url"])
            return {"title": "手动观看", "channel": "手动 URL", "url": url, "videoId": video_id(url), "source": "manual"}
        enabled_channels = [channel for channel in self.config["channels"] if channel.get("enabled", True)]
        failures: list[str] = []
        checked_no_live = 0
        for channel in enabled_channels:
            base = channel.get("url") or f"https://www.youtube.com/channel/{channel.get('id', '')}/live"
            url = base if str(base).rstrip("/").endswith("/live") else str(base).rstrip("/") + "/live"
            result, no_live, error = self._detect_with_ytdlp(channel, url)
            if result:
                return result
            public, public_checked = self._detect_single_public(channel, url)
            if public:
                return public
            if no_live or public_checked:
                checked_no_live += 1
                continue
            if error is not None and self.proxy["enableProxy"] and self.proxy["fallbackDirect"]:
                if not self._proxy_fallback_reported:
                    self.logger.info("YouTube 代理连接失败，正在尝试直连")
                    self._proxy_fallback_reported = True
                previous = self.proxy["enableProxy"]
                self.proxy["enableProxy"] = False
                try:
                    result, no_live, direct_error = self._detect_with_ytdlp(channel, url)
                    if result:
                        return result
                    public, public_checked = self._detect_single_public(channel, url)
                    if public:
                        return public
                    if no_live or public_checked:
                        checked_no_live += 1
                        continue
                    error = direct_error or error
                finally:
                    self.proxy["enableProxy"] = previous
            if error is not None:
                failures.append(f"{channel.get('name', '')}: {error}")
        if failures:
            self.logger.debug("YouTube channel detection details: %s", " | ".join(failures))
        if enabled_channels and failures and checked_no_live == 0 and len(failures) == len(enabled_channels):
            self._last_scan_failed = True
            self.logger.warning("YouTube 检测失败，请检查网络或浏览器设置")
        return None

    @staticmethod
    def _is_no_live_error(exc: BaseException) -> bool:
        message = str(exc).casefold()
        return any(marker in message for marker in NO_LIVE_MARKERS)

    def _detect_with_ytdlp(
        self, channel: dict[str, Any], url: str
    ) -> tuple[dict[str, Any] | None, bool, BaseException | None]:
        try:
            import yt_dlp
            options: dict[str, Any] = {
                "quiet": True, "no_warnings": True, "skip_download": True, "noplaylist": True,
                "ignore_no_formats_error": True, "socket_timeout": 15, "retries": 1,
                "extractor_retries": 1, "cachedir": False, "logger": _YtDlpLogBridge(self.logger),
            }
            if self.proxy["enableProxy"]:
                options["proxy"] = self.proxy["proxyUrl"]
            with yt_dlp.YoutubeDL(options) as ydl:
                info = ydl.extract_info(url, download=False)
            if info and info.get("_type") == "playlist":
                info = next((entry for entry in info.get("entries") or [] if entry), {})
            if info and (info.get("is_live") or info.get("live_status") == "is_live"):
                found_id = str(info.get("id") or "")
                return ({
                    "title": str(info.get("title") or "正在直播"),
                    "channel": str(info.get("channel") or info.get("uploader") or channel.get("name", "")),
                    "url": str(info.get("webpage_url") or (f"https://www.youtube.com/watch?v={found_id}" if found_id else url)),
                    "videoId": found_id, "source": "yt-dlp",
                }, False, None)
            return None, True, None
        except Exception as exc:
            if self._is_no_live_error(exc):
                return None, True, None
            self.logger.debug("yt-dlp detection failed for %s: %s", channel.get("name", ""), exc)
            return None, False, exc

    def _detect_single_public(
        self, channel: dict[str, Any], url: str
    ) -> tuple[dict[str, Any] | None, bool]:
        try:
            import requests
            session = requests.Session()
            session.trust_env = False
            proxies = {"http": self.proxy["proxyUrl"], "https": self.proxy["proxyUrl"]} if self.proxy["enableProxy"] else None
            response = session.get(url, timeout=15, proxies=proxies, headers={"User-Agent": "Mozilla/5.0", "Accept-Language": "zh-CN,zh;q=0.9"})
            response.raise_for_status()
            if not re.search(r'"isLiveNow"\s*:\s*true', response.text):
                return None, True
            ids = re.findall(r'"videoId"\s*:\s*"([A-Za-z0-9_-]{11})"', response.text)
            if not ids:
                return None, True
            found = ids[-1]
            return ({"title": "正在直播", "channel": channel.get("name", ""), "url": f"https://www.youtube.com/watch?v={found}", "videoId": found, "source": "public-page"}, True)
        except Exception as exc:
            self.logger.debug("YouTube public page detection failed for %s: %s", channel.get("name", ""), exc)
            return None, False

    def _sample_until_next_check(self, stream: dict[str, Any]) -> None:
        deadline = time.monotonic() + self.config["check_interval"]
        while not self._stop.is_set() and time.monotonic() < deadline:
            for profile in list(self.config["profiles"]):
                session = self._ensure_session(profile, stream["url"])
                state = self._video_state(session, stream.get("videoId", ""))
                if state and state.get("playing"):
                    self._record_watch(profile, stream, 5.0)
                elif state and state.get("paused"):
                    self._resume_video(session)
                    event("playback_recovered", {"profile": profile, "url": stream["url"]})
            self._stop.wait(5)

    def _browser_binary(self) -> Path:
        configured = str(self.config.get("browser_path", "")).strip()
        if configured:
            try:
                if Path(configured).is_file():
                    return Path(configured)
            except OSError as exc:
                self.logger.debug("Configured browser path is unavailable: %s (%s)", configured, exc)
        candidates = []
        local = os.environ.get("LOCALAPPDATA", "")
        program_files = [os.environ.get("PROGRAMFILES", ""), os.environ.get("PROGRAMFILES(X86)", "")]
        if self.config["browser"] == "brave":
            candidates = [Path(root) / "BraveSoftware/Brave-Browser/Application/brave.exe" for root in [local, *program_files] if root]
        else:
            candidates = [Path(root) / "Google/Chrome/Application/chrome.exe" for root in [local, *program_files] if root]
        for candidate in candidates:
            try:
                if candidate.is_file():
                    return candidate
            except OSError as exc:
                self.logger.debug("Browser candidate is unavailable: %s (%s)", candidate, exc)
        raise FileNotFoundError("未找到 Chrome / Brave，请在设置中指定浏览器路径。")

    def _launch(self, profile: str, url: str, login: bool = False) -> BrowserSession:
        self._close_profile(profile)
        binary = self._browser_binary()
        profile_dir = (self.profiles_dir / normalize_profile(profile)).resolve()
        if profile_dir.parent != self.profiles_dir.resolve():
            raise ValueError("观看账号资料路径无效")
        profile_dir.mkdir(parents=True, exist_ok=True)
        port = free_local_port()
        args = [
            str(binary), f"--user-data-dir={profile_dir}", f"--remote-debugging-port={port}",
            "--remote-debugging-address=127.0.0.1", "--remote-allow-origins=*", "--no-first-run",
            "--no-default-browser-check", "--autoplay-policy=no-user-gesture-required",
        ]
        if self.proxy["enableProxy"]:
            args.extend([f"--proxy-server={self.proxy['proxyUrl']}", "--proxy-bypass-list=localhost;127.0.0.1;[::1]"])
        if self.config.get("mute", True) and not login:
            args.append("--mute-audio")
        if self.config.get("headless", False) and not login:
            args.extend(["--headless=new", "--window-size=1280,800"])
        args.append(url)
        flags = getattr(subprocess, "CREATE_NO_WINDOW", 0)
        process = subprocess.Popen(args, creationflags=flags)
        session = BrowserSession(profile, process, port, url)
        with self._lock:
            self.sessions[profile] = session
        self.last_browser_status = f"{profile} 已启动"
        event("browser", {"profile": profile, "status": "running", "url": url, "pid": process.pid})
        return session

    def _ensure_session(self, profile: str, url: str) -> BrowserSession:
        with self._lock:
            session = self.sessions.get(profile)
        if session and session.alive():
            if session.url != url:
                self._open_devtools_url(session, url)
                session.url = url
            return session
        return self._launch(profile, url)

    @staticmethod
    def _direct_session():
        import requests
        session = requests.Session()
        session.trust_env = False
        return session

    def _tabs(self, session: BrowserSession) -> list[dict[str, Any]]:
        try:
            response = self._direct_session().get(f"http://127.0.0.1:{session.port}/json", timeout=1)
            response.raise_for_status()
            value = response.json()
            return value if isinstance(value, list) else []
        except Exception:
            return []

    def _open_devtools_url(self, session: BrowserSession, url: str) -> None:
        tabs = self._tabs(session)
        if tabs:
            self._evaluate(tabs[0].get("webSocketDebuggerUrl", ""), f"location.href={json.dumps(url)}")

    def _video_state(self, session: BrowserSession, expected_id: str) -> dict[str, Any] | None:
        tabs = [tab for tab in self._tabs(session) if "youtube.com" in str(tab.get("url", ""))]
        if not tabs:
            return None
        expression = "(()=>{const v=document.querySelector('video');return v?{paused:v.paused,ended:v.ended,currentTime:v.currentTime,readyState:v.readyState,url:location.href}:null})()"
        result = self._evaluate(tabs[0].get("webSocketDebuggerUrl", ""), expression)
        if not isinstance(result, dict):
            return None
        current = str(result.get("url", ""))
        if expected_id and expected_id not in current:
            return None
        paused = bool(result.get("paused", True))
        ended = bool(result.get("ended", False))
        result["playing"] = not paused and not ended and int(result.get("readyState", 0)) >= 2
        return result

    def _resume_video(self, session: BrowserSession) -> None:
        tabs = self._tabs(session)
        if tabs:
            self._evaluate(tabs[0].get("webSocketDebuggerUrl", ""), "(()=>{const v=document.querySelector('video');if(v){v.play();return true}return false})()")

    @staticmethod
    def _evaluate(websocket_url: str, expression: str) -> Any:
        if not websocket_url:
            return None
        try:
            import websocket
            ws = websocket.create_connection(websocket_url, timeout=2, origin="http://127.0.0.1", http_proxy_host=None)
            try:
                ws.send(json.dumps({"id": 1, "method": "Runtime.evaluate", "params": {"expression": expression, "returnByValue": True}}))
                data = json.loads(ws.recv())
                return data.get("result", {}).get("result", {}).get("value")
            finally:
                ws.close()
        except Exception:
            return None

    def _record_watch(self, profile: str, stream: dict[str, Any], seconds: float) -> None:
        now = datetime.now().astimezone()
        date = now.date().isoformat()
        key = stream.get("videoId") or video_id(stream["url"]) or stream["url"]
        records = self.history.setdefault("records", {})
        row = records.setdefault(date, {}).setdefault(profile, {}).setdefault(key, {
            "title": stream.get("title", ""), "channel": stream.get("channel", ""),
            "url": stream["url"], "watch_seconds": 0.0, "last_watched_at": "",
        })
        row["watch_seconds"] = float(row.get("watch_seconds", 0.0)) + seconds
        row["last_watched_at"] = now.isoformat(timespec="seconds")
        atomic_write_json(self.history_path, self.history)
        event("watch_time", {"profile": profile, "seconds": row["watch_seconds"], "todayTotal": self._history_snapshot()["todayTotal"]})

    def _history_snapshot(self) -> dict[str, Any]:
        today = datetime.now().astimezone().date()
        accepted = {(today - timedelta(days=i)).isoformat() for i in range(7)}
        rows: list[dict[str, Any]] = []
        today_total = week_total = 0.0
        for date, profiles in self.history.get("records", {}).items():
            if date not in accepted or not isinstance(profiles, dict):
                continue
            for profile, videos in profiles.items():
                if not isinstance(videos, dict):
                    continue
                for identifier, value in videos.items():
                    if not isinstance(value, dict):
                        continue
                    seconds = float(value.get("watch_seconds", 0.0))
                    week_total += seconds
                    if date == today.isoformat():
                        today_total += seconds
                    rows.append({"date": date, "profile": profile, "videoId": identifier, **value})
        rows.sort(key=lambda row: (row.get("date", ""), row.get("last_watched_at", "")), reverse=True)
        return {"rows": rows, "todayTotal": today_total, "weekTotal": week_total}

    def _close_profile(self, profile: str) -> None:
        with self._lock:
            session = self.sessions.pop(profile, None)
        if not session or not session.alive():
            return
        try:
            session.process.terminate()
            session.process.wait(timeout=5)
        except Exception:
            try:
                session.process.kill()
            except Exception:
                pass

    def _close_all_browsers(self) -> None:
        with self._lock:
            profiles = list(self.sessions)
        for profile in profiles:
            self._close_profile(profile)


if __name__ == "__main__":
    raise SystemExit(run_worker(YouTubeWorker))
