using System.Collections.ObjectModel;
using System.Text.Json;
using System.Windows;
using CloudLightBlizzard.Services;
using CloudLightBlizzard.Services.Drops;

namespace CloudLightBlizzard.ViewModels.Visuals;

/// <summary>Every benefit references one official Drop. No independent benefit timers or guessed unlock order.</summary>
public sealed class RewardCardVisual : ObservableObject
{
    public string Key { get; }
    public RewardVisual Drop { get; private set; }
    public BenefitVisual? Benefit { get; private set; }
    public string Title => Benefit?.Name ?? Drop.Title;
    public string ImageUrl => Benefit?.ImageUrl ?? Drop.ImageUrl;
    public string Stage => Drop.Brand!="Twitch" ? Drop.Brand=="Soop" ? "官方任务阶段" : "官方任务" : Drop.Benefits.Count > 1 ? $"{Drop.Benefits.Count} 件共享进度 · {Drop.Title}" : $"Drop · {Drop.Title}";
    public string Percent => Drop.Required > 0 ? Drop.PercentText : Drop.WatchCompleted || Drop.Claimed ? "已完成" : "—";
    internal RewardCardVisual(string key, RewardVisual drop, BenefitVisual? benefit) { Key=key; Drop=drop; Benefit=benefit; }
    internal void Update(RewardVisual drop, BenefitVisual? benefit)
    {
        Drop=drop; Benefit=benefit;
        foreach(var name in new[]{nameof(Drop),nameof(Benefit),nameof(Title),nameof(ImageUrl),nameof(Stage),nameof(Percent)}) Raise(name);
    }
}

public sealed class CampaignRewardGroup : ObservableObject
{
    public string Id { get; }
    private JsonElement _source;
    public ObservableCollection<RewardCardVisual> Cards { get; } = [];
    public IReadOnlyList<RewardVisual> Drops { get; private set; } = [];
    public string Title => V.Text(_source,"name",Drops.FirstOrDefault()?.Campaign??"未命名活动");
    public string Game => V.Text(_source,"game",Drops.FirstOrDefault()?.Game??"");
    public string GameId => V.Text(_source,"gameId",Drops.FirstOrDefault(d=>d.GameId.Length>0)?.GameId??"");
    public string ImageUrl => V.Text(_source,"gameImageUrl",Drops.FirstOrDefault()?.GameImageUrl??"");
    public bool Available => V.Bool(_source,"available");
    public bool Priority => V.Bool(_source,"priority");
    public bool Expired => V.Bool(_source,"expired") || DateTimeOffset.TryParse(V.Text(_source,"endsAt"),out var end)&&end<DateTimeOffset.UtcNow;
    public bool Upcoming => V.Bool(_source,"upcoming");
    public bool? Eligible => V.Get(_source,"eligible").ValueKind is JsonValueKind.True or JsonValueKind.False ? V.Bool(_source,"eligible") : null;
    public string Status => Expired ? "活动已结束" : Upcoming ? "活动尚未开始" : Eligible==false ? "资格未满足" : Available ? "可参与" : Drops.Count>0&&Drops.All(d=>d.WatchCompleted||d.Claimed) ? "观看要求已完成" : "资格 / 频道待确认";
    public string EndText => DateTimeOffset.TryParse(V.Text(_source,"endsAt"),out var end) ? $"结束于 {end.ToLocalTime():MM-dd HH:mm}" : "结束时间未提供";
    public DateTimeOffset SortEnd => DateTimeOffset.TryParse(V.Text(_source,"endsAt"),out var end) ? end : DateTimeOffset.MaxValue;
    public string Summary => $"{Drops.Count(d=>d.WatchCompleted||d.Claimed)}/{Drops.Count} Drop 完成 · {Cards.Count} 件物品 · {Drops.Count(d=>d.CanClaim&&!d.Claimed)} 可领取";
    public string ProgressSummary => V.Has(_source,"remainingMinutes") ? $"官方剩余 {V.Number(_source,"remainingMinutes"):0.#} 分钟" : "官方时长以各 Drop 为准";
    public double RemainingMinutes => V.Number(_source,"remainingMinutes");
    public string StartText => DateTimeOffset.TryParse(V.Text(_source,"startsAt"),out var at) ? $"开始于 {at.ToLocalTime():MM-dd HH:mm}" : "开始时间未提供";
    public string RemainingSummary => $"已完成 {Drops.Count(d=>d.WatchCompleted||d.Claimed)} · 剩余 {Drops.Count(d=>!d.WatchCompleted&&!d.Claimed)} · {ProgressSummary}";
    public double Percent => V.Has(_source,"progress") ? Math.Clamp(V.Number(_source,"progress")*100,0,100) : 0;
    public double ContainerMaxWidth=>Cards.Count==1?360:Cards.Count==2?510:double.PositiveInfinity;

