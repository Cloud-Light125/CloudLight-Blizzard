from __future__ import annotations

import importlib.util
import logging
import sys
import tempfile
import types
import threading
import unittest
from pathlib import Path
from unittest.mock import patch


YOUTUBE_DIR = Path(__file__).resolve().parents[1]
DROPS_DIR = YOUTUBE_DIR.parent
if str(DROPS_DIR) not in sys.path:
    sys.path.insert(0, str(DROPS_DIR))

spec = importlib.util.spec_from_file_location("cloudlight_youtube_worker", YOUTUBE_DIR / "worker.py")
assert spec is not None and spec.loader is not None
youtube_worker = importlib.util.module_from_spec(spec)
spec.loader.exec_module(youtube_worker)
YouTubeWorker = youtube_worker.YouTubeWorker


class _NotLiveYoutubeDL:
    def __init__(self, options) -> None:
        self.options = options

    def __enter__(self):
        return self

    def __exit__(self, exc_type, exc, traceback) -> None:
        return None

    def extract_info(self, url: str, download: bool = False):
        raise RuntimeError("ERROR: The channel is not currently live")


class YouTubeWorkerContractTests(unittest.TestCase):
    def test_logout_during_browser_launch_cannot_recreate_stopped_profile(self) -> None:
        with tempfile.TemporaryDirectory() as temporary:
            root = Path(temporary)
            worker = YouTubeWorker(root / "data", root / "youtube.log")
            entered, release, closed = threading.Event(), threading.Event(), threading.Event()
            worker._stop.clear()
            process = types.SimpleNamespace(pid=12345, poll=lambda: None)
            errors = []
            def popen(*args, **kwargs):
                entered.set()
                if not release.wait(3): raise TimeoutError("test launch gate")
                return process
            def launch():
                try: worker._ensure_session("主账号", "https://www.youtube.com/")
                except Exception as exc: errors.append(exc)
            try:
                with patch.object(worker, '_browser_binary', return_value=Path(__file__)), \
                     patch.object(youtube_worker.subprocess, 'Popen', side_effect=popen), \
                     patch.object(worker, '_close_session', side_effect=lambda session: closed.set() if session else None):
                    opening = threading.Thread(target=launch)
                    opening.start(); self.assertTrue(entered.wait(2))
                    stopping = threading.Thread(target=lambda: worker.logout({'profile':'主账号'}))
                    stopping.start(); release.set()
                    opening.join(3); stopping.join(3)
                    self.assertFalse(opening.is_alive() or stopping.is_alive())
                    self.assertEqual(errors, [])
                    self.assertTrue(closed.is_set())
                    self.assertNotIn('主账号', worker.sessions)
                    self.assertIsNone(worker._ensure_session('主账号', 'https://www.youtube.com/'))
                    worker.running = True
                    worker.current_stream = {'url':'https://www.youtube.com/'}
                    self.assertEqual(worker._state()['status'], '等待观看账号')
            finally:
                release.set()
                self._close(worker)
    @staticmethod
    def _close(worker: YouTubeWorker) -> None:
        for handler in list(worker.logger.handlers):
            worker.logger.removeHandler(handler)
            handler.close()

    def test_channel_not_live_is_normal_scan_result(self) -> None:
        with tempfile.TemporaryDirectory() as temporary:
            root = Path(temporary)
            worker = YouTubeWorker(root / "data", root / "youtube.log")
            try:
                worker.config["channels"] = [{
                    "name": "Overwatch Esports",
                    "id": "UCiAInBL9kUzz1XRxk66v-gw",
                    "url": "https://www.youtube.com/channel/UCiAInBL9kUzz1XRxk66v-gw/live",
                    "enabled": True,
                }]
                worker._detect_single_public = lambda channel, url: (None, True)
                fake_yt_dlp = types.SimpleNamespace(YoutubeDL=_NotLiveYoutubeDL)
                with patch.dict(sys.modules, {"yt_dlp": fake_yt_dlp}):
                    self.assertIsNone(worker._detect_stream())

                log_text = (root / "youtube.log").read_text(encoding="utf-8")
                self.assertNotIn("WARNING", log_text)
                self.assertNotIn("not currently live", log_text)
                self.assertFalse(worker._last_scan_failed)
            finally:
                self._close(worker)


if __name__ == "__main__":
    unittest.main()
