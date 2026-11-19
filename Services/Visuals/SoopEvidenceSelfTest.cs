using System.Text.Json;
using CloudLightBlizzard.Services.Drops;
using CloudLightBlizzard.ViewModels;
using CloudLightBlizzard.ViewModels.Visuals;

namespace CloudLightBlizzard.Services.Visuals;

internal static class SoopEvidenceSelfTest
{
    internal static JsonElement Account(string uid="A",string authentication="verified",bool running=true,string room="room-A",string heartbeat="success",string official="synced",int minutes=247,int ageSeconds=0)
    {
        var at=DateTimeOffset.UtcNow.AddSeconds(-ageSeconds).ToString("o");
        return JsonSerializer.SerializeToElement(new {
            uid,authenticationState=authentication,loggedIn=authentication=="verified",running,
            channelId=room,channelName="隔离模拟直播间",broadcastNo="1",bridgeConnected=room.Length>0,connectionHealthy=room.Length>0,
            queueTarget=room.Length > 0 ? new {campaignId="task",channelId=room,campaignTitle="守望先锋 · Stage 3"} : null,
            queueDiagnostics=running && room.Length == 0 ? new {reason="no_online_channel",message="已检查的 6 位指定主播当前均未开播",candidateActivities=1,totalActivities=427,activitiesChecked=1,onlineChannels=0,officialProbes=6} : null,
            heartbeatEvidence=new {status=heartbeat,uid,channelId=room,broadcastNo="1",campaignId="task",lastSuccessAt=heartbeat=="not_sent"?"":at,requestExpired=false},
            officialEvidence=new {uid,status=official,syncedAt=official=="unread"?"":at,values=new Dictionary<string,int>{{"task|cycle|tier|360",minutes}},campaignStates=new Dictionary<string,string>{{"task",official}}},
            currentProgress=new[]{new {account=uid,id="tier",campaignId="task",reward="SIMULATED · 官方分钟回放",currentMinutes=minutes,requiredMinutes=360,watchEligible=true}}
        });
    }
    internal static void Run(Action<bool,string> check)
    {
        var health=new DropsPlatformViewModel(DropsPlatform.Soop,"SOOP"){Running=true,WorkerLifecycle=WorkerLifecycle.Running};
        var model=new PlatformOverviewModel(health);
        void Apply(JsonElement state)=>model.UpdateAccount(state);
        AccountVisual A()=>model.Accounts.Single(a=>a.Id=="A");
        Apply(Account(authentication:"unverified"));
        check(model.AccountCount=="0"&&!A().HasWatchEvidence&&A().WorkerText.Contains("运行")&&A().Evidence!.WatchText.Contains("等待认证"),"SOOP evidence 01: unverified + running Worker stays waiting for authentication");
        Apply(Account(running:false));
        check(model.AccountCount=="1"&&!A().HasWatchEvidence&&A().WaitReason=="账号观看任务尚未启动","SOOP evidence 02: verified + stopped session retains login independently");
        Apply(Account(room:"",heartbeat:"not_sent"));
        check(!A().HasWatchEvidence&&A().ChannelText=="直播间：未连接","SOOP evidence 03: Worker without channel waits for eligible room");
        Apply(Account(heartbeat:"not_sent"));
        check(!A().HasWatchEvidence&&A().HeartbeatText=="观看心跳：未成功","SOOP evidence 04: channel connection alone never certifies heartbeat");
        Apply(Account(official:"unchanged"));
        check(A().HasWatchEvidence&&!A().OfficialGrowth&&A().OfficialText=="官方进度：未增长","SOOP evidence 05: successful heartbeat and unchanged official minutes are independent");
        Apply(Account(official:"grown",minutes:248));
        check(A().OfficialGrowth&&A().OfficialText.Contains("已增长")&&A().Reward?.Current==248,"SOOP evidence 06: correlated official growth is shown without local minute accumulation");
        Apply(Account(heartbeat:"timeout"));
        check(!A().HasWatchEvidence&&A().HeartbeatText.Contains("超时"),"SOOP evidence 07: timeout immediately invalidates current heartbeat");
        health.WorkerLifecycle=WorkerLifecycle.Crashed;
        model.RefreshClock();
        check(!A().HasWatchEvidence&&A().WorkerText.Contains("异常退出"),"SOOP evidence 08: Worker crash invalidates success evidence");
        health.WorkerLifecycle=WorkerLifecycle.Running;
        var old=Account();
        var fields=old.EnumerateObject().ToDictionary(p=>p.Name,p=>p.Value.Clone());
        fields["channelId"]=JsonSerializer.SerializeToElement("new-room");
        Apply(JsonSerializer.SerializeToElement(fields));
        check(!A().HasWatchEvidence,"SOOP evidence 09: delayed old-room heartbeat cannot certify the new channel");
        Apply(Account("B",heartbeat:"failure",minutes:12));
        Apply(Account(minutes:248));
        check(A().HasWatchEvidence&&!model.Accounts.Single(a=>a.Id=="B").HasWatchEvidence&&model.Accounts.Single(a=>a.Id=="B").Reward?.Current==12,"SOOP evidence 10: UID A/B have separate watch status and official values");
        health.ConnectionState=DropsConnectionState.Recovering;
        check(model.Accounts.All(a=>!a.HasWatchEvidence),"SOOP evidence 11: reconnect invalidates prior success until a new correlated event");
        health.ConnectionState=DropsConnectionState.Connected;
        Apply(Account(ageSeconds:301));
        check(!A().HasWatchEvidence&&!A().OfficialGrowth&&A().OfficialText.Contains("过期")&&A().Reward?.Current==247,"SOOP evidence 12: stale official data retains minutes with explicit expiry");
        Apply(Account(official:"failed"));
        Apply(JsonSerializer.SerializeToElement(new {uid="A",officialEvidence=new {uid="A",status="failed",error="offline"},currentProgress=Array.Empty<object>()}));
        check(A().Reward?.Current==247&&A().OfficialText.Contains("读取失败"),"SOOP evidence: failed official read never clears the last official numeric value");
        model.ResetSoopGeneration();
        check(model.AccountCount=="0"&&model.Accounts.All(a=>!a.HasWatchEvidence),"SOOP evidence: generation change clears auth, heartbeat and growth without deleting cached minutes");
        Apply(Account());
        fields=Account(official:"grown").EnumerateObject().ToDictionary(p=>p.Name,p=>p.Value.Clone());
        fields["officialEvidence"]=JsonSerializer.SerializeToElement(new {uid="B",status="grown",syncedAt=DateTimeOffset.UtcNow.ToString("o")});
        Apply(JsonSerializer.SerializeToElement(fields));
        check(!A().OfficialGrowth,"SOOP evidence: another UID's official response never confirms account A growth");
        fields["officialEvidence"]=JsonSerializer.SerializeToElement(new {uid="A",status="grown",syncedAt=DateTimeOffset.UtcNow.ToString("o"),campaignStates=new Dictionary<string,string>{{"other-task","grown"},{"task","unchanged"}}});
        Apply(JsonSerializer.SerializeToElement(fields));
        check(!A().OfficialGrowth&&A().OfficialText=="官方进度：未增长","SOOP evidence: another task's growth never certifies the displayed target");

        Apply(JsonSerializer.SerializeToElement(new
        {
            uid = "A", running = true, channelId = (string?)null, queueTarget = new { },
            queueDiagnostics = new
            {
                reason = "no_online_channel", message = "所选活动的官方频道当前均无可用直播",
                totalActivities = 427, candidateActivities = 1, activitiesChecked = 1, onlineChannels = 0, officialProbes = 6,
                activityFilters = new { gameFilter = 7, eligibility = 1 }, channelFilters = new { officialOffline = 6 },
            },
        }));
        check(A().StateText.Contains("均无可用直播") && A().Evidence is { Queue: { } diagnostics } evidence
            && diagnostics.Counts.Contains("候选活动 1/427") && diagnostics.Filters.Contains("指定主播离线 6")
            && evidence.ChannelId.Length == 0 && evidence.Campaign.Length == 0 && !A().HasWatchEvidence,
            "SOOP queue: account diagnostics replace stale room/target after the last official streamer goes offline");
        check(model.Accounts.Single(a => a.Id == "B").Evidence?.Queue is null,
            "SOOP queue: diagnostics stay scoped to the account that supplied them");
        model.ResetSoopGeneration();
        check(A().Evidence?.Queue is null, "SOOP queue: new Worker generation clears old discovery diagnostics");

        var directory=JsonDocument.Parse(typeof(SoopGameNames).Assembly.GetManifestResourceStream("SoopGameCatalog")!).RootElement.GetProperty("data");
        foreach(var entry in directory.EnumerateArray())
            check(SoopGameNames.Resolve("game:"+V.Text(entry,"gameIdx")).Contains(V.Text(entry,"gameTitle")),"SOOP directory ID "+V.Text(entry,"gameIdx")+" resolves to official title");
        var campaign=new SoopCampaignModel();
        var settings=new {campaign_selection_mode="selected_only",selected_games=new[]{"game:12"},game_priority=new[]{"game:12","game:170"},campaign_priorities=new Dictionary<string,string[]>{{"game:12",["one"]}}};
        JsonElement State(string name)=>JsonSerializer.SerializeToElement(new {settings,campaigns=new[]{new{uid="A",id="one",gameId="game:12",gameName=name,title="one",items=Array.Empty<object>()}}});
        campaign.Apply(State("game:12"));var first=campaign.Games[0];campaign.Apply(State("오버워치"));
        check(ReferenceEquals(first,campaign.Games[0])&&first.Name.Contains("守望先锋")&&campaign.Games[0].Id=="game:12"&&campaign.Games[0].Selected,"SOOP names refresh cached display object without changing stable selection/order");
        check(SoopGameNames.Resolve("game:99999")=="未知游戏（ID: 99999）","SOOP unknown directory ID has explicit fallback");
    }
}
