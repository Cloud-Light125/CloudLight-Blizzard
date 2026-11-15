"""Twitch settings only: validated recovery, durable writes and stale-writer rejection.

No cookie/cache files are read or changed here. Legacy utils.json_load remains
unchanged for other Core consumers. Failure messages contain no JSON values.
"""
from __future__ import annotations

import copy
import hashlib
import json
import os
import stat
import tempfile
import threading
import time
from contextlib import contextmanager
from pathlib import Path

from yarl import URL
from constants import PriorityMode
from utils import _serialize


class SettingsError(RuntimeError):
    def __init__(self, state: str, path: Path, detail: str = ""):
        self.state, self.path = state, path
        super().__init__(f"Twitch 配置异常 [{state}]：{path.name}。{detail}")


_locks: dict[str, threading.RLock] = {}
_guard = threading.Lock()


def _regular(path: Path):
    try:
        info = path.lstat()
    except FileNotFoundError:
        return None
    if (not stat.S_ISREG(info.st_mode) or info.st_nlink != 1 or
            getattr(info, 'st_file_attributes', 0) & 0x400):
        raise SettingsError('Unreadable', path, '拒绝符号链接、重解析点或非独占普通文件。')
    return info


def read_bytes(path: Path) -> bytes | None:
    info = _regular(path)
    if info is None:
        return None
    with path.open('rb') as stream:
        opened = os.fstat(stream.fileno())
        if (opened.st_ino, opened.st_dev) != (info.st_ino, info.st_dev):
            raise SettingsError('Unreadable', path, '文件在读取期间已更换。')
        return stream.read()


class FileLease:
    """OS lock released on crash; the lock file itself is never deleted."""
    def __init__(self, path: Path):
        self.path, self.stream = path, None

    def acquire(self):
        _regular(self.path)
        stream = self.path.open('a+b')
        try:
            if os.fstat(stream.fileno()).st_nlink != 1:
                raise OSError('lock ownership')
            stream.seek(0, os.SEEK_END)
            if not stream.tell():
                stream.write(b'0')
                stream.flush()
            stream.seek(0)
            if os.name == 'nt':
                import msvcrt
                msvcrt.locking(stream.fileno(), msvcrt.LK_NBLCK, 1)
            else:
                import fcntl
                fcntl.flock(stream, fcntl.LOCK_EX | fcntl.LOCK_NB)
        except Exception:
            stream.close()
            raise
        self.stream = stream
        return self

    def close(self):
        if self.stream is not None:
            self.stream.close()
            self.stream = None


