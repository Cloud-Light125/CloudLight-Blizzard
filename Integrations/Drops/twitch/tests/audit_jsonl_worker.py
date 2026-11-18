"""Offline-only subprocess fixture for real Host/JSONL/Core/UI integration.

Uses an isolated directory and deterministic official-transport substitutes.
It cannot authenticate or contact Twitch.
"""
from pathlib import Path
import argparse
import sys
import asyncio

sys.path.insert(0, str(Path(__file__).resolve().parents[1]))
from worker import TwitchWorker
from test_campaign_scheduling import campaign

parser = argparse.ArgumentParser()
parser.add_argument("--data-dir", type=Path, required=True)
parser.add_argument("--log-file", type=Path, required=True)
args = parser.parse_args()
worker = TwitchWorker(args.data_dir, args.log_file)
worker.on_proxy_changed = lambda: None
def blocked_transport(*args, **kwargs):
    raise RuntimeError("Offline fixture blocks all network and authentication")
worker._core["twitch"].Twitch.request = blocked_transport
worker._settings.auto_claim_drops = False
client = worker._core["twitch"].Twitch(worker._settings)
client.save = lambda **kw: None
client._cloudlight_inventory_loaded = True
worker._client = client
worker.running = True
worker._authenticated_user_id = 987654
remote = set()
mutations = []

async def fetch():
    client.inventory = [campaign(client, cid, current=60) for cid in ("a", "b")]
    for cam in client.inventory:
        drop = next(iter(cam.drops)); drop.claim_id = cam.id
        drop.is_claimed = cam.id in remote
    from datetime import datetime, timezone
    client._cloudlight_inventory_updated_at = datetime.now(timezone.utc).isoformat()

async def gql(op):
    if op.get("operationName") != "DropsPage_ClaimDropRewards":
        raise RuntimeError("Offline fixture blocks every real network request")
    cid = op["variables"]["input"]["dropInstanceID"]
    mutations.append(cid)
    (args.data_dir/"mutations.json").write_text(__import__("json").dumps(mutations), encoding="utf-8")
    remote.add(cid)
    await asyncio.sleep(.01)
    return {"data": {"claimDropRewards": {"status": "ELIGIBLE_FOR_ALL"}}}

client.fetch_inventory = fetch
client.gql_request = gql
client.get_auth = lambda: (_ for _ in ()).throw(RuntimeError("Offline fixture blocks authentication"))
client.request = blocked_transport
worker._submit(fetch())
try:
    worker.run()
finally:
    if worker._loop.is_running():
        worker._loop.call_soon_threadsafe(worker._loop.stop)
        worker._loop_thread.join(5)
