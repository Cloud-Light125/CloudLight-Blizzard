using System.Collections.ObjectModel;
using System.Text.Json;
using System.Windows;

namespace CloudLightBlizzard.ViewModels.Visuals;

/// <summary>One projection per platform; incremental updates never reuse another account's progress.</summary>
public sealed class PlatformOverviewModel : ObservableObject
{
    public DropsPlatformViewModel Health { get; }
    public string Brand => Health.Platform.ToString();
    public string Name => Health.Name;
    public string WorkerText => Health.WorkerLifecycle==Services.Drops.WorkerLifecycle.Crashed ? "Worker 异常退出" : Health.WorkerLifecycle==Services.Drops.WorkerLifecycle.Starting ? "Worker 正在启动" : Health.Running ? "Worker 运行中" : "Worker 已停止";
    public ObservableCollection<RewardVisual> Rewards { get; } = [];
    public ObservableCollection<CampaignVisual> Campaigns { get; } = [];
    public ObservableCollection<AccountVisual> Accounts { get; } = [];
    private string _accountText="尚未连接账号", _sessionText="未验证", _channel="尚未选择频道", _sync="", _autoClaim="由平台原设置控制";
    private RewardVisual? _activeReward;
    private DateTimeOffset? _observedStart;
    private string _officialChange="本次尚未观察到新的官方进度";
    private string _runtimeSummary="";
    private readonly Dictionary<string,DateTimeOffset> _accountRejectedBefore=new(StringComparer.Ordinal);
    public string RuntimeSummary=>Brand=="Soop" ? Health.WorkerLifecycle==Services.Drops.WorkerLifecycle.Crashed ? "Worker 已异常退出" : !Health.Running ? "已停止观看" :
        Health.ConnectionState is Services.Drops.DropsConnectionState.Failed or Services.Drops.DropsConnectionState.WaitingRetry or Services.Drops.DropsConnectionState.Recovering ? "连接失败 · 等待恢复" :
        Accounts.Any(a=>a.HasWatchEvidence) ? $"{Accounts.Count(a=>a.HasWatchEvidence)} 个账号观看请求已接受 · 官方进度单独确认" :
        Accounts.Any(a=>a.Running)?"Worker 运行中 · 等待账号有效观看":"Worker 运行中 · 无活跃账号观看" :
        _runtimeSummary.Length>0?_runtimeSummary:Brand=="YouTube"?Health.Status:Health.Running?"Worker 运行中 · 观看状态需单独确认":"已停止观看";
    internal void UpdateRuntimeSummary(string value){if(_runtimeSummary==value)return;_runtimeSummary=value;Raise(nameof(RuntimeSummary));}
    public string AccountText { get=>_accountText; private set=>Set(ref _accountText,value); }
    public string SessionText { get=>_sessionText; private set=>Set(ref _sessionText,value); }
    public string Channel { get=>_channel; private set=>Set(ref _channel,value); }
    public string AutoClaimText { get=>_autoClaim; private set=>Set(ref _autoClaim,value); }
    public RewardVisual? ActiveReward { get=>_activeReward; private set { Set(ref _activeReward,value); Raise(nameof(FeaturedReward)); Raise(nameof(FeaturedLabel)); } }
    public RewardVisual? FeaturedReward => ActiveReward ?? Accounts.FirstOrDefault(a=>a.Running&&a.Reward is not null)?.Reward ?? Rewards.FirstOrDefault(r=>!r.Claimed&&!r.WatchCompleted&&!r.Expired&&r.Eligible!=false) ?? Rewards.FirstOrDefault(r=>r.CanClaim&&!r.Claimed) ?? Rewards.FirstOrDefault();
    public string FeaturedLabel => "奖励预览 · 进度以官方确认为准";
    public string RewardCount => Rewards.Count.ToString();
    public string CampaignCount => Campaigns.Count.ToString();
    public string ClaimableCount => Rewards.Count(r=>r.CanClaim&&!r.Claimed).ToString();
    public string AccountCount => Accounts.Count(a=>a.LoggedIn).ToString();
    public string SyncText => DateTimeOffset.TryParse(_sync,out var at) ? $"上次官方同步 {at.ToLocalTime():HH:mm:ss}" : "尚无官方同步记录";
    public Visibility RewardVisibility => FeaturedReward is null||SessionText!="已验证" ? Visibility.Collapsed : Visibility.Visible;
    public Visibility EmptyRewardVisibility => RewardVisibility==Visibility.Visible ? Visibility.Collapsed : Visibility.Visible;
    public string NextActionText => SessionText!="已验证" ? "连接账号 →" : "查看任务 →";
    public string EmptyTaskText => SessionText!="已验证" ? "连接账号后查看官方任务与进度" : "尚无当前任务 · 刷新平台活动";
    public string TaskSummary => SessionText!="已验证" ? "连接账号后查看奖励" : FeaturedReward?.Title ?? "尚无当前任务";
    public string FeaturedImageUrl=>FeaturedReward?.Benefits.FirstOrDefault()?.ImageUrl??FeaturedReward?.ImageUrl??"";
    public string FeaturedProgress=>FeaturedReward?.ProgressText??"等待官方任务";
    public double FeaturedPercent=>FeaturedReward?.Percent??0;
    public string OfficialChangeText=>_officialChange;
    public IReadOnlyList<RewardVisual> RewardPreview=>(ActiveReward is null?Rewards.AsEnumerable():new[]{ActiveReward}.Concat(Rewards)).DistinctBy(r=>r.Id).Take(4).ToArray();
    public IReadOnlyList<CampaignVisual> CampaignPreview=>Campaigns.Take(3).ToArray();
    public string ObservedDurationText=>_observedStart is {} start ? $"本机观察时长 {(DateTimeOffset.Now-start):hh\\:mm\\:ss}" : "本机未观察到运行";