    internal CampaignRewardGroup(string id) => Id=id;
    internal void Update(JsonElement source, IReadOnlyList<RewardVisual> drops)
    {
        _source=source; Drops=drops;
        var wanted=new List<RewardCardVisual>();
        var existing=Cards.ToDictionary(c=>c.Key,StringComparer.Ordinal);
        foreach(var drop in drops)
        {
            var benefits=drop.Benefits.Count==0 ? new BenefitVisual?[]{null} : drop.Benefits.Cast<BenefitVisual?>().ToArray();
            for(var index=0;index<benefits.Length;index++)
            {
                var benefit=benefits[index]; var key=drop.Id+"/"+(benefit?.Id??"missing")+"/"+index;
                if(existing.TryGetValue(key,out var card))card.Update(drop,benefit); else card=new(key,drop,benefit);
                wanted.Add(card);
            }
        }
        TwitchInventoryModel.Reconcile(Cards,wanted,c=>c.Key);
        foreach(var property in new[]{nameof(Title),nameof(Game),nameof(GameId),nameof(ImageUrl),nameof(Status),nameof(StartText),nameof(EndText),nameof(Summary),nameof(RemainingSummary),nameof(RemainingMinutes),nameof(ProgressSummary),nameof(Percent),nameof(ContainerMaxWidth)})Raise(property);
    }
}

