"""Small Windows CurrentUser DPAPI bridge used by the Bilibili worker.

The worker never writes a plaintext cookie.  The WPF process and this module
use the same CurrentUser scope and entropy so the encrypted blob can be
transferred through a local file without putting credentials in settings,
arguments, logs, or protocol responses.
"""

from __future__ import annotations

import base64
import ctypes
import os
from ctypes import wintypes
from pathlib import Path


_ENTROPY = b"CloudLight Blizzard:BilibiliCredential:v1"
_CRYPTPROTECT_UI_FORBIDDEN = 0x1


class _DataBlob(ctypes.Structure):
    _fields_ = [("cbData", wintypes.DWORD), ("pbData", ctypes.POINTER(ctypes.c_ubyte))]


def _blob(value: bytes) -> tuple[_DataBlob, ctypes.Array[ctypes.c_ubyte] | None]:
    if not value:
        return _DataBlob(0, None), None
    buffer = (ctypes.c_ubyte * len(value)).from_buffer_copy(value)
    return _DataBlob(len(value), ctypes.cast(buffer, ctypes.POINTER(ctypes.c_ubyte))), buffer


def _crypt(value: bytes, *, protect: bool) -> bytes:
    if os.name != "nt":
        raise RuntimeError("Windows DPAPI is required for Bilibili credentials")
    crypt32 = ctypes.windll.crypt32
    kernel32 = ctypes.windll.kernel32
    input_blob, input_buffer = _blob(value)
    entropy_blob, entropy_buffer = _blob(_ENTROPY)
    output_blob = _DataBlob()
    function = crypt32.CryptProtectData if protect else crypt32.CryptUnprotectData
    function.argtypes = [
        ctypes.POINTER(_DataBlob), ctypes.c_wchar_p, ctypes.POINTER(_DataBlob),
        ctypes.c_void_p, ctypes.c_void_p, wintypes.DWORD, ctypes.POINTER(_DataBlob),
    ]
    function.restype = wintypes.BOOL
    if not function(
        ctypes.byref(input_blob), "CloudLight Blizzard Bilibili credential",
        ctypes.byref(entropy_blob), None, None,
        _CRYPTPROTECT_UI_FORBIDDEN, ctypes.byref(output_blob),
    ):
        raise ctypes.WinError()
    try:
        return ctypes.string_at(output_blob.pbData, output_blob.cbData)
    finally:
        if output_blob.pbData:
            kernel32.LocalFree(output_blob.pbData)
        # Keep these references alive through the native call.
        _ = input_buffer, entropy_buffer


def protect(value: str) -> str:
    return base64.b64encode(_crypt(value.encode("utf-8"), protect=True)).decode("ascii")


def unprotect(value: str) -> str:
    decoded = base64.b64decode(value.encode("ascii"), validate=True)
    return _crypt(decoded, protect=False).decode("utf-8")


def write_protected(path: Path, value: str) -> None:
    encoded = protect(value)
    path.parent.mkdir(parents=True, exist_ok=True)
    temporary = path.with_name(path.name + ".tmp")
    temporary.write_text(encoded + "\n", encoding="ascii")
    os.replace(temporary, path)


def read_protected(path: Path) -> str | None:
    if not path.exists():
        return None
    encoded = path.read_text(encoding="ascii").strip()
    if not encoded:
        return None
    return unprotect(encoded)
