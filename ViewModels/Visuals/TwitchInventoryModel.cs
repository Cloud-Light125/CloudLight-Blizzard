using System.Collections.ObjectModel;
using System.Text.Json;
using System.Windows;
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
    public double Percent => V.Has(_source,"progress") ? Math.Clamp(V.Number(_source,"progress")*100,0,100) : 0;

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
        foreach(var property in new[]{nameof(Title),nameof(Game),nameof(ImageUrl),nameof(Status),nameof(EndText),nameof(Summary),nameof(ProgressSummary),nameof(Percent)})Raise(property);
    }
}

/// <summary>Read-only inventory projection. Watching requires recent accepted requests for the actual channel.</summary>
public sealed class TwitchInventoryModel : ObservableObject
{
    private readonly DropsPlatformViewModel _health;
    private readonly Dictionary<string,CampaignRewardGroup> _all=new(StringComparer.Ordinal);
    private JsonElement _inventory, _campaigns;
    private string _channelId="", _watchChannelId="", _watchResult="", _sync="", _phase="", _inventorySource="";
    private DateTimeOffset? _acceptedAt;
    private bool _emptyPriorityOnly;
    private RewardVisual? _selectedDrop;
    private string _channel="尚未选择频道", _game="", _scope="available", _search="", _status="all", _sort="recommended", _gameFilter="全部游戏";
    public ObservableCollection<CampaignRewardGroup> Groups { get; } = [];
    public ObservableCollection<CampaignRewardGroup> OverviewGroups { get; } = [];
    public ObservableCollection<CampaignRewardGroup> QueueGroups { get; } = [];
    public ObservableCollection<string> Games { get; } = ["全部游戏"];
    public string Search { get=>_search; set { if(_search==value)return;Set(ref _search,value);Filter(); } }
    public string StatusFilter { get=>_status; set { if(_status==value)return;Set(ref _status,value??"all");Filter(); } }
    public string Sort { get=>_sort; set { if(_sort==value)return;Set(ref _sort,value??"recommended");Filter(); } }
    public string GameFilter { get=>_gameFilter; set { if(_gameFilter==value)return;Set(ref _gameFilter,value??"全部游戏");Filter(); } }
    public string Channel=>_channel;
    public string Game=>_game;
    public string SyncText=>(DateTimeOffset.TryParse(_sync,out var at)?$"官方同步 {at.ToLocalTime():HH:mm:ss}" : "尚无官方同步")+(_inventorySource=="cache"||_health.ConnectionState is DropsConnectionState.Failed or DropsConnectionState.Degraded or DropsConnectionState.WaitingRetry?" · 数据可能过期":"");
    public string DataFreshness=>_inventorySource=="cache"||_health.ConnectionState is DropsConnectionState.Failed or DropsConnectionState.Degraded or DropsConnectionState.WaitingRetry ? "保留上次官方确认值 · 数据可能过期" : "官方进度与本地估算分别显示";
    public RewardVisual? CurrentDrop=>_health.Running&&_selectedDrop is {WatchCompleted:false,Claimed:false,Expired:false,Upcoming:false}?_selectedDrop:null;
    public bool HasWatchEvidence=>_health.Running&&_channelId.Length>0&&_watchChannelId==_channelId&&_watchResult=="accepted"&&_acceptedAt is {} at&&DateTimeOffset.Now-at<TimeSpan.FromMinutes(3);
    public string WatchState=>_health.WorkerLifecycle==WorkerLifecycle.Crashed ? "Worker 已异常退出" : !_health.Running ? "已停止观看" :
        _health.ConnectionState is DropsConnectionState.Failed or DropsConnectionState.WaitingRetry ? "连接失败 · 等待恢复" :
        _channelId.Length==0 ? _emptyPriorityOnly ? "等待设置优先游戏" : _phase is "loading_campaigns" or "loading_inventory" ? "正在加载官方活动" : "等待合资格频道" :
        !HasWatchEvidence ? "频道已选择 · 观看请求待确认" : CurrentDrop is null ? "观看连接已确认 · 等待可获取奖励" : "正在观看 · 进度以官方确认为准";
    public string CurrentSummary=>CurrentDrop is {} drop ? $"{drop.Title} · 官方 {drop.ProgressText} · {drop.PercentText} · {drop.RemainingText}" : "当前没有正在积累进度的 Drop";
    public string EstimateText=>CurrentDrop is {Estimate:>0} drop?drop.EstimateText:"";
    public string CountText=>$"{_all.Count} 个活动 · {_all.Values.Sum(g=>g.Cards.Count)} 件物品 · {_all.Values.Sum(g=>g.Drops.Count(d=>d.CanClaim&&!d.Claimed))} 可领取";
    public string FilterSummary=>$"筛选后 {Groups.Count} 个活动 · {Groups.Sum(g=>g.Cards.Count)} 件物品";
    public string EmptyText=>_all.Count==0 ? "尚未读取到官方活动，请验证账号并刷新。" : "当前筛选无结果 · 可调整显示范围、游戏或状态筛选。";
    public Visibility EmptyVisibility=>Groups.Count==0?Visibility.Visible:Visibility.Collapsed;
    public string OverviewTitle=>CurrentDrop is not null ? "当前活动的全部奖励" : "奖励背包 · 可参与与待处理活动";

