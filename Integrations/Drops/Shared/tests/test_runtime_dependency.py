from __future__ import annotations

import tempfile
import sys
import unittest
from pathlib import Path
from unittest.mock import patch

DROPS_DIR = Path(__file__).resolve().parents[2]
if str(DROPS_DIR) not in sys.path:
    sys.path.insert(0, str(DROPS_DIR))

from Shared.protocol import RuntimeDependencyError, WorkerBase, parse_protocol_request


class _HttpsWorker(WorkerBase):
    platform = "test"


class RuntimeDependencyTests(unittest.TestCase):
    def test_protocol_accepts_windows_powershell_utf8_bom(self) -> None:
        request = parse_protocol_request(
            '\ufeff{"id":"ssl-selftest","command":"ssl_check","payload":{}}\n')
        self.assertEqual(request["id"], "ssl-selftest")
        self.assertEqual(request["command"], "ssl_check")

    def test_ssl_import_failure_is_structured_non_retryable_and_logged_once(self) -> None:
        failure = RuntimeDependencyError(
            "ssl", "ssl_runtime_unavailable",
            "Python SSL 组件无法加载。", "DLL load failed while importing _ssl",
        )
        emitted: list[tuple[str, dict[str, object]]] = []
        with tempfile.TemporaryDirectory() as temporary, \
             patch("Shared.protocol.check_ssl_runtime", side_effect=failure), \
             patch("Shared.protocol.event", side_effect=lambda name, payload: emitted.append((name, payload))):
            root = Path(temporary)
            worker = _HttpsWorker(root / "data", root / "worker.log")
            try:
                for _ in range(2):
                    with self.assertRaises(RuntimeDependencyError):
                        worker.require_runtime()
                runtime_events = [payload for name, payload in emitted if name == "runtime_error"]
                self.assertGreaterEqual(len(runtime_events), 1)
                self.assertTrue(all(payload["code"] == "ssl_runtime_unavailable" for payload in runtime_events))
                self.assertTrue(all(payload["retryable"] is False for payload in runtime_events))
                log_text = (root / "worker.log").read_text(encoding="utf-8")
                self.assertEqual(log_text.count("ERROR"), 1)
                self.assertNotIn("Traceback", log_text)
            finally:
                for handler in list(worker.logger.handlers):
                    worker.logger.removeHandler(handler)
                    handler.close()


if __name__ == "__main__":
    unittest.main()
