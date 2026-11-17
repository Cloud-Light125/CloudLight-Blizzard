using System.Text.Json;
using CloudLightBlizzard.Services.Drops;
using CloudLightBlizzard.ViewModels;
using CloudLightBlizzard.ViewModels.Visuals;

namespace CloudLightBlizzard.Services.Visuals;

internal static class TwitchInventorySelfTest
{
    internal static void Run(Action<bool,string> check)
    {
        static JsonElement Json(object value)=>JsonSerializer.SerializeToElement(value);
        static JsonElement Raw(string value)=>JsonDocument.Parse(value).RootElement.Clone();
        var health=new DropsPlatformViewModel(DropsPlatform.Twitch,"Twitch"){Running=true};
        var model=new TwitchInventoryModel(health);
        model.Apply(Raw("""{"campaigns":[{"id":"a","name":"Completed campaign","game":"A","available":false,"expired":true},{"id":"b","name":"Current campaign","game":"B","available":true,"eligible":true,"priority":true}],"inventory":[{"id":"complete","campaignId":"a","name":"Tier 1","currentMinutes":60,"requiredMinutes":60,"completed":true,"benefits":[{"id":"done","name":"Done","imageUrl":"https://static-cdn.jtvnw.net/done.png"}]},{"id":"current","campaignId":"b","name":"Tier 2","currentMinutes":18,"requiredMinutes":60,"estimatedExtraMinutes":2,"eligible":true,"preconditionDropIds":["prior"],"preconditionsMet":false,"preconditionsSatisfiedForMining":true,"benefits":[{"id":"one","name":"One","imageUrl":"https://static-cdn.jtvnw.net/one.png"},{"id":"two","name":"Two","imageUrl":"https://static-cdn.jtvnw.net/two.png"}]}],"currentDrop":{"id":"complete","campaignId":"a","currentMinutes":60,"requiredMinutes":60,"completed":true},"currentChannel":null}"""));
        check(model.CurrentDrop is null&&!model.HasWatchEvidence&&model.WatchState=="等待合资格频道","running Worker plus completed Drop and no channel never claims current watching");
        check(model.OverviewGroups.First().Id=="b"&&model.OverviewGroups[0].Cards.Count==2,"eligible unfinished campaign ranks ahead of historical completed rewards");
        var cards=model.Groups.Single().Cards;
        check(cards.Count==2&&ReferenceEquals(cards[0].Drop,cards[1].Drop)&&cards.All(c=>c.Drop.Current==18&&c.Drop.Percent==30&&c.Drop.Estimate==2),"every Benefit retains one shared official 18/60 Drop; estimates are never added");
        check(cards[0].Drop.StateText=="部分完成"&&cards[0].Drop.PrerequisitesSatisfiedForTest(),"NoAutoClaim mining prerequisites stay distinct from claim unlock requirements");
        var first=cards[0];model.Apply(Raw("""{"currentChannel":{"id":"channel-a","name":"Channel A","game":"B"},"currentDrop":{"id":"current","campaignId":"b","name":"Tier 2","currentMinutes":18,"requiredMinutes":60},"watchRequests":{"channelId":"channel-a","lastResult":"accepted"}}"""));
        check(!model.HasWatchEvidence&&model.WatchState.Contains("等待观看确认"),"accepted flag without a recent acceptance time does not prove watching");
        model.Apply(Json(new{watchRequests=new{channelId="channel-a",lastResult="accepted",lastAcceptedAt=DateTimeOffset.UtcNow.ToString("o")}}));
        check(model.HasWatchEvidence&&model.WatchState=="观看请求正常，等待官方进度确认"&&!model.OfficialGrowthConfirmed,"accepted playlist request establishes transport evidence, never official growth");
        model.ApplyEvent("current_channel",Json(new{id="channel-b",name="Channel B"}));
        check(!model.HasWatchEvidence,"channel changes invalidate accepted requests for the previous channel");
        model.Apply(Json(new{watchRequests=new{channelId="channel-b",lastResult="accepted",lastAcceptedAt=DateTimeOffset.UtcNow.AddMinutes(-4).ToString("o")}}));
        check(!model.HasWatchEvidence,"old accepted requests expire without being treated as present watching");
        model.SetScope("priority");check(model.Groups.Count==1&&model.Groups[0].Id=="b","original priority-only campaign filter is retained");
        model.Search="not present";check(model.Groups.Count==0&&model.EmptyText.Contains("当前筛选无结果"),"empty filtered results do not imply an empty account inventory");
        model.Search="";model.SetScope("all");model.StatusFilter="completed";
        check(model.Groups.Count==1&&model.Groups[0].Id=="a","completed campaign filter uses official Drop completion");
        model.StatusFilter="claimable";check(model.Groups.Count==0,"watch completion without canClaim is not labeled claimable");
        model.StatusFilter="all";model.GameFilter="B";check(model.Groups.Single().Id=="b","game filter cannot mix another game's rewards");
        model.GameFilter="全部游戏";model.Apply(Raw("""{"inventory":[{"id":"complete","campaignId":"a","name":"Tier 1","currentMinutes":60,"requiredMinutes":60,"completed":true,"claimed":true},{"id":"current","campaignId":"b","name":"Tier 2","currentMinutes":19,"requiredMinutes":60,"canClaim":false,"benefits":[{"id":"one","name":"One","imageUrl":"https://static-cdn.jtvnw.net/one.png"},{"id":"two","name":"Two","imageUrl":"https://static-cdn.jtvnw.net/two.png"}]}]}"""));
        check(ReferenceEquals(first,model.Groups.Single(g=>g.Id=="b").Cards[0])&&first.Drop.Current==19,"inventory refresh updates existing card objects without rebuilding every image control");
        model.Apply(Raw("""{"inventorySource":"cache"}"""));health.ConnectionState=DropsConnectionState.Degraded;
        check(model.Groups.Single(g=>g.Id=="b").Drops[0].Current==19&&model.DataFreshness.Contains("可能过期"),"network failure preserves the last confirmed official minutes with stale labeling");
        health.Running=false;check(model.CurrentDrop is null&&!model.HasWatchEvidence&&model.WatchState=="已停止观看","stopping clears current watching even if a prior selected Drop remains cached");
        health.WorkerLifecycle=WorkerLifecycle.Crashed;check(model.WatchState.Contains("异常退出"),"crashed Worker is distinguishable from a manual stop");
        var biliHealth=new DropsPlatformViewModel(DropsPlatform.Bilibili,"Bilibili");var bili=new BilibiliDropsViewModel(biliHealth);
        bili.ApplyState(Raw("""{"account":{"loggedIn":true,"uid":123,"userName":"isolated"},"tasks":[{"id":"task","name":"Unfinished","current":18,"limit":60,"completed":false}],"sessions":{"configuredSessions":1,"activeSessions":0,"sessions":[]}}"""));
        check(bili.WatchStateText.Contains("已停止")&&bili.Tasks.Count==1,"unfinished Bilibili tasks do not imply a running viewing Worker");
        biliHealth.Running=true;check(bili.WatchingSessions==0&&!bili.WatchStateText.StartsWith("正在观看"),"running Bilibili Worker with zero active Sessions is not watching");
        bili.HandleEvent("session",Raw("""{"configuredSessions":1,"activeSessions":1,"sessions":[{"id":"s","roomId":1,"state":"active"}]}"""));
        check(bili.WatchingSessions==1&&bili.WatchStateText.Contains("官方进度待确认"),"active Bilibili Session evidence is independent of task percent");
        biliHealth.Running=false;check(bili.WatchingSessions==0&&bili.WatchStateText.Contains("已停止"),"manual Bilibili stop suppresses stale active Session records");
        check(Controls.RewardTilePanel.ColumnsFor(750)==3&&Controls.RewardTilePanel.ColumnsFor(970)==4&&Controls.RewardTilePanel.ColumnsFor(1170)==5&&Controls.RewardTilePanel.ColumnsFor(1700)==8,"compact readable reward grid adapts to actual content width with three/four/five/eight columns");
        var complete=new RewardVisual{Current=120,Required=120};
        check(complete.StateKind=="completed"&&!complete.Claimed&&!complete.CanClaim&&complete.StateText.Contains("未开放"),"100 percent never implies claimed or claimable");
        check((complete with{CanClaim=true}).StateKind=="claimable"&&(complete with{Claimed=true}).StateKind=="claimed","completion, claim availability and receipt have independent visual states");
        check(new RewardVisual{PreconditionDropIds=["prior"]}.PrerequisiteSummary=="前置待确认","unknown prerequisite evidence never appears satisfied");
        var soop=new PlatformOverviewModel(new(DropsPlatform.Soop,"Soop"));
        soop.Apply(Raw("""{"accounts":[{"uid":"one","loggedIn":true,"currentProgress":[{"account":"one","reward":"A","currentMinutes":15,"requiredMinutes":30},{"account":"one","reward":"B","currentMinutes":5,"requiredMinutes":45},{"account":"two","reward":"foreign","currentMinutes":99}]},{"uid":"two","loggedIn":true,"currentProgress":[{"account":"two","reward":"C","currentMinutes":2,"requiredMinutes":30}]}]}"""));
        check(soop.Accounts[0].Rewards.Count==2&&soop.Accounts[1].Rewards.Single().Current==2,"SOOP renders every official stage without accepting another UID's progress");
    }
    private static bool PrerequisitesSatisfiedForTest(this RewardVisual value)=>value.PreconditionsSatisfiedForMining==true&&value.PreconditionsMet==false;
}