    internal TwitchInventoryModel(DropsPlatformViewModel health)
    {
        _health=health;
        health.PropertyChanged+=(_,_)=>NotifyRuntime();
    }
    internal void SetScope(string scope){_scope=scope;Filter();}
    internal void Apply(JsonElement state)
    {
        if(V.Has(state,"inventory"))_inventory=V.Get(state,"inventory").Clone();
        if(V.Has(state,"campaigns"))_campaigns=V.Get(state,"campaigns").Clone();
        if(V.Has(state,"lastInventorySyncAt"))_sync=V.Text(state,"lastInventorySyncAt");
        if(V.Has(state,"inventorySource"))_inventorySource=V.Text(state,"inventorySource");
        if(V.Has(state,"connectionState"))_phase=V.Text(state,"connectionState");
        if(V.Has(state,"settings")){var settings=V.Get(state,"settings");_emptyPriorityOnly=V.Text(settings,"priority_mode")=="PRIORITY_ONLY"&&!V.Rows(settings,"priority").Any();}
        if(V.Has(state,"currentChannel"))UpdateChannel(V.Get(state,"currentChannel"));
        if(V.Has(state,"watchRequests"))UpdateWatch(V.Get(state,"watchRequests"));
        if(V.Has(state,"currentDrop"))_selectedDrop=V.Get(state,"currentDrop").ValueKind==JsonValueKind.Object?RewardVisual.From(V.Get(state,"currentDrop"),"Twitch",_sync):null;
        if(V.Has(state,"inventory")||V.Has(state,"campaigns"))Rebuild();
        NotifyRuntime();
    }
    internal void ApplyEvent(string name,JsonElement payload)
    {
        if(name=="current_channel")UpdateChannel(payload);
        if(name=="watch_request")UpdateWatch(payload);
        if(name=="connection_status")_phase=V.Text(payload,"phase");
        NotifyRuntime();
    }
    private void UpdateChannel(JsonElement channel)
    {
        var id=V.Text(channel,"id");
        if(id!=_channelId){_watchResult="";_acceptedAt=null;}
        _channelId=id;_channel=V.Text(channel,"name","尚未选择频道");_game=V.Text(channel,"game");
    }
    private void UpdateWatch(JsonElement watch)
    {
        _watchChannelId=V.Text(watch,"channelId");_watchResult=V.Text(watch,"lastResult");
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
            if(!_all.TryGetValue(id,out var group))_all[id]=group=new(id);
            group.Update(campaign,rewards);
        }
        foreach(var removed in _all.Keys.Except(ids).ToArray())_all.Remove(removed);
        Reconcile(Games,new[]{"全部游戏"}.Concat(_all.Values.Select(g=>g.Game).Where(g=>g.Length>0).Distinct().Order()).ToArray(),s=>s);
        Filter();
    }
    private void Filter()
    {
        bool Matches(CampaignRewardGroup g)=>_status switch
        {
            "unfinished"=>g.Drops.Any(d=>!d.WatchCompleted&&!d.Claimed),
            "completed"=>g.Drops.Any(d=>d.WatchCompleted),"claimable"=>g.Drops.Any(d=>d.CanClaim&&!d.Claimed),
            "claimed"=>g.Drops.Any(d=>d.Claimed),"active"=>!g.Expired&&!g.Upcoming,
            "ending"=>!g.Expired&&g.SortEnd<DateTimeOffset.Now.AddDays(2),_=>true
        };
        var filtered=_all.Values.Where(g=>(_scope=="all"||g.Available&&(_scope!="priority"||g.Priority))&&Matches(g)&&
            (_gameFilter=="全部游戏"||g.Game==_gameFilter)&&(_search.Length==0||(g.Title+" "+g.Game+" "+string.Join(" ",g.Cards.Select(c=>c.Title))).Contains(_search,StringComparison.OrdinalIgnoreCase)));
        var ordered=_sort switch {"ending"=>filtered.OrderBy(g=>g.SortEnd),"name"=>filtered.OrderBy(g=>g.Game).ThenBy(g=>g.Title),_=>Rank(filtered)};
        Reconcile(Groups,ordered.ToArray(),g=>g.Id);
        var overview=Rank(_all.Values).Take(4).ToArray();
        Reconcile(OverviewGroups,overview,g=>g.Id);
        Reconcile(QueueGroups,Rank(_all.Values.Where(g=>g.Drops.Any(d=>!d.Claimed&&(d.CanClaim||!d.WatchCompleted&&!d.Expired&&!d.Upcoming&&d.Eligible!=false&&d.PreconditionsSatisfiedForMining!=false)))).ToArray(),g=>g.Id);
        foreach(var property in new[]{nameof(CountText),nameof(FilterSummary),nameof(EmptyText),nameof(EmptyVisibility),nameof(OverviewTitle)})Raise(property);
    }
    private IOrderedEnumerable<CampaignRewardGroup> Rank(IEnumerable<CampaignRewardGroup> groups)=>groups
        .OrderByDescending(g=>CurrentDrop?.CampaignId==g.Id)
        .ThenByDescending(g=>g.Available&&g.Drops.Any(d=>!d.WatchCompleted&&!d.Claimed&&d.PreconditionsSatisfiedForMining!=false))
        .ThenByDescending(g=>g.Drops.Any(d=>d.CanClaim&&!d.Claimed))
        .ThenBy(g=>g.Expired).ThenBy(g=>g.SortEnd);
    private void NotifyRuntime()
    {
        if(!_health.Running){_watchResult="";_acceptedAt=null;}
        foreach(var property in new[]{nameof(Channel),nameof(Game),nameof(SyncText),nameof(DataFreshness),nameof(CurrentDrop),nameof(HasWatchEvidence),nameof(WatchState),nameof(CurrentSummary),nameof(EstimateText)})Raise(property);
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