class SettingsStore:
    def __init__(self, path: Path, defaults: dict):
        self.path = path
        self.defaults = copy.deepcopy(defaults)
        # Missing/legacy unknown claim preference is never consent to claim.
        self.defaults['auto_claim_drops'] = False
        self.state = 'Missing'
        self.recovered_from = ''
        self.revision = None
        with _guard:
            self.lock = _locks.setdefault(os.path.normcase(str(path.resolve())), threading.RLock())

    @contextmanager
    def transaction(self):
        with self.lock:
            lease = FileLease(self.path.with_name(self.path.name + '.lock'))
            try:
                deadline = time.monotonic() + 2
                while True:
                    try:
                        lease.acquire()
                        break
                    except OSError:
                        if time.monotonic() >= deadline:
                            raise SettingsError('Unreadable', self.path, '配置正被其他进程使用或无法访问。') from None
                        time.sleep(.025)
                yield
            except OSError as exc:
                raise SettingsError('Unreadable', self.path, type(exc).__name__) from None
            finally:
                lease.close()

    def decode(self, data: bytes) -> tuple[dict, bool]:
        def unique(pairs):
            result = {}
            for key, value in pairs:
                if key in result:
                    raise ValueError('duplicate key')
                result[key] = value
            return result
        try:
            raw = json.loads(data.decode('utf-8-sig'), object_pairs_hook=unique,
                             parse_constant=lambda _: (_ for _ in ()).throw(ValueError()))
        except (ValueError, UnicodeError):
            raise SettingsError('InvalidJson', self.path, 'JSON无效；原文件保留，登录资料未清除。') from None
        if not isinstance(raw, dict):
            raise SettingsError('InvalidSchema', self.path, '配置根节点必须是对象。')
        result = copy.deepcopy(raw)
        legacy = False
        for key, default in self.defaults.items():
            if key not in raw:
                result[key] = copy.deepcopy(default)
                legacy = True
                continue
            value = raw[key]
            try:
                if key == 'proxy':
                    if isinstance(value, dict) and value.get('__type') == 'URL' and isinstance(value.get('data'), str):
                        value = URL(value['data'])
                    elif isinstance(value, str):
                        value, legacy = URL(value), True
                elif key == 'exclude':
                    if isinstance(value, dict) and value.get('__type') == 'set':
                        value = value.get('data')
                    if isinstance(value, list) and all(isinstance(v, str) for v in value):
                        value = set(value)
                elif key == 'priority_mode':
                    if isinstance(value, dict) and value.get('__type') == 'PriorityMode':
                        if type(value.get('data')) is not int:
                            raise ValueError()
                        value = PriorityMode(value['data'])
                    elif isinstance(value, str):
                        value, legacy = PriorityMode[value.upper()], True
                if type(value) is not type(default):
                    raise ValueError()
                if key == 'priority' and not all(isinstance(v, str) for v in value):
                    raise ValueError()
                if key == 'connection_quality' and not 1 <= value <= 6:
                    raise ValueError()
                if key == 'language' and not value.strip():
                    raise ValueError()
                if key == 'proxy' and value and (value.scheme not in {'http', 'https', 'socks5', 'socks5h'} or not value.host):
                    raise ValueError()
            except (ValueError, TypeError, KeyError):
                raise SettingsError('InvalidSchema', self.path, f'字段 {key} 类型或取值无效；不会静默覆盖。') from None
            result[key] = value
        return result, legacy

    def encode(self, values: dict) -> bytes:
        try:
            data = json.dumps(values, default=_serialize, sort_keys=True, indent=4, allow_nan=False).encode('utf-8')
        except (ValueError, TypeError):
            raise SettingsError('InvalidSchema', self.path, '设置无法序列化。') from None
        self.decode(data)
        return data

    def _read(self, path: Path):
        try:
            return read_bytes(path)
        except OSError as exc:
            raise SettingsError('Unreadable', path, type(exc).__name__) from None

    @staticmethod
    def _revision(data):
        return hashlib.sha256(data).hexdigest() if data is not None else None

    def _write(self, target: Path, data: bytes):
        _regular(target)
        fd, name = tempfile.mkstemp(prefix=target.name + '.', suffix='.tmp', dir=target.parent)
        temporary = Path(name)
        # On failure preserve our unique temporary file for diagnostics/recovery.
        with os.fdopen(fd, 'wb') as stream:
            stream.write(data)
            stream.flush()
            os.fsync(stream.fileno())
        if read_bytes(temporary) != data:
            raise SettingsError('RecoveryFailed', target, '临时文件校验失败。')
        self.decode(data)
        os.replace(temporary, target)
        if read_bytes(target) != data:
            raise SettingsError('RecoveryFailed', target, '替换后校验失败。')

    def preserve(self, data: bytes):
        fd, name = tempfile.mkstemp(prefix=self.path.name + '.corrupt-', suffix='.original', dir=self.path.parent)
        with os.fdopen(fd, 'wb') as stream:
            stream.write(data)
            stream.flush()
            os.fsync(stream.fileno())
        if read_bytes(Path(name)) != data:
            raise SettingsError('RecoveryFailed', self.path, '原始证据备份校验失败；未重置。')
        return Path(name)

    def load(self) -> dict:
        with self.transaction():
            primary = self._read(self.path)
            failure = None
            if primary is not None:
                try:
                    result, legacy = self.decode(primary)
                except SettingsError as exc:
                    failure = exc
                else:
                    self.revision = self._revision(primary)
                    self.state = 'LegacyCompatible' if legacy else 'Valid'
                    return result
            for suffix in ('.lkg', '.bak', '.new'):
                source = self.path.with_name(self.path.name + suffix)
                try:
                    backup = self._read(source)
                    if backup is None:
                        continue
                    result, _ = self.decode(backup)
                except SettingsError:
                    # Invalid candidates are retained, never unlinked.
                    continue
                self.state = 'RecoveryAvailable'
                try:
                    if primary is not None:
                        self.preserve(primary)
                    self._write(self.path, backup)
                    self._write(self.path.with_name(self.path.name + '.lkg'), backup)
                except (OSError, SettingsError):
                    raise SettingsError('RecoveryFailed', self.path, '可信副本恢复失败；原始文件及备份已保留。') from None
                self.revision = self._revision(backup)
                self.recovered_from = source.name
                return result
            if failure:
                self.state = failure.state
                raise failure
            self.revision = None
            self.state = 'Missing'
            return copy.deepcopy(self.defaults)

    def save(self, values: dict):
        data = self.encode(values)  # serialize/validate before touching files
        with self.transaction():
            previous = self._read(self.path)
            if self._revision(previous) != self.revision:
                raise SettingsError('RecoveryFailed', self.path, '文件已由其他设置对象或进程更改，请重新检测；本次未覆盖。')
            if previous is not None:
                self.decode(previous)
            try:
                if previous is not None:
                    self._write(self.path.with_name(self.path.name + '.bak'), previous)
                self._write(self.path, data)
                self.revision = self._revision(data)
                self._write(self.path.with_name(self.path.name + '.lkg'), data)
            except (OSError, SettingsError):
                raise SettingsError('RecoveryFailed', self.path, '保存未完整完成；旧配置/有效备份及临时证据保留，请重新检测。') from None

    def reset_defaults(self):
        with self.transaction():
            original = self._read(self.path)
            if original is None:
                raise SettingsError('RecoveryFailed', self.path, '没有需要重置的损坏文件。')
            try:
                self.decode(original)
            except SettingsError:
                pass
            else:
                raise SettingsError('RecoveryFailed', self.path, '配置有效，拒绝重置。')
            self.preserve(original)
            data = self.encode(self.defaults)
            try:
                self._write(self.path, data)
                self._write(self.path.with_name(self.path.name + '.lkg'), data)
            except (OSError, SettingsError):
                raise SettingsError('RecoveryFailed', self.path, '重置失败；原始证据已保留。') from None
