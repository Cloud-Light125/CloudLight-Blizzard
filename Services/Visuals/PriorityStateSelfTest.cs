using System.Collections.ObjectModel;
using System.IO;
using System.Text.Json;
using CloudLightBlizzard.Services.Drops;
using CloudLightBlizzard.ViewModels;
using CloudLightBlizzard.ViewModels.Visuals;

namespace CloudLightBlizzard.Services.Visuals;

/// <summary>Display settings, official progress and platform-specific runtime evidence are independent.</summary>
internal static class PriorityStateSelfTest
{
    internal static void Run(Action<bool,string> check,string output)
    {
        static JsonElement J(object value)=>JsonSerializer.SerializeToElement(value);
        static JsonElement Raw(string value)=>JsonDocument.Parse(value).RootElement.Clone();
        var priorities=new ObservableCollection<string>{"A","B","C"};
        var health=new DropsPlatformViewModel(DropsPlatform.Twitch,"Twitch");
        var model=new TwitchInventoryModel(health,priorities);
        var settings=new AppSettings();model.ConfigureDisplay(settings);model.SetScope("all");
        var snapshot=J(new{settings=new{priority_mode="PRIORITY_ONLY",priority=new[]{"A","B","C"}},
            campaigns=new[]{
                new{id="z",game="Other",gameId="other",available=true,eligible=true},
                new{id="c",game="C",gameId="3",available=true,eligible=true},
                new{id="b",game="B",gameId="2",available=true,eligible=true},
                new{id="a2",game="A",gameId="1",available=false,eligible=false},
                new{id="a1",game="A",gameId="1",available=true,eligible=true}},
            inventory=new object[]{new{id="d",campaignId="a1",game="A",gameId="1",currentMinutes=25,requiredMinutes=120,estimatedExtraMinutes=7},
                new{id="sibling",campaignId="a1",game="A",gameId="1",currentMinutes=0,requiredMinutes=60}}});
        model.Apply(snapshot);
        check(model.OverviewGroups.Select(g=>g.Id).SequenceEqual(["a1","a2","b","c","z"]),"PRIORITY 1: A/B/C user order precedes other games, including multiple campaigns and ineligible A");
        var inventoryIds=model.Groups.Select(g=>g.Id).ToArray();var queueIds=model.QueueGroups.Select(g=>g.Id).ToArray();
        model.OverviewPriorityOnly=true;
        check(model.OverviewGroups.Count==4&&model.OverviewGroups.All(g=>g.Game!="Other")&&model.Groups.Select(g=>g.Id).SequenceEqual(inventoryIds)&&model.QueueGroups.Select(g=>g.Id).SequenceEqual(queueIds),"PRIORITY 2: overview filter hides other games without changing full inventory or queue");
        check(priorities.SequenceEqual(["A","B","C"])&&V.Text(V.Get(snapshot,"settings"),"priority_mode")=="PRIORITY_ONLY"&&!health.Running,"PRIORITY 3: display toggle cannot mutate Core strategy, game order or start Worker");
        check(AppSettings.LoadFrom(AppSettings.FilePath).TwitchOverviewPriorityOnly,"PRIORITY 4: UI preference survives a settings disk round trip");
        model.OverviewPriorityOnly=false;check(model.OverviewGroups.Count==5,"PRIORITY 5: switching to all restores nonpriority campaigns");
        model.OverviewPriorityOnly=true;var reopened=new TwitchInventoryModel(new(DropsPlatform.Twitch,"Twitch"),priorities);reopened.ConfigureDisplay(AppSettings.LoadFrom(AppSettings.FilePath));reopened.Apply(snapshot);
        check(reopened.OverviewGroups.Count==4&&reopened.OverviewPriorityOnly,"PRIORITY 6: reopening restores the independent overview range");
        priorities.Move(2,0);check(model.OverviewGroups[0].Game=="C","PRIORITY 7: existing priority collection moves immediately update custom order");
        model.Apply(snapshot);check(model.OverviewGroups[0].Game=="C"&&priorities[0]=="C","PRIORITY 8: background refresh preserves current user order");
        var order=model.OverviewGroups.Select(g=>g.Id).ToArray();model.Apply(snapshot);check(order.SequenceEqual(model.OverviewGroups.Select(g=>g.Id)),"PRIORITY 9: identical refresh has deterministic campaign tie breakers");
        priorities.Clear();check(model.OverviewGroups.Count==0&&model.OverviewEmptyText=="尚未设置优先游戏","PRIORITY 10: empty priority list never silently falls back to all");
        model.OverviewPriorityOnly=false;check(model.OverviewGroups.Count==5&&model.OverviewHint.Contains("设置游戏优先级"),"PRIORITY 11: empty priorities still show all with setup guidance");
        priorities.Add("Absent");model.OverviewPriorityOnly=true;check(model.OverviewGroups.Count==0&&model.OverviewEmptyText=="当前优先游戏暂无符合条件的活动","PRIORITY 12: missing priority campaigns have a distinct empty state");
        priorities.Clear();priorities.Add("A expansion");check(model.OverviewGroups.Count==0,"PRIORITY 13: substring names never identify another game");
        priorities[0]="A";model.Apply(Raw("""{"campaigns":[{"id":"renamed","game":"Official renamed A","gameId":"1"},{"id":"exact","game":"A","gameId":"1"},{"id":"ended","game":"A","gameId":"1","expired":true}]}"""));
        check(model.OverviewGroups.Any(g=>g.Id=="renamed")&&model.OverviewGroups.Any(g=>g.Id=="ended"),"PRIORITY 14: verified game IDs match official name variants and retain ended campaign context");
        var legacy=Path.Combine(output,"legacy-settings.json");File.WriteAllText(legacy,"{\"MigrationCompleted\":true}");
        check(!AppSettings.LoadFrom(legacy).TwitchOverviewPriorityOnly,"PRIORITY 15: old settings migrate to all without changing mining configuration");

        model.OverviewPriorityOnly=false;model.Apply(snapshot);var partial=model.Groups.SelectMany(g=>g.Drops).Single(d=>d.Id=="d");
        check(partial.Current==25&&partial.PercentText=="20.8%"&&partial.StateBadge=="部分完成"&&model.WatchState=="已停止观看"&&!model.IsWatchingDrop(partial),"STATE 1: stopped + official 25/120 is partial completion with no watching badge");
        check(partial.Current==25&&partial.Estimate==7&&partial.Percent<21,"STATE 2: local estimate never increments official minutes");
        health.Running=true;model.Apply(J(new{currentDrop=new{id="d",campaignId="a1",currentMinutes=25,requiredMinutes=120,eligible=true}}));
        check(model.WatchState=="等待合资格频道"&&!model.HasWatchEvidence,"STATE 3: running Worker with no channel does not establish viewing");
        model.Apply(J(new{currentChannel=new{id="ch",name="Channel"}}));
        check(model.WatchState=="频道已选择 · 等待观看确认"&&!model.IsWatchingDrop(partial),"STATE 4: selected channel without accepted requests does not establish viewing");
        model.Apply(J(new{watchRequests=new{channelId="ch",lastResult="accepted",lastAcceptedAt=DateTimeOffset.UtcNow.ToString("o")}}));
        check(model.IsWatchingDrop(partial)&&!model.IsWatchingDrop(partial with{Id="sibling"})&&!model.OfficialGrowthConfirmed&&model.WatchState.Contains("等待官方进度确认"),"STATE 5: only exact campaign/Drop target has request evidence; siblings and official growth remain independent");
        health.ConnectionState=DropsConnectionState.Recovering;check(!model.HasWatchEvidence&&model.WatchState.Contains("恢复"),"STATE recovery: recovering connections never carry a watching badge");
        health.ConnectionState=DropsConnectionState.Connected;
        model.Apply(J(new{watchRequests=new{channelId="ch",lastResult="accepted",lastAcceptedAt=DateTimeOffset.UtcNow.ToString("o")}}));
        model.Apply(J(new{lastInventorySyncAt=DateTimeOffset.UtcNow.ToString("o"),currentDrop=new{id="d",campaignId="a1",currentMinutes=26,requiredMinutes=120,eligible=true}}));
        check(model.OfficialGrowthConfirmed&&model.WatchState=="官方进度已确认增长","STATE 6: a subsequent official increase for the same Drop confirms growth");
        model.ApplyEvent("watch_request",J(new{channelId="ch",lastResult="failed"}));check(!model.HasWatchEvidence&&!model.OfficialGrowthConfirmed&&model.WatchState.Contains("失败"),"STATE 7: newer failed request immediately overrides earlier accepted evidence");
        model.Apply(J(new{watchRequests=new{channelId="ch",lastResult="accepted",lastAcceptedAt=DateTimeOffset.UtcNow.AddMinutes(1).ToString("o")}}));check(!model.HasWatchEvidence,"STATE 8: future request timestamps are not valid evidence");
        model.Apply(J(new{watchRequests=new{channelId="ch",lastResult="accepted",lastAcceptedAt=DateTimeOffset.UtcNow.AddMinutes(-4).ToString("o")}}));check(!model.HasWatchEvidence,"STATE 9: expired request evidence cannot claim viewing");
        model.Apply(J(new{historicalReplay=true,watchRequests=new{channelId="ch",lastResult="accepted",lastAcceptedAt=DateTimeOffset.UtcNow.ToString("o")}}));
        check(!model.HasWatchEvidence&&model.CurrentDrop is null&&model.WatchState.Contains("历史")&&partial.StateBadge=="部分完成","STATE 10: historical progress and recorded requests cannot become live viewing");
        health.Running=false;model.Apply(J(new{historicalReplay=false}));check(model.WatchState=="已停止观看"&&model.Channel.Contains("上次使用频道")&&model.Groups.SelectMany(g=>g.Drops).Any(d=>d.Current==25),"STATE 11: stopping preserves official progress and labels the historical channel");
        foreach(var reward in new[]{partial,partial with{Expired=true},partial with{Eligible=false},partial with{PreconditionsSatisfiedForMining=false}})
            check(reward.StateBadge=="部分完成"&&!reward.StateText.Contains("观看"),"STATE matrix: qualification never overwrites partial official progress");
        check((partial with{Current=120}).StateKind=="completed"&&(partial with{CanClaim=true}).StateKind=="claimable"&&(partial with{Claimed=true}).StateKind=="claimed","STATE matrix: completed, claimable and claimed remain separate");

        var soopHealth=new DropsPlatformViewModel(DropsPlatform.Soop,"Soop"){Running=true};var soop=new PlatformOverviewModel(soopHealth);
        JsonElement Account(string uid,bool running,int minutes)=>J(new{uid,loggedIn=true,running,channelId="room-"+uid,connectionHealthy=true,bridgeConnected=true,heartbeatStatus="success",heartbeatLastSuccess=DateTimeOffset.UtcNow.ToString("o"),lastConfirmedAt=DateTimeOffset.UtcNow.ToString("o"),currentProgress=new[]{new{account=uid,id="task-"+uid,reward="Reward "+uid,currentMinutes=minutes,requiredMinutes=30,watchEligible=true}}});
        soop.UpdateAccount(Account("A",false,15));soop.UpdateAccount(Account("B",true,5));
        check(!soop.Accounts[0].HasWatchEvidence&&soop.Accounts[0].StateText=="已停止观看"&&soop.Accounts[0].Reward?.StateBadge=="部分完成"&&soop.Accounts[1].HasWatchEvidence,"SOOP 1: stopped A and active B use independent UID runtime and official viewTime");
        soop.UpdateAccount(J(new{uid="B",running=true,connectionHealthy=false}));check(!soop.Accounts[1].HasWatchEvidence&&!soop.RuntimeSummary.Contains("已接受"),"SOOP 2: shared Worker running without a valid account connection is not viewing");
        soop.UpdateAccount(Account("B",true,6));check(soop.Accounts[1].OfficialGrowth,"SOOP 3: official viewTime increase is observed per UID/task");
        soopHealth.ConnectionState=DropsConnectionState.Failed;check(soop.Accounts.All(a=>!a.HasWatchEvidence)&&soop.RuntimeSummary.Contains("恢复"),"SOOP recovery: connection failure invalidates account evidence and dashboard together");
        soopHealth.Running=false;check(soop.Accounts.All(a=>!a.HasWatchEvidence)&&soop.RuntimeSummary=="已停止观看"&&soop.Accounts[0].Reward?.Current==15,"SOOP 4: global stop clears every viewing indicator while preserving per UID progress");

        var biliHealth=new DropsPlatformViewModel(DropsPlatform.Bilibili,"Bilibili"){Running=true};var bili=new BilibiliDropsViewModel(biliHealth);
        bili.ApplyState(Raw("""{"account":{"loggedIn":true,"uid":1},"tasks":[{"id":"task","current":18,"limit":60}],"sessions":{"activeSessions":0,"sessions":[]}}"""));
        check(bili.WatchingSessions==0&&bili.WatchStateText.Contains("尚未建立有效观看")&&bili.Tasks.Single().Current==18,"BILIBILI 1: running Worker with zero sessions preserves 30% official progress without viewing");
        bili.HandleEvent("session",Raw("""{"activeSessions":2,"sessions":[{"id":"a","state":"active"},{"id":"b","state":"retrying"}]}"""));
        check(bili.WatchingSessions==1&&bili.WatchStateText.Contains("官方进度待确认"),"BILIBILI 2: actual active session records bound the count; sessions never imply official progress");
        biliHealth.Running=false;biliHealth.Running=true;check(bili.WatchingSessions==0,"BILIBILI 3: stop and resume cannot reuse old active Session evidence");
        bili.ApplyState(Raw("""{"historicalReplay":true,"sessions":{"activeSessions":1,"sessions":[{"id":"a","state":"active"}]}}"""));
        check(bili.WatchingSessions==0&&bili.WatchStateText.Contains("历史"),"BILIBILI 4: history never becomes live viewing");
    }
}