/// <summary>Read-only inventory projection. Watching requires recent accepted requests for the actual channel.</summary>
public sealed class TwitchInventoryModel : ObservableObject
{
    private readonly DropsPlatformViewModel _health;
    private readonly Dictionary<string,CampaignRewardGroup> _all=new(StringComparer.Ordinal);
    private readonly Dictionary<string,TwitchClaimAction> _claimActions=new(StringComparer.Ordinal);
    private string _claimOwner="";
    public IReadOnlyCollection<CampaignRewardGroup> AllCampaigns => _all.Values;
    public JsonElement WatchPriorities { get; private set; }
    private string _prioritySummary="观看优先级：最早结束优先";
    public string PrioritySummary => _prioritySummary;
    private bool _batchClaiming;
    private string _batchSummary = "批量领取将先读取官方库存。";
    public bool CanClaimBatch => !_batchClaiming;
    public bool CanCancelBatch => _batchClaiming;
    public string BatchClaimSummary => _batchSummary;
    internal bool BeginBatchClaim()
    {
        if (_batchClaiming) return false;
        _batchClaiming = true; _batchSummary = "正在读取官方可领取集合…";
        Raise(nameof(CanClaimBatch)); Raise(nameof(CanCancelBatch)); Raise(nameof(BatchClaimSummary)); return true;
    }
    internal void EndBatchClaim() { _batchClaiming = false; Raise(nameof(CanClaimBatch)); Raise(nameof(CanCancelBatch)); }
    internal void ApplyBatchClaim(JsonElement payload)
    {
        var rows=V.Rows(payload,"results").ToArray();
        int Count(string status)=>rows.Count(r=>V.Text(r,"status")==status);
        var total=(int)V.Number(payload,"total",rows.Length);
        var success=(int)V.Number(payload,"success",Count("claimed"));
        var skipped=(int)V.Number(payload,"skipped",Count("already_claimed"));
        var unknown=(int)V.Number(payload,"unknown",Count("unconfirmed"));
        var pending=(int)V.Number(payload,"pending",Math.Max(0,total-rows.Length)+Count("not_submitted"));
        var failed=(int)V.Number(payload,"failed",rows.Length-Count("claimed")-Count("already_claimed")-Count("unconfirmed")-Count("not_submitted"));
        _batchSummary=$"本次符合条件 {total} · 已确认成功 {success} · 已领取 {skipped} · 失败 {failed} · 待确认 {unknown} · 未执行 {pending}";
        var current=V.Text(payload,"current");if(current.Length>0)_batchSummary+=$"\n正在处理：{current}";
        Raise(nameof(BatchClaimSummary));
    }
    public Action<string>? DisplaySortChanged { get; set; }
    private JsonElement _inventory, _campaigns;
    private string _channelId="", _watchChannelId="", _watchResult="", _sync="", _phase="", _inventorySource="";
    private DateTimeOffset? _acceptedAt;
    private DateTimeOffset _runStartedAt=DateTimeOffset.UtcNow;
    private DateTimeOffset _rejectedBefore=DateTimeOffset.MinValue;
    private DateTimeOffset? _growthAt;
    private readonly Dictionary<string,double> _officialMinutes=new(StringComparer.Ordinal);
    private readonly ObservableCollection<string>? _configuredGames;
    private IReadOnlyList<string> _priorityGames=[];
    private readonly Dictionary<string,int> _priorityRanks=new(StringComparer.Ordinal);
    private AppSettings? _displaySettings;
    private bool _overviewPriorityOnly, _historical;
    private bool _emptyPriorityOnly;
    private RewardVisual? _selectedDrop;
    private string _channel="尚未选择频道", _game="", _scope="available", _search="", _status="all", _sort="recommended", _gameFilter="全部游戏";
    public ObservableCollection<CampaignRewardGroup> Groups { get; } = [];
    public ObservableCollection<CampaignRewardGroup> OverviewGroups { get; } = [];
    public ObservableCollection<CampaignRewardGroup> QueueGroups { get; } = [];
    public ObservableCollection<string> Games { get; } = ["全部游戏"];
    public string Search { get=>_search; set { if(_search==value)return;Set(ref _search,value);Filter(); } }
    public string StatusFilter { get=>_status; set { if(_status==value)return;Set(ref _status,value??"all");Filter(); } }
    public string Sort { get=>_sort; set { if(_sort==value)return;Set(ref _sort,value??"recommended");Filter();DisplaySortChanged?.Invoke(_sort); } }
    public string GameFilter { get=>_gameFilter; set { if(_gameFilter==value)return;Set(ref _gameFilter,value??"全部游戏");Filter(); } }
    public string Channel=>!_health.Running&&_channelId.Length>0?"上次使用频道："+_channel:_channel;
    public string Game=>_game;
    public string SyncText=>(DateTimeOffset.TryParse(_sync,out var at)?$"官方同步 {at.ToLocalTime():HH:mm:ss}" : "尚无官方同步")+(_inventorySource=="cache"||_health.ConnectionState is DropsConnectionState.Failed or DropsConnectionState.Degraded or DropsConnectionState.WaitingRetry?" · 数据可能过期":"");
    public string DataFreshness=>_inventorySource=="cache"||_health.ConnectionState is DropsConnectionState.Failed or DropsConnectionState.Degraded or DropsConnectionState.WaitingRetry ? "保留上次官方确认值 · 数据可能过期" : "官方进度与本地估算分别显示";
    public RewardVisual? CurrentDrop=>_health.Running&&!_historical&&_selectedDrop is {WatchCompleted:false,Claimed:false,Expired:false,Upcoming:false,Eligible:not false,PreconditionsSatisfiedForMining:not false}&&
        (!_all.TryGetValue(_selectedDrop.CampaignId,out var group)||group.Available&&!group.Expired&&!group.Upcoming&&group.Eligible!=false)?_selectedDrop:null;
    public bool HasWatchEvidence=>_health.Running&&!_historical&&_health.WorkerLifecycle is not (WorkerLifecycle.Starting or WorkerLifecycle.Stopping or WorkerLifecycle.Crashed)&&_health.ConnectionState is not (DropsConnectionState.Failed or DropsConnectionState.WaitingRetry or DropsConnectionState.Recovering)&&
        _channelId.Length>0&&_watchChannelId==_channelId&&_watchResult=="accepted"&&_acceptedAt is {} at&&at>=_runStartedAt&&at>=_rejectedBefore&&WatchEvidence.Recent(at);
    public bool IsWatchingDrop(RewardVisual drop)=>HasWatchEvidence&&CurrentDrop is {} current&&current.Id==drop.Id&&current.CampaignId==drop.CampaignId&&
        !drop.WatchCompleted&&!drop.Claimed&&!drop.Expired&&!drop.Upcoming&&drop.Eligible!=false&&drop.PreconditionsSatisfiedForMining!=false;
    public bool OfficialGrowthConfirmed=>HasWatchEvidence&&CurrentDrop is not null&&_growthAt is {} at&&WatchEvidence.Recent(at);
    public string WatchState=>_historical ? "历史官方快照 · 非实时观看" : _health.WorkerLifecycle==WorkerLifecycle.Crashed ? "Worker 已异常退出" : _health.WorkerLifecycle==WorkerLifecycle.Starting ? "Worker 正在启动" : !_health.Running ? "已停止观看" :
        _health.ConnectionState is DropsConnectionState.Failed or DropsConnectionState.WaitingRetry ? "连接失败 · 等待恢复" :
        _health.ConnectionState==DropsConnectionState.Recovering ? "等待重试 · 恢复中" :
        _health.ConnectionState==DropsConnectionState.Connecting&&_channelId.Length>0 ? "正在连接频道 · 等待观看确认" :
        _channelId.Length==0 ? _emptyPriorityOnly ? "等待设置优先游戏" : _phase is "loading_campaigns" or "loading_inventory" ? "正在加载官方活动" : "等待合资格频道" :
        _watchResult=="failed" ? "观看请求失败 · 等待重试" : !HasWatchEvidence ? "频道已选择 · 等待观看确认" :
        OfficialGrowthConfirmed ? "官方进度已确认增长" : "观看请求正常，等待官方进度确认";
    public string CurrentSummary=>CurrentDrop is {} drop ? $"当前目标：{drop.Title} · {drop.StateText} · 官方 {drop.ProgressText} · {drop.PercentText}" : "当前没有有效观看目标 · 已有官方进度保留在奖励卡片中";
    public Visibility CurrentSummaryVisibility=>CurrentDrop is null?Visibility.Collapsed:Visibility.Visible;
    public string EstimateText=>CurrentDrop is {Estimate:>0} drop?drop.EstimateText:"";
    public string CountText=>$"{_all.Count} 个活动 · {_all.Values.Sum(g=>g.Cards.Count)} 件物品 · {_all.Values.Sum(g=>g.Drops.Count(d=>d.CanClaim&&!d.Claimed))} 可领取";
    public string FilterSummary=>$"筛选后 {Groups.Count} 个活动 · {Groups.Sum(g=>g.Cards.Count)} 件物品";
    public string EmptyText=>_all.Count==0 ? "尚未读取到官方活动，请验证账号并刷新。" : "当前筛选无结果 · 可调整显示范围、游戏或状态筛选。";
    public Visibility EmptyVisibility=>Groups.Count==0?Visibility.Visible:Visibility.Collapsed;
    public string OverviewTitle=>"奖励背包 · 优先游戏与待处理活动";
    public bool OverviewPriorityOnly
    {
        get=>_overviewPriorityOnly;
        set
        {
            if(value==_overviewPriorityOnly)return;
            Set(ref _overviewPriorityOnly,value);
            if(_displaySettings is not null){_displaySettings.TwitchOverviewPriorityOnly=value;_displaySettings.Save();}
            Filter();
        }
    }
    public string OverviewScope { get=>OverviewPriorityOnly?"priority":"all"; set=>OverviewPriorityOnly=value=="priority"; }
    public string OverviewEmptyText=>OverviewPriorityOnly ? _priorityGames.Count==0 ? "尚未设置优先游戏" : "当前优先游戏暂无符合条件的活动" : "尚未读取到官方活动，请验证账号并刷新。";
    public Visibility OverviewEmptyVisibility=>OverviewGroups.Count==0?Visibility.Visible:Visibility.Collapsed;
    public Visibility PrioritySetupVisibility=>_priorityGames.Count==0?Visibility.Visible:Visibility.Collapsed;
    public Visibility ViewAllVisibility=>OverviewPriorityOnly&&_priorityGames.Count>0&&OverviewGroups.Count==0?Visibility.Visible:Visibility.Collapsed;
    public string OverviewHint=>_priorityGames.Count==0?"可设置游戏优先级，让指定游戏置顶。":$"按已设置的 {_priorityGames.Count} 个优先游戏顺序展示 · {OverviewGroups.Count} 个活动";

