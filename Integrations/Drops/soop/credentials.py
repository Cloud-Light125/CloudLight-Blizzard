"""Windows CurrentUser DPAPI storage for SOOP login credentials."""

from __future__ import annotations

import base64
import ctypes
import json
import os
from ctypes import wintypes
from pathlib import Path


_ENTROPY = b"CloudLight Blizzard:SoopCredential:v1"
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
        raise RuntimeError("Windows DPAPI is required for SOOP credentials")
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
        ctypes.byref(input_blob), "CloudLight Blizzard SOOP credential",
        ctypes.byref(entropy_blob), None, None,
        _CRYPTPROTECT_UI_FORBIDDEN, ctypes.byref(output_blob),
    ):
        raise ctypes.WinError()
    try:
        return ctypes.string_at(output_blob.pbData, output_blob.cbData)
    finally:
        if output_blob.pbData:
            kernel32.LocalFree(output_blob.pbData)
        _ = input_buffer, entropy_buffer


def write_credentials(path: Path, userid: str, password: str) -> None:
    raw = json.dumps({"userid": userid, "password": password}, ensure_ascii=False).encode("utf-8")
    encoded = base64.b64encode(_crypt(raw, protect=True)).decode("ascii")
    path.parent.mkdir(parents=True, exist_ok=True)
    temporary = path.with_name(path.name + ".tmp")
    temporary.write_text(encoded + "\n", encoding="ascii")
    os.replace(temporary, path)


def read_credentials(path: Path) -> tuple[str, str] | None:
    if not path.is_file():
        return None
    encoded = path.read_text(encoding="ascii").strip()
    if not encoded:
        return None
    raw = _crypt(base64.b64decode(encoded.encode("ascii"), validate=True), protect=False)
    value = json.loads(raw.decode("utf-8"))
    if not isinstance(value, dict):
        return None
    userid = str(value.get("userid", "")).strip()
    password = str(value.get("password", ""))
    return (userid, password) if userid and password else None
