"""Read official inventory using an isolated copy of an existing Session; never watch/claim."""
from __future__ import annotations
import argparse
import asyncio
import json
import logging
import shutil
import sys
import tempfile
import hashlib
from datetime import datetime, timezone
from pathlib import Path

parser=argparse.ArgumentParser()
parser.add_argument("--data-dir",type=Path,required=True)
parser.add_argument("--app-settings",type=Path,required=True)
parser.add_argument("--output",type=Path,required=True)
parser.add_argument("--snapshot",type=Path)
args=parser.parse_args()
root=Path(__file__).resolve().parents[1]
sys.path.insert(0,str(root/"Integrations/Drops/twitch"))
from worker import TwitchWorker

report={"checkedAt":datetime.now(timezone.utc).isoformat(),"claimsSubmitted":0,"watchRequests":0}
session=args.data_dir/"cookies.jar"
original_hash=hashlib.sha256(session.read_bytes()).hexdigest() if session.is_file() else ""
with tempfile.TemporaryDirectory(prefix="cloudlight-twitch-readonly-") as temporary:
    private=Path(temporary)
    worker=TwitchWorker(private/private.name,private/"probe.log")
    try:
        if not (args.data_dir/"cookies.jar").is_file():
            raise RuntimeError("no_saved_session")
        shutil.copy2(args.data_dir/"cookies.jar",worker.cookies_path)
        settings=json.loads(args.app_settings.read_text(encoding="utf-8-sig"))
        from yarl import URL
        enabled=settings.get("EnableProxy",False) and settings.get("TwitchUseProxy",True)
        object.__setattr__(worker._settings,"_cloudlight_global_proxy",URL(settings.get("ProxyUrl","") if enabled else ""))
        object.__setattr__(worker._settings,"_cloudlight_fallback_direct",settings.get("FallbackDirect",False))
        worker._settings.auto_claim_drops=False
        async def read_only():
            client=worker._core["twitch"].Twitch(worker._settings)
            client._cloudlight_auto_start=True
            original=client.gql_request
            from network_policy import replayable
            async def query_only(ops):
                if not replayable("POST","https://gql.twitch.tv/gql",{"json":ops}):
                    raise RuntimeError("mutation_blocked_by_readonly_probe")
                return await original(ops)
            client.gql_request=query_only
            try:
                async with asyncio.timeout(65):
                    await client.get_auth()
                    await client.fetch_inventory()
                campaigns=[]
                for campaign in client.inventory:
                    if "overwatch" not in campaign.game.name.casefold():continue
                    campaigns.append({"id":campaign.id,"gameId":str(campaign.game.id),"name":campaign.name,"active":campaign.active,"eligible":campaign.eligible,
                        "endsAt":campaign.ends_at.isoformat(),"drops":[{"name":d.name,"currentMinutes":d.real_current_minutes,
                        "id":d.id,"benefits":[{"id":b.id,"name":b.name} for b in d.benefits],
                        "requiredMinutes":d.required_minutes,"claimed":d.is_claimed,"canClaim":d.can_claim} for d in campaign.drops]})
                if args.snapshot:
                    worker._client=client
                    state=worker.load_state({})
                    state.pop("accounts",None)
                    args.snapshot.parent.mkdir(parents=True,exist_ok=True)
                    args.snapshot.write_text(json.dumps(state,ensure_ascii=False,indent=2),encoding="utf-8")
                    worker._client=None
                return {"status":"official_inventory_read","campaigns":campaigns}
            finally:
                await client.shutdown()
        report.update(worker._submit(read_only(),timeout=75))
    except BaseException as exc:
        report.update(status="not_confirmed",errorType=type(exc).__name__)
    finally:
        worker._loop.call_soon_threadsafe(worker._loop.stop)
        worker._loop_thread.join(timeout=5)
        for handler in list(worker.logger.handlers):
            worker.logger.removeHandler(handler);logging.getLogger("TwitchDrops").removeHandler(handler);handler.close()
args.output.parent.mkdir(parents=True,exist_ok=True)
report["originalSessionUnchanged"]=bool(original_hash) and hashlib.sha256(session.read_bytes()).hexdigest()==original_hash
args.output.write_text(json.dumps(report,ensure_ascii=False,indent=2),encoding="utf-8")
print(json.dumps(report,ensure_ascii=False))
