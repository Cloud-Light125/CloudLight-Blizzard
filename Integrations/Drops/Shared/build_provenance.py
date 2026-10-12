"""Local build provenance. Hash only runtime source, never account data."""
from __future__ import annotations

import argparse
import hashlib
import importlib.metadata
import json
import platform
import subprocess
from datetime import datetime, timezone
from pathlib import Path


def git(root: Path, *args: str) -> str:
    result = subprocess.run(["git", "-C", str(root), *args], capture_output=True, text=True)
    return result.stdout.strip() if result.returncode == 0 else ""


def sources(root: Path) -> dict:
    # These are exactly the files copied by --add-data *.py;core.
    files = {p.name: hashlib.sha256(p.read_bytes()).hexdigest() for p in sorted(root.glob("*.py"))}
    digest = hashlib.sha256(json.dumps(files, sort_keys=True, separators=(",", ":")).encode()).hexdigest()
    return {"sha256": digest, "files": files}


def adapter_sources(repo: Path) -> dict:
    root = repo / 'Integrations' / 'Drops'
    files = {p.relative_to(root).as_posix(): hashlib.sha256(p.read_bytes()).hexdigest()
             for p in sorted(root.rglob('*.py')) if 'tests' not in p.parts and '__pycache__' not in p.parts}
    return {"sha256": hashlib.sha256(json.dumps(files, sort_keys=True, separators=(",", ":")).encode()).hexdigest(), "files": files}


def core_info(root: Path) -> dict:
    import ast
    def method(file: str, cls: str, member: str) -> bool:
        tree = ast.parse((root / file).read_text(encoding="utf-8-sig"))
        return any(isinstance(node, ast.ClassDef) and node.name == cls and
                   any(isinstance(m, (ast.FunctionDef, ast.AsyncFunctionDef)) and m.name == member for m in node.body)
                   for node in tree.body)
    model = (root / "models.py").read_text(encoding="utf-8-sig")
    capabilities = {
        "forceRefresh": method("miner.py", "SoopMiner", "force_refresh"),
        "forceRefreshAccount": method("multi_miner.py", "MultiMinerManager", "force_refresh_account"),
        "bindingProtection": all(s in model for s in ("ingameGiveYn", "itemType", "acctConn", "GameAccountRequirement", "GameAccountLinkState")),
        "remoteViewTime": all(s in model for s in ("viewTime", "view_time", "DropItem", "Mission")),
    }
    if not all(capabilities.values()):
        raise RuntimeError("SOOP Core capability version 1 is not satisfied: " + str(capabilities))
    return {"path": str(root.resolve()), "commit": git(root, "rev-parse", "HEAD") or None,
            "dirty": bool(git(root, "status", "--porcelain")), "capabilityVersion": 1,
            "capabilities": capabilities, "redistributionAuthorized": False, **sources(root)}


def main() -> None:
    parser = argparse.ArgumentParser()
    parser.add_argument("--repo", type=Path, required=True)
    parser.add_argument("--core", type=Path)
    parser.add_argument("--output", type=Path, required=True)
    parser.add_argument("--artifacts", type=Path)
    parser.add_argument("--purpose", choices=("Local", "Distribution"), default="Local")
    args = parser.parse_args()
    if args.purpose == "Distribution" and args.core:
        raise RuntimeError("SOOP redistribution permission is unconfirmed; only a local-use build may bundle this Core.")
    info = {"schema": 1, "protocol": 1, "purpose": args.purpose,
            "createdAt": datetime.now(timezone.utc).isoformat(),
            "commit": git(args.repo, "rev-parse", "HEAD"),
            "dirty": bool(git(args.repo, "status", "--porcelain")),
            "python": platform.python_version(),
            "dependencies": {d.metadata["Name"]: d.version for d in importlib.metadata.distributions()},
            "soopCore": core_info(args.core) if args.core else None,
            "adapterSources": adapter_sources(args.repo)}
    if args.artifacts:
        info["workers"] = {p.parent.name: hashlib.sha256(p.read_bytes()).hexdigest()
                           for p in sorted(args.artifacts.glob("*/*-worker.exe"))}
    args.output.parent.mkdir(parents=True, exist_ok=True)
    args.output.write_text(json.dumps(info, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")


if __name__ == "__main__":
    main()
