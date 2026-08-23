from __future__ import annotations

import hashlib
import json
import platform
import sys
from pathlib import Path

import pefile


AMD64 = 0x8664


def imported_dlls(path: Path) -> list[str]:
    pe = pefile.PE(str(path), fast_load=True)
    try:
        if pe.FILE_HEADER.Machine != AMD64 or pe.OPTIONAL_HEADER.Magic != 0x20B:
            raise RuntimeError(f"{path.name} is not an x64 PE image")
        pe.parse_data_directories(
            directories=[pefile.DIRECTORY_ENTRY["IMAGE_DIRECTORY_ENTRY_IMPORT"]]
        )
        return sorted({entry.dll.decode("ascii") for entry in getattr(pe, "DIRECTORY_ENTRY_IMPORT", [])})
    finally:
        pe.close()


def file_record(path: Path) -> dict[str, object]:
    return {
        "name": path.name,
        "path": str(path),
        "size": path.stat().st_size,
        "sha256": hashlib.sha256(path.read_bytes()).hexdigest(),
        "imports": imported_dlls(path),
    }


def main() -> int:
    import _ssl
    import ssl

    ssl.create_default_context()
    extension = Path(_ssl.__file__).resolve()
    roots = list(dict.fromkeys([
        extension.parent,
        Path(sys.base_prefix) / "Library" / "bin",
        Path(sys.base_prefix) / "DLLs",
        Path(sys.prefix) / "Library" / "bin",
        Path(sys.prefix) / "DLLs",
    ]))
    extension_imports = imported_dlls(extension)
    required_names = [
        name for name in extension_imports
        if name.casefold().startswith(("libssl-", "libcrypto-"))
    ]
    if len(required_names) != 2:
        raise RuntimeError(
            f"Expected _ssl.pyd to import one libssl and one libcrypto DLL; found {required_names}"
        )

    binaries: list[Path] = []
    for name in required_names:
        matches = [root / name for root in roots if (root / name).is_file()]
        if not matches:
            raise FileNotFoundError(f"Cannot resolve the exact _ssl.pyd dependency: {name}")
        binaries.append(matches[0].resolve())

    records = [file_record(path) for path in binaries]
    crypto_names = {path.name.casefold() for path in binaries if path.name.casefold().startswith("libcrypto-")}
    for record in records:
        if str(record["name"]).casefold().startswith("libssl-"):
            imported_crypto = {
                name.casefold() for name in record["imports"]
                if name.casefold().startswith("libcrypto-")
            }
            if imported_crypto != crypto_names:
                raise RuntimeError(
                    f"libssl/libcrypto mismatch: {record['name']} imports {sorted(imported_crypto)}, "
                    f"resolved {sorted(crypto_names)}"
                )

    support_names = ("libexpat.dll", "libmpdec-4.dll", "liblzma.dll", "ffi.dll", "sqlite3.dll")
    support_binaries: list[Path] = []
    for name in support_names:
        matches = [root / name for root in roots if (root / name).is_file()]
        if not matches:
            raise FileNotFoundError(f"Cannot resolve the Conda runtime dependency: {name}")
        support_binaries.append(matches[0].resolve())

    print(json.dumps({
        "python": sys.version.split()[0],
        "machine": platform.machine(),
        "openssl": ssl.OPENSSL_VERSION,
        "ssl_extension": file_record(extension),
        "binaries": records,
        "support_binaries": [file_record(path) for path in support_binaries],
    }, ensure_ascii=False))
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
