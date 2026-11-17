"""Copy visual-only historical evidence and hashed image cache into a new isolated WPF review root."""
import argparse
import hashlib
import json
import shutil
from pathlib import Path

p = argparse.ArgumentParser()
p.add_argument("evidence", type=Path)
p.add_argument("output", type=Path)
p.add_argument("--cache", type=Path)
p.add_argument("--baseline", action="store_true")
args = p.parse_args()
if args.output.exists():
    raise SystemExit("Use a new isolated output directory")
snapshots = []
for path in args.evidence.rglob("state-*.json"):
    doc = json.loads(path.read_text(encoding="utf-8-sig"))
    state = doc.get("state", doc)
    if any(0 < r.get("currentMinutes", 0) < r.get("requiredMinutes", 0) for r in state.get("inventory", [])):
        snapshots.append((doc.get("receivedAt", ""), path, state))
if not snapshots:
    raise SystemExit("No official snapshot with partial progress")
_, source, state = max(snapshots, key=lambda r: r[0])
safe = {k: state[k] for k in ("coreAvailable", "campaigns", "inventory", "currentDrop", "currentChannel", "lastInventorySyncAt", "inventorySource") if k in state}
safe.update(running=False, historicalReplay=True, authState="logged_in", watchRequests={}, accounts=[{"userId": "历史官方数据 · 隔离回放", "loggedIn": True}])
args.output.mkdir(parents=True)
(args.output / "core-visual-state.json").write_text(json.dumps(safe, ensure_ascii=False), encoding="utf-8")
(args.output / ("density-test.flag" if args.baseline else "priority-state-test.flag")).write_text("Official history is never live viewing; all runtime scenarios are simulated.", encoding="utf-8")
copied = 0
if args.cache:
    dest = args.output / "isolated-data/cache/visual-images"
    dest.mkdir(parents=True)
    for image in args.cache.glob("*.img"):
        shutil.copy2(image, dest / image.name)
        copied += 1
report = {"source": source.name, "sourceSha256": hashlib.sha256(source.read_bytes()).hexdigest(), "campaigns": len(safe.get("campaigns", [])), "drops": len(safe["inventory"]), "cachedImagesCopied": copied, "authOrSessionsCopied": False, "runtimeEvidence": "simulated separately"}
(args.output / "replay-source.json").write_text(json.dumps(report, indent=2), encoding="utf-8")
print(json.dumps(report))