    internal PlatformOverviewModel(DropsPlatformViewModel health)
    {
        Health=health;if(health.Running)_observedStart=DateTimeOffset.Now;
        health.PropertyChanged+=(_,e)=>
        {
            Raise(nameof(RuntimeSummary));
            Raise(nameof(WorkerText));
            if((e.PropertyName==nameof(health.ConnectionState)&&health.ConnectionState is Services.Drops.DropsConnectionState.Failed or Services.Drops.DropsConnectionState.WaitingRetry or Services.Drops.DropsConnectionState.Recovering)||
                e.PropertyName==nameof(health.WorkerLifecycle)&&health.WorkerLifecycle==Services.Drops.WorkerLifecycle.Crashed)
            {
                for(var index=0;index<Accounts.Count;index++){_accountRejectedBefore[Accounts[index].Id]=DateTimeOffset.UtcNow;Accounts[index]=Accounts[index] with{WatchAcceptedAt=null,OfficialGrowth=false,ConnectionText="连接失败 · 等待重试"};}
                NotifyVisuals();
            }
            if(e.PropertyName==nameof(health.Running))
            {
                _observedStart=health.Running?DateTimeOffset.Now:null;
                if(!health.Running&&_activeReward is {} reward)_activeReward=reward with{IsCurrent=false};
                if(!health.Running)for(var index=0;index<Accounts.Count;index++)Accounts[index]=Accounts[index] with{Running=false,WatchAcceptedAt=null,OfficialGrowth=false};
                NotifyVisuals();
            }
        };
    }
    internal void Apply(JsonElement state)
    {
        if(V.Has(state,"lastInventorySyncAt")) _sync=V.Text(state,"lastInventorySyncAt");
        if(V.Has(state,"accounts"))
        {
            var incoming=V.Rows(state,"accounts").ToArray();
            var ids=incoming.Select(a=>V.Text(a,"uid",V.Text(a,"userId"))).ToHashSet(StringComparer.Ordinal);
            foreach(var account in incoming) UpdateAccount(account);
            foreach(var removed in Accounts.Where(a=>!ids.Contains(a.Id)).ToArray()){Accounts.Remove(removed);_accountRejectedBefore.Remove(removed.Id);}
            var first=V.Rows(state,"accounts").FirstOrDefault();
            AccountText=V.Text(first,"userName",V.Text(first,"userId",V.Text(first,"uid","尚未连接账号")));
            SessionText=V.Rows(state,"accounts").Any(a=>V.Bool(a,"loggedIn")) ? "已验证" : V.Rows(state,"accounts").Any(a=>V.Bool(a,"sessionSaved")) ? "已保存 · 待验证" : "未验证";
        }
        if(V.Has(state,"account"))
        {
            var account=V.Get(state,"account");
            AccountText=V.Text(account,"userName",V.Text(account,"uname",V.Text(account,"uid","尚未连接账号")));
        }
        if(V.Has(state,"loggedIn")) SessionText=V.Bool(state,"loggedIn") ? "已验证" : "未验证";
        if(V.Has(state,"currentChannel")) Channel=V.Text(V.Get(state,"currentChannel"),"name","尚未选择频道");
        if(V.Has(state,"rooms") && Brand=="Bilibili") Channel=V.Rows(state,"rooms").Where(r=>V.Bool(r,"enabled",true)).Select(r=>V.Text(r,"name",V.Text(r,"id"))).FirstOrDefault() ?? "尚未添加直播间";
        var rewardKey=Brand=="Bilibili" ? "tasks" : "inventory";
        if(V.Has(state,rewardKey))
        {
            Rewards.Clear();
            foreach(var row in V.Rows(state,rewardKey)) Rewards.Add(RewardVisual.From(row,Brand,_sync));
        }
        if(V.Has(state,"currentDrop"))
        {
            var current=V.Get(state,"currentDrop");
            var next=current.ValueKind==JsonValueKind.Object ? RewardVisual.From(current,Brand,_sync,Health.Running) : null;
            if(next is not null&&ActiveReward is {} prior&&prior.Id==next.Id)
                _officialChange=next.Current>prior.Current?$"本次官方进度增加 {next.Current-prior.Current:0.#} 分钟":next.Current<prior.Current?"官方进度发生回退，请重新检查":"本次尚未观察到新的官方进度";
            else _officialChange="等待下一次官方进度确认";
            if(next is not null&&Rewards.FirstOrDefault(r=>r.Id==next.Id&&r.CampaignId==next.CampaignId) is {} inventory)
                next=next with{Benefits=next.Benefits.Count==0?inventory.Benefits:next.Benefits,ImageUrl=string.IsNullOrWhiteSpace(next.ImageUrl)?inventory.ImageUrl:next.ImageUrl,Game=string.IsNullOrWhiteSpace(next.Game)?inventory.Game:next.Game,GameImageUrl=string.IsNullOrWhiteSpace(next.GameImageUrl)?inventory.GameImageUrl:next.GameImageUrl};
            ActiveReward=next is {WatchCompleted:false,Claimed:false}?next:null;
        }
        if(V.Has(state,"campaigns") || Brand=="Bilibili" && V.Has(state,"activities"))
        {
            Campaigns.Clear();
            foreach(var c in V.Rows(state,Brand=="Bilibili"?"activities":"campaigns"))
                Campaigns.Add(new(V.Text(c,"id"),V.Text(c,"name"),V.Text(c,"game"),V.Text(c,"gameImageUrl",V.Text(c,"imageUrl")),
                    V.Bool(c,"available")?"可参与":V.Bool(c,"eligible")?"资格已确认":V.Text(c,"status","资格待确认"),V.Text(c,"endsAt"))
                    {Rewards=V.Rows(c,"drops").Select(d=>Rewards.FirstOrDefault(r=>r.Id==V.Text(d,"id"))??RewardVisual.From(d,Brand,_sync)).ToArray()});
        }
        if(Brand=="Soop" && V.Has(state,"uid")) UpdateAccount(state);
        if(Brand=="Soop"&&V.Has(state,"currentProgress"))
            foreach(var group in V.Rows(state,"currentProgress").GroupBy(p=>V.Text(p,"account")).Where(g=>!string.IsNullOrWhiteSpace(g.Key)))
                UpdateAccount(JsonSerializer.SerializeToElement(new{uid=group.Key,currentProgress=group.ToArray()}));
        var settings=V.Get(state,"settings");
        if(settings.ValueKind==JsonValueKind.Object) AutoClaimText=V.Bool(settings,"auto_claim_drops",V.Bool(settings,"auto_claim_enabled",V.Bool(settings,"auto_claim"))) ? "自动领取已启用" : "自动领取已关闭";
        NotifyVisuals();
    }
    internal void UpdateAccount(JsonElement account)
    {
        var uid=V.Text(account,"uid",V.Text(account,"userId"));
        if(string.IsNullOrWhiteSpace(uid)) return;
        // Progress is read only from this UID's current official response, never from history.
        var old=Accounts.FirstOrDefault(a=>a.Id==uid);
        var rewards=V.Has(account,"currentProgress") ? V.Rows(account,"currentProgress").Where(p=>!V.Has(p,"account")||V.Text(p,"account")==uid).Select(p=>RewardVisual.From(p,Brand,"")).ToArray() : old?.Rewards??[];
        var p=V.Rows(account,"currentProgress").FirstOrDefault(p=>!V.Has(p,"account")||V.Text(p,"account")==uid);
        var reward=p.ValueKind==JsonValueKind.Object ? RewardVisual.From(p,Brand,"") : V.Has(account,"currentProgress")?null:old?.Reward;
        var running=Health.Running&&V.Bool(account,"running",old?.Running??false);
        var loggedIn=V.Bool(account,"loggedIn",old?.LoggedIn??false);
        var phase=V.Text(account,"status",old?.Phase??"");
        var channelId=V.Text(account,"channelId",old?.ChannelId??"");
        var channelChanged=channelId!=old?.ChannelId;
        var diagnostic=V.Rows(account,"progressDiagnostics").FirstOrDefault(d=>V.Text(d,"uid")==uid&&V.Bool(d,"watchEligible"));
        var confirmedAt=V.Text(account,"heartbeatLastSuccess",V.Text(diagnostic,"heartbeatLastSuccess"));
        if(!running||!loggedIn)_accountRejectedBefore[uid]=DateTimeOffset.UtcNow;
        var accepted=running&&loggedIn&&channelId.Length>0&&V.Bool(account,"connectionHealthy",V.Bool(diagnostic,"connectionHealthy"))&&
            V.Bool(account,"bridgeConnected",V.Bool(diagnostic,"bridgeConnected"))&&V.Text(account,"heartbeatStatus",V.Text(diagnostic,"heartbeatStatus")) is "success" or "ok"&&WatchEvidence.Recent(confirmedAt)&&
            DateTimeOffset.TryParse(confirmedAt,out var heartbeatAt)&&heartbeatAt>=_accountRejectedBefore.GetValueOrDefault(uid)&&(_observedStart is null||heartbeatAt>=_observedStart)&&
            (V.Bool(diagnostic,"watchEligible")||V.Rows(account,"currentProgress").Any(p=>(!V.Has(p,"account")||V.Text(p,"account")==uid)&&V.Bool(p,"watchEligible")));
        var connection=phase.Contains("recover",StringComparison.OrdinalIgnoreCase)||phase.Contains("恢复") ? "等待重试 · 恢复中" :
            phase.Contains("fail",StringComparison.OrdinalIgnoreCase)||phase.Contains("失败")?"连接失败 · 等待重试":channelId.Length==0?"等待合资格频道":"频道已选择 · 等待观看确认";
        var historical=V.Bool(account,"historicalReplay",old?.Historical??false);
        var grew=accepted&&!historical&&!channelChanged&&rewards.Any(r=>r.Id.Length>0&&old?.Rewards.Any(p=>p.Id==r.Id&&r.Current>p.Current)==true)&&WatchEvidence.Recent(V.Text(account,"lastConfirmedAt"));
        var row=new AccountVisual(uid,V.Text(account,"userName",old?.Name??uid),V.Bool(account,"primary",old?.IsPrimary??false),running,loggedIn,
            V.Text(account,"channelName",old?.Channel??"尚未进入直播间"),V.Text(account,"missionTitle",old?.Task??"等待任务"),reward,V.Text(account,"avatarUrl",old?.ImageUrl??""))
            {Phase=phase,Rewards=rewards,ChannelId=channelId,WatchAcceptedAt=accepted?DateTimeOffset.Parse(confirmedAt):null,OfficialGrowth=grew,ConnectionText=connection,Historical=historical};
        if(old is null) Accounts.Add(row); else Accounts[Accounts.IndexOf(old)]=row;
        if(Brand=="Soop")
        {
            var active=Accounts.FirstOrDefault(a=>a.Running)??Accounts.FirstOrDefault(a=>a.IsPrimary);
            Channel=active?.Channel??"尚未选择频道";
        }
        NotifyVisuals();
    }
    private void NotifyVisuals()
    {
        foreach(var name in new[]{nameof(RuntimeSummary),nameof(FeaturedReward),nameof(FeaturedLabel),nameof(RewardVisibility),nameof(EmptyRewardVisibility),nameof(RewardCount),nameof(CampaignCount),nameof(ClaimableCount),nameof(AccountCount),nameof(SyncText),nameof(TaskSummary),nameof(FeaturedImageUrl),nameof(FeaturedProgress),nameof(FeaturedPercent),nameof(ObservedDurationText),nameof(OfficialChangeText),nameof(RewardPreview),nameof(CampaignPreview),nameof(NextActionText),nameof(EmptyTaskText)}) Raise(name);
    }
    internal void RefreshClock()
    {
        if(Brand=="Soop")for(var index=0;index<Accounts.Count;index++)if(Accounts[index].WatchAcceptedAt is {} at&&!WatchEvidence.Recent(at))Accounts[index]=Accounts[index] with{WatchAcceptedAt=null,OfficialGrowth=false};
        Raise(nameof(SyncText));Raise(nameof(ObservedDurationText));Raise(nameof(RuntimeSummary));
    }
    internal void RefreshBilibili(BilibiliDropsViewModel details)
    {
        AccountText=details.LoggedIn?$"{details.UserName} · {details.Uid}":"尚未连接账号";SessionText=details.LoggedIn?"已验证":"未验证";
        var previous=Rewards.GroupBy(r=>r.Id).ToDictionary(g=>g.Key,g=>g.First());
        Rewards.Clear();
        foreach(var task in details.Tasks)Rewards.Add(new RewardVisual{Id=task.Id,Title=task.Name,Brand="Bilibili",Current=task.Current,Required=task.Limit,Unit="",Completed=task.Completed,Claimed=task.Claimed,CanClaim=task.Claimable,ImageUrl=previous.GetValueOrDefault(task.Id)?.ImageUrl??""});
        Channel=details.Rooms.FirstOrDefault(r=>r.Enabled)?.Name??"尚未添加直播间";NotifyVisuals();
        if(!string.IsNullOrWhiteSpace(details.LastApiSuccessAt)){_sync=details.LastApiSuccessAt;Raise(nameof(SyncText));}
    }
}