    internal TwitchInventoryModel(DropsPlatformViewModel health,ObservableCollection<string>? configuredGames=null)
    {
        _health=health;_configuredGames=configuredGames;
        if(configuredGames is not null){_priorityGames=configuredGames.ToArray();configuredGames.CollectionChanged+=(_,_)=>{_priorityGames=configuredGames.ToArray();Filter();};}
        health.PropertyChanged+=(_,e)=>
        {
            if(e.PropertyName==nameof(health.Running)){_runStartedAt=DateTimeOffset.UtcNow;InvalidateWatch();}
            if(e.PropertyName==nameof(health.ConnectionState)&&health.ConnectionState is DropsConnectionState.Failed or DropsConnectionState.WaitingRetry or DropsConnectionState.Recovering){_rejectedBefore=DateTimeOffset.UtcNow;InvalidateWatch();}
            NotifyRuntime();Filter();
        };
    }
    internal void ConfigureDisplay(AppSettings settings){_displaySettings=settings;_overviewPriorityOnly=settings.TwitchOverviewPriorityOnly;Raise(nameof(OverviewPriorityOnly));Raise(nameof(OverviewScope));Filter();}
    internal void SetScope(string scope){_scope=scope;Filter();}
    internal void Apply(JsonElement state)
    {
        if(V.Has(state,"accounts"))
        {
            var owner=V.Rows(state,"accounts").Select(a=>V.Text(a,"userId")).FirstOrDefault()??"";
            if(owner!=_claimOwner){_claimActions.Clear();_claimOwner=owner;}
        }
        var watchSettings=V.Get(state,"settings");
        if(V.Has(watchSettings,"campaign_watch_priority"))WatchPriorities=V.Get(watchSettings,"campaign_watch_priority").Clone();
        if(V.Has(watchSettings,"campaign_display_sort")){_sort=V.Text(watchSettings,"campaign_display_sort","recommended");Raise(nameof(Sort));}
        if(V.Has(state,"watchSchedule"))
        {
            var schedule=V.Get(state,"watchSchedule");
            _prioritySummary=V.Has(schedule,"rank")?$"当前观看优先级：{V.Number(schedule,"rank"):0} · {V.Text(schedule,"campaignName")}":"观看优先级：等待调度";
            Raise(nameof(PrioritySummary));
        }
        if(V.Has(state,"inventory"))_inventory=V.Get(state,"inventory").Clone();
        if(V.Has(state,"campaigns"))_campaigns=V.Get(state,"campaigns").Clone();
        if(V.Has(state,"lastInventorySyncAt"))_sync=V.Text(state,"lastInventorySyncAt");
        if(V.Has(state,"inventorySource"))_inventorySource=V.Text(state,"inventorySource");
        if(V.Has(state,"connectionState"))_phase=V.Text(state,"connectionState");
        if(V.Has(state,"historicalReplay"))_historical=V.Bool(state,"historicalReplay");
        if(V.Has(state,"settings")){var settings=V.Get(state,"settings");_emptyPriorityOnly=V.Text(settings,"priority_mode")=="PRIORITY_ONLY"&&!V.Rows(settings,"priority").Any();if(_configuredGames is null&&V.Has(settings,"priority"))_priorityGames=V.Rows(settings,"priority").Where(p=>p.ValueKind==JsonValueKind.String).Select(p=>p.GetString()??"").ToArray();}
        if(V.Has(state,"currentChannel"))UpdateChannel(V.Get(state,"currentChannel"));
        if(V.Has(state,"watchRequests"))UpdateWatch(V.Get(state,"watchRequests"));
        if(V.Has(state,"currentDrop"))
        {
            var next=V.Get(state,"currentDrop").ValueKind==JsonValueKind.Object?RewardVisual.From(V.Get(state,"currentDrop"),"Twitch",_sync):null;
            if(next?.Id!=_selectedDrop?.Id||next?.CampaignId!=_selectedDrop?.CampaignId)_growthAt=null;
            _selectedDrop=next;
        }
        if(V.Has(state,"inventory")||V.Has(state,"campaigns"))Rebuild();
        ObserveOfficialProgress(state);
        Filter();
        NotifyRuntime();
    }
    internal void ApplyEvent(string name,JsonElement payload)
    {
        if(name=="claim_progress")ApplyBatchClaim(payload);
        if(name=="watch_schedule")
        {
            _prioritySummary=V.Has(payload,"rank")?$"当前观看优先级：{V.Number(payload,"rank"):0} · {V.Text(payload,"campaignName")}":"观看优先级：等待调度";
            Raise(nameof(PrioritySummary));
        }
        if(name=="current_channel")UpdateChannel(payload);
        if(name=="watch_request")UpdateWatch(payload);
        if(name=="connection_status"){_phase=V.Text(payload,"phase");if(_phase is "failed" or "waiting_retry" or "stopped"){_rejectedBefore=DateTimeOffset.UtcNow;InvalidateWatch();}}
        NotifyRuntime();Filter();
    }
    private void UpdateChannel(JsonElement channel)
    {
        var id=V.Text(channel,"id");
        if(id!=_channelId)InvalidateWatch();
        _channelId=id;_channel=V.Text(channel,"name","尚未选择频道");_game=V.Text(channel,"game");
    }
    private void UpdateWatch(JsonElement watch)
    {
        _watchChannelId=V.Text(watch,"channelId");_watchResult=V.Text(watch,"lastResult");
        if(_watchResult=="failed")_rejectedBefore=DateTimeOffset.UtcNow;
        _acceptedAt=DateTimeOffset.TryParse(V.Text(watch,"lastAcceptedAt",V.Text(watch,"lastSuccessAt")),out var at)?at:null;
    }
    private void Rebuild()
    {
        var rows=_inventory.ValueKind==JsonValueKind.Array?_inventory.EnumerateArray().ToArray():[];
        var campaigns=_campaigns.ValueKind==JsonValueKind.Array?_campaigns.EnumerateArray().ToArray():[];
        var ids=campaigns.Select(c=>V.Text(c,"id")).Concat(rows.Select(r=>V.Text(r,"campaignId"))).Where(id=>id.Length>0).Distinct().ToArray();
        foreach(var id in ids)
        {
            var campaign=campaigns.FirstOrDefault(c=>V.Text(c,"id")==id);
            var rewards=rows.Where(r=>V.Text(r,"campaignId")==id).Select(r=>RewardVisual.From(r,"Twitch",_sync)).ToArray();
            if(rewards.Length==0)rewards=V.Rows(campaign,"drops").Select(r=>RewardVisual.From(r,"Twitch",_sync) with{CampaignId=id,Campaign=V.Text(campaign,"name"),Game=V.Text(campaign,"game"),GameImageUrl=V.Text(campaign,"gameImageUrl")}).ToArray();
            rewards=rewards.Select(r=>r with{Expired=r.Expired||V.Bool(campaign,"expired"),Upcoming=r.Upcoming||V.Bool(campaign,"upcoming"),Eligible=r.Eligible??(V.Has(campaign,"eligible")?V.Bool(campaign,"eligible"):null)}).ToArray();
            rewards=rewards.Select(WithClaimAction).ToArray();
            if(_selectedDrop?.CampaignId==id&&rewards.FirstOrDefault(r=>r.Id==_selectedDrop.Id) is {} selected)_selectedDrop=selected;
            if(!_all.TryGetValue(id,out var group))_all[id]=group=new(id);
            group.Update(campaign,rewards);
        }
        foreach(var removed in _all.Keys.Except(ids).ToArray())_all.Remove(removed);
        Reconcile(Games,new[]{"全部游戏"}.Concat(_all.Values.Select(g=>g.Game).Where(g=>g.Length>0).Distinct().Order()).ToArray(),s=>s);
        Filter();
    }
    internal RewardVisual WithClaimAction(RewardVisual reward)
    {
        var key=reward.CampaignId+"/"+reward.Id;
        if(!_claimActions.TryGetValue(key,out var action))_claimActions[key]=action=new();
        action.Update(reward);
        return reward with { ClaimAction=action };
    }
    private void Filter()
    {
        RebuildPriorityRanks();
        bool Matches(CampaignRewardGroup g)=>_status switch
        {
            "unfinished"=>g.Drops.Any(d=>!d.WatchCompleted&&!d.Claimed),
            "completed"=>g.Drops.Any(d=>d.WatchCompleted),"claimable"=>g.Drops.Any(d=>d.CanClaim&&!d.Claimed),
            "claimed"=>g.Drops.Any(d=>d.Claimed),"active"=>!g.Expired&&!g.Upcoming,
            "ending"=>!g.Expired&&g.SortEnd<DateTimeOffset.Now.AddDays(2),_=>true
        };
        var filtered=_all.Values.Where(g=>(_scope=="all"||(g.Available||g.Drops.Any(d=>d.CanClaim&&!d.Claimed))&&(_scope!="priority"||g.Priority))&&Matches(g)&&
            (_gameFilter=="全部游戏"||g.Game==_gameFilter)&&(_search.Length==0||(g.Title+" "+g.Game+" "+string.Join(" ",g.Cards.Select(c=>c.Title))).Contains(_search,StringComparison.OrdinalIgnoreCase)));
        var ordered=_sort switch {"ending"=>filtered.OrderBy(g=>g.SortEnd),"name"=>filtered.OrderBy(g=>g.Game).ThenBy(g=>g.Title),"progress"=>filtered.OrderByDescending(g=>g.Percent),"status"=>filtered.OrderBy(g=>g.Status),_=>Rank(filtered)};
        Reconcile(Groups,ordered.ToArray(),g=>g.Id);
        var overview=Rank(_all.Values.Where(g=>!OverviewPriorityOnly||PriorityRank(g)<int.MaxValue)).OrderBy(g=>PriorityRank(g)).ToArray();
        Reconcile(OverviewGroups,overview,g=>g.Id);
        Reconcile(QueueGroups,Rank(_all.Values.Where(g=>g.Drops.Any(d=>!d.Claimed&&(d.CanClaim||!d.WatchCompleted&&!d.Expired&&!d.Upcoming&&d.Eligible!=false&&d.PreconditionsSatisfiedForMining!=false)))).ToArray(),g=>g.Id);
        foreach(var property in new[]{nameof(CountText),nameof(FilterSummary),nameof(EmptyText),nameof(EmptyVisibility),nameof(OverviewTitle),nameof(OverviewScope),nameof(OverviewEmptyText),nameof(OverviewEmptyVisibility),nameof(PrioritySetupVisibility),nameof(ViewAllVisibility),nameof(OverviewHint)})Raise(property);
    }
    private int PriorityRank(CampaignRewardGroup group)=>_priorityRanks.GetValueOrDefault(group.Id,int.MaxValue);
    private void RebuildPriorityRanks()
    {
        _priorityRanks.Clear();
        // Core currently stores exact official names. Resolve these through official ID/name pairs
        // when available; ambiguous names never identify two different games. No substring aliases.
        var names=_all.Values.Where(g=>g.GameId.Length>0).GroupBy(g=>g.Game.Trim(),StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g=>g.Key,g=>g.Select(c=>c.GameId).Distinct(StringComparer.Ordinal).ToArray(),StringComparer.OrdinalIgnoreCase);
        for(var index=0;index<_priorityGames.Count;index++)
        {
            var name=_priorityGames[index].Trim();if(name.Length==0)continue;
            names.TryGetValue(name,out var ids);
            foreach(var group in _all.Values)
            {
                var matches=group.GameId.Length>0 ? group.GameId==name||ids is {Length:1}&&group.GameId==ids[0] :
                    string.Equals(group.Game.Trim(),name,StringComparison.OrdinalIgnoreCase)&&ids is not {Length:>1};
                if(matches)_priorityRanks.TryAdd(group.Id,index);
            }
        }
    }
    private IOrderedEnumerable<CampaignRewardGroup> Rank(IEnumerable<CampaignRewardGroup> groups)=>groups
        .OrderByDescending(g=>HasWatchEvidence&&CurrentDrop?.CampaignId==g.Id)
        .ThenByDescending(g=>g.Available&&g.Drops.Any(d=>!d.WatchCompleted&&!d.Claimed&&d.PreconditionsSatisfiedForMining!=false))
        .ThenByDescending(g=>g.Drops.Any(d=>d.CanClaim&&!d.Claimed))
        .ThenBy(g=>g.Expired).ThenBy(g=>g.Upcoming).ThenBy(g=>g.SortEnd).ThenBy(g=>g.Id,StringComparer.Ordinal);
    private void InvalidateWatch(){_watchResult="";_acceptedAt=null;_growthAt=null;}
    private void ObserveOfficialProgress(JsonElement state)
    {
        var rows=V.Rows(state,"inventory").Concat(V.Get(state,"currentDrop").ValueKind==JsonValueKind.Object?[V.Get(state,"currentDrop")]:Array.Empty<JsonElement>());
        foreach(var row in rows.DistinctBy(r=>V.Text(r,"campaignId")+"/"+V.Text(r,"id")))
        {
            var drop=RewardVisual.From(row,"Twitch",_sync);var key=drop.CampaignId+"/"+drop.Id;
            if(_officialMinutes.TryGetValue(key,out var before)&&drop.Current>before&&IsWatchingDrop(drop)&&_inventorySource!="cache"&&
                DateTimeOffset.TryParse(_sync,out var synced)&&WatchEvidence.Recent(synced)&&synced>=_runStartedAt)_growthAt=DateTimeOffset.UtcNow;
            if(drop.Current<before&&CurrentDrop?.Id==drop.Id)_growthAt=null;
            _officialMinutes[key]=drop.Current;
        }
    }
    private void NotifyRuntime()
    {
        if(!_health.Running)InvalidateWatch();
        foreach(var property in new[]{nameof(Channel),nameof(Game),nameof(SyncText),nameof(DataFreshness),nameof(CurrentDrop),nameof(HasWatchEvidence),nameof(OfficialGrowthConfirmed),nameof(WatchState),nameof(CurrentSummary),nameof(CurrentSummaryVisibility),nameof(EstimateText)})Raise(property);
    }
    internal void RefreshClock()=>NotifyRuntime();
    internal static void Reconcile<T>(ObservableCollection<T> target,IReadOnlyList<T> wanted,Func<T,string> key)
    {
        for(var index=0;index<wanted.Count;index++)
        {
            if(index<target.Count&&key(target[index])==key(wanted[index]))continue;
            var existing=target.Select((item,i)=>(item,i)).FirstOrDefault(p=>key(p.item)==key(wanted[index]));
            if(existing.item is not null)target.Move(existing.i,index);else target.Insert(index,wanted[index]);
        }
        while(target.Count>wanted.Count)target.RemoveAt(target.Count-1);
    }
}
