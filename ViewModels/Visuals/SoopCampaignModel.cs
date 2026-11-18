using System.Collections.ObjectModel;
using System.Text.Json;
using CloudLightBlizzard.Services;

namespace CloudLightBlizzard.ViewModels.Visuals;

public sealed class SoopCampaign : ObservableObject
{
    public string Id { get; init; } = "";
    public string AccountId { get; init; } = "";
    public string GameId { get; init; } = "";
    public string Title { get; init; } = "";
    public string GameName { get; init; } = "";
    public string Lifecycle { get; init; } = "";
    public string Type { get; init; } = "";
    public string Summary { get; init; } = "";
    public string Details { get; init; } = "";
    public bool Available { get; init; }
    public bool Completed { get; init; }
    public bool Running { get; set; }
    public bool Expanded { get; set; }
    public string End { get; init; } = "";
    public string Start { get; init; } = "";
    public int RewardCount { get; init; }
    public string Eligibility { get; init; } = "";
    public bool Random { get; init; }
    public string Status => Completed ? "已完成" : Lifecycle switch { "ended"=>"已结束", "notYetOpen"=>"已安排", "active"=>"进行中", _=>"待确认" };
    public string PlanText => Running ? "当前观看目标" : Selected ? "已加入观看计划" : "未加入观看计划";
    public string TimeText => $"{Start} → {End}";
    internal Func<IReadOnlyList<SoopCampaignReward>>? LoadRewards;
    private IReadOnlyList<SoopCampaignReward>? _rewards;
    public IReadOnlyList<SoopCampaignReward> Rewards => _rewards ??= LoadRewards?.Invoke() ?? [];
    internal bool RewardsMaterialized => _rewards is not null;
    private bool _selected;
    public bool Selected { get => _selected; set { if (_selected == value) return; _selected = value; Raise(); Raise(nameof(PlanText)); Changed?.Invoke(); } }
    internal Action? Changed;
}

public sealed record SoopCampaignReward(string Name, string ImageUrl, RewardVisual Visual)
{ public string Progress => Visual.OfficialProgressText; }

public sealed class SoopGame : ObservableObject
{
    public string Id { get; init; } = "";
    public string Name { get; init; } = "";
    public ObservableCollection<SoopCampaign> Campaigns { get; } = [];
    public int Priority { get; internal set; }
    public string PriorityText => Selected ? $"已选游戏 · 优先级 {Priority}" : $"未选择 · 顺序 {Priority}";
    public string Counts => $"{Campaigns.Count} 个活动 · {Campaigns.Count(x=>x.Lifecycle=="active")} 进行中 · {Campaigns.Count(x => x.Available)} 可参与 · {Campaigns.Count(x => x.Completed)} 完成";
    internal void Notify() => Raise("");
    private bool _selected;
    public bool Selected { get => _selected; set { if (_selected == value) return; _selected = value; Raise(); Changed?.Invoke(); } }
    internal Action? Changed;
}

/// <summary>Saved watch intent is global; official progress belongs to exactly one UID.</summary>
public sealed class SoopCampaignModel : ObservableObject
{
    public ObservableCollection<SoopGame> Games { get; } = [];
    public ObservableCollection<string> Accounts { get; } = [];
    private readonly Dictionary<string, JsonElement> _rows = [];
    private readonly Dictionary<string, string> _targets = [];
    private readonly Dictionary<string,(string Signature,SoopCampaign Campaign)> _campaignCache = [];
    private readonly Dictionary<string,SoopGame> _gameCache = [];
    private AppSettings? _displaySettings;
    private int _displayScope;
    public int DisplayScope { get=>_displayScope; set { if(_displayScope==value)return;_displayScope=Math.Clamp(value,0,2);Raise();if(_displaySettings is not null){_displaySettings.SoopInventoryDisplayScope=_displayScope;_displaySettings.Save();}Updated?.Invoke(); } }
    internal void ConfigureDisplay(AppSettings settings){_displaySettings=settings;_displayScope=settings.SoopInventoryDisplayScope;Raise(nameof(DisplayScope));}
    private JsonElement _settings;
    private bool _applying;
    public bool Dirty { get; private set; }
    public event Action? Updated;
    private string _account = "";
    public string Account { get => _account; set { if (_account == value) return; RememberDraft(); _account = value ?? ""; Raise(); Rebuild(); } }
    private int _strategy;
    public int Strategy { get => _strategy; set { if (_strategy == value) return; _strategy = value; Raise(); MarkDirty(); } }
    private bool _explicit;
    public bool ExplicitSelection { get => _explicit; set { if (_explicit == value) return; _explicit = value; Raise(); MarkDirty(); } }
    public string ModeNotice => V.Text(_settings, "channel_mode", "smart") switch
    {
        "manual" => "手动频道优先；只有符合官方频道规则的活动才会标记正在执行。",
        "owesports" => "当前仅守望先锋赛事频道模式限制观看队列；其他游戏需更改频道模式后才能观看。",
        _ => "游戏顺序优先，再按活动策略选择；每账号只观看一个直播间。"
    };
    private Dictionary<string, object?>? _draft;
    internal void MarkDirty() { if (_applying) return; Dirty = true; Updated?.Invoke(); }
    private void RememberDraft() { if (Dirty && Games.Count > 0) _draft = SettingsPayload(); }
    public Dictionary<string, object?> SettingsPayload() => new()
    {
        ["selected_games"] = Games.Where(g => g.Selected).Select(g => g.Id).ToArray(),
        ["game_priority"] = Games.Select(g => g.Id).ToArray(),
        ["campaign_priorities"] = Games.ToDictionary(g => g.Id, g => g.Campaigns.Where(c => c.Selected).Select(c => c.Id).ToArray()),
        ["campaign_order"] = Games.ToDictionary(g => g.Id, g => g.Campaigns.Select(c => c.Id).ToArray()),
        ["campaign_selection_mode"] = ExplicitSelection ? "selected_only" : "legacy",
        ["campaign_sort_mode"] = new[] { "custom", "ending", "remaining", "most", "least" }[Math.Clamp(Strategy, 0, 4)]
    };
    public void Saved() { Dirty = false; _draft = null; }

    public void Apply(JsonElement state)
    {
        RememberDraft();
        if (state.TryGetProperty("settings", out var settings) && settings.TryGetProperty("campaign_selection_mode", out _)) _settings = settings.Clone();
        var incremental = V.Text(state, "uid");
        if (state.TryGetProperty("campaigns", out var rows) && rows.ValueKind == JsonValueKind.Array)
        {
            if (incremental.Length == 0) _rows.Clear();
            else foreach (var key in _rows.Where(p => V.Text(p.Value, "uid") == incremental).Select(p => p.Key).ToArray()) _rows.Remove(key);
            foreach (var row in rows.EnumerateArray())
            {
                var uid = V.Text(row, "uid"); var id = V.Text(row, "id");
                if (uid.Length > 0 && id.Length > 0 && (incremental.Length == 0 || incremental == uid)) _rows[uid + "\0" + id] = row.Clone();
            }
        }
        if (incremental.Length > 0 && state.TryGetProperty("queueTarget", out var target)) _targets[incremental] = V.Text(target, "campaignId");
        if (state.TryGetProperty("queueTargets", out var targets)) { _targets.Clear(); foreach (var item in targets.EnumerateObject()) _targets[item.Name] = V.Text(item.Value, "campaignId"); }
        Sync(Accounts,_rows.Values.Select(r=>V.Text(r,"uid")).Distinct().ToArray());
        foreach(var key in _campaignCache.Keys.Where(k=>!_rows.ContainsKey(k)).ToArray()) _campaignCache.Remove(key);
        if (!Accounts.Contains(_account)) { _account = Accounts.FirstOrDefault() ?? ""; Raise(nameof(Account)); }
        Rebuild();
    }
    private void Rebuild()
    {
        _applying = true;
        var expanded = Games.SelectMany(g=>g.Campaigns).Where(c=>c.Expanded).Select(c=>c.Id).ToHashSet();
        var settings = _draft is null ? _settings : JsonSerializer.SerializeToElement(_draft);
        var gameOrder = V.Rows(settings, "game_priority").Select(x => x.GetString() ?? "").ToList();
        var selected = V.Rows(settings, "selected_games").Select(x => x.GetString() ?? "").ToHashSet();
        var priorities = V.Get(settings, "campaign_priorities");
        var savedOrder = V.Get(settings, "campaign_order");
        _explicit = V.Text(settings, "campaign_selection_mode") == "selected_only";
        _strategy = Array.IndexOf(new[] { "custom", "ending", "remaining", "most", "least" }, V.Text(settings, "campaign_sort_mode", "custom"));
        var old = V.Text(_settings, "priority_mission_id", "auto");
        var rows = _rows.Values.Where(r => V.Text(r, "uid") == _account).ToList();
        // Keep saved unknown/temporarily absent activities editable without ever
        // borrowing another account's completion or eligibility.
        var gameIds = rows.Select(r => V.Text(r, "gameId")).Concat(gameOrder).Concat(selected).Distinct().Where(x => x.Length > 0);
        var games = new List<SoopGame>();
        foreach (var id in gameIds.OrderBy(id => gameOrder.Contains(id) ? gameOrder.IndexOf(id) : int.MaxValue))
        {
            var gameRows = rows.Where(r => V.Text(r, "gameId") == id).ToList();
            var order = priorities.ValueKind == JsonValueKind.Object && priorities.TryGetProperty(id, out var array) ? array.EnumerateArray().Select(x => x.GetString() ?? "").ToList() : [];
            var displayOrder = savedOrder.ValueKind == JsonValueKind.Object && savedOrder.TryGetProperty(id, out var saved) ? saved.EnumerateArray().Select(x => x.GetString() ?? "").ToList() : order;
            if (!_gameCache.TryGetValue(id,out var game)) _gameCache[id] = game = new SoopGame { Id = id, Name = V.Text(_rows.Values.FirstOrDefault(r=>V.Text(r,"gameId")==id),"gameName",id) };
            game.Selected = selected.Contains(id) || (!_explicit && gameRows.Any(r=>V.Text(r,"id")==old));
            game.Priority=games.Count+1;
            var campaigns=new List<SoopCampaign>();
            foreach (var activityId in gameRows.Select(r => V.Text(r, "id")).Concat(order).Concat(displayOrder).Distinct().OrderBy(x => displayOrder.Contains(x) ? displayOrder.IndexOf(x) : int.MaxValue))
            {
                var row = gameRows.FirstOrDefault(r => V.Text(r, "id") == activityId);
                var official = V.Bool(row, "officialProgressAvailable");
                var rewardCount=V.Rows(row,"items").Count();
                var random=V.Text(row,"giveCon") is "none" or "draw" || V.Text(row,"type").Contains("随机") || V.Text(row,"type").Contains("抽奖");
                var key=_account+"\0"+activityId;
                var signature=(row.ValueKind==JsonValueKind.Object?row.GetRawText():"")+"|"+(_targets.GetValueOrDefault(_account)==activityId);
                if(_campaignCache.TryGetValue(key,out var cached)&&cached.Signature==signature)
                {cached.Campaign.Selected=order.Contains(activityId)||(!_explicit&&activityId==old);campaigns.Add(cached.Campaign);continue;}
                var campaign = new SoopCampaign { Id = activityId, GameId = id, GameName = game.Name,
                    Expanded = expanded.Contains(activityId),
                    Title = V.Text(row, "title", "已保存活动 " + activityId), Selected = order.Contains(activityId) || (!_explicit && activityId == old),
                    Lifecycle = V.Bool(row,"ended") ? "ended" : V.Bool(row,"notYetOpen") ? "notYetOpen" : V.Bool(row,"active") ? "active" : "unknown", Type = V.Text(row, "type"), Available = V.Bool(row, "executable"), Completed = V.Bool(row, "completed"),
                    AccountId=_account, Random=random, RewardCount=rewardCount, Start=V.Text(row,"startTime",V.Text(row,"startDate","开始时间未提供")),
                    Eligibility=V.Text(row,"executionBlockReason") is {Length:>0} reason?reason:V.Bool(row,"executable")?"当前账号可按官方规则参与":"当前账号资格待确认",
                    Running = _targets.GetValueOrDefault(_account) == activityId, End = V.Text(row, "endTime", V.Text(row, "endDate","结束时间未提供")),
                    Summary = $"{V.Text(row,"type")} · {Status(row)} · {V.Number(row,"taskCount")} 个任务 · {rewardCount} 件奖励",
                    Details = $"{V.Text(row, "startTime", V.Text(row, "startDate"))} → {V.Text(row, "endTime", V.Text(row, "endDate"))}\n资格：{V.Text(row, "gameAccountBindingStatus")} · {V.Text(row, "executionBlockReason", "可按官方规则参与")}\n{V.Text(row, "randomNotice")}\n频道：{string.Join("、", V.Rows(row, "channelIds").Select(x => x.GetString()))} 分类：{string.Join("、", V.Rows(row, "categoryNames").Select(x => x.GetString()))}\n{V.Text(row, "rules")}\n活动 ID：{activityId} · 游戏 ID：{id}" };
                campaign.Changed = () => { ExplicitSelection = true; MarkDirty(); };
                campaign.LoadRewards=()=>V.Rows(row,"items").Select((item,index)=>
                {
                    var known=official&&!random&&V.Get(item,"viewMinutes").ValueKind==JsonValueKind.Number;
                    var visual=new RewardVisual {Brand="Soop",AccountId=campaign.AccountId,Id=activityId+"/"+index,CampaignId=activityId,Campaign=campaign.Title,GameId=id,Game=game.Name,
                        Title=V.Text(item,"name","未命名奖励"),ImageUrl=V.Text(item,"imageUrl"),IsRandom=random,OfficialProgressKnown=known,
                        Current=known?V.Number(item,"viewMinutes"):0,Required=random?0:V.Number(item,"requiredMinutes"),Completed=!random&&official&&V.Bool(item,"completed"),
                        Claimed=V.Bool(item,"claimed"),CanClaim=V.Bool(item,"claimable"),Eligible=campaign.Available?true:null,Expired=campaign.Lifecycle=="ended",Upcoming=campaign.Lifecycle=="notYetOpen"};
                    return new SoopCampaignReward(visual.Title,visual.ImageUrl,visual);
                }).ToArray();
                _campaignCache[key]=(signature,campaign);campaigns.Add(campaign);
            }
            game.Changed = () => { ExplicitSelection = true; MarkDirty(); };
            Sync(game.Campaigns,campaigns);game.Notify();games.Add(game);
        }
        Sync(Games,games);
        _applying = false;
        foreach (var name in new[] { nameof(Strategy), nameof(ExplicitSelection), nameof(ModeNotice) }) Raise(name);
        Updated?.Invoke();
    }
    private static string Status(JsonElement row) => V.Bool(row,"completed") ? "已完成" : V.Bool(row,"ended") ? "已结束" : V.Bool(row,"notYetOpen") ? "已安排" : V.Bool(row,"active") ? "进行中" : "待确认";
    internal static void Sync<T>(ObservableCollection<T> target,IReadOnlyList<T> desired)
    {
        var keep=desired.ToHashSet();for(int i=target.Count-1;i>=0;i--)if(!keep.Contains(target[i]))target.RemoveAt(i);
        for(int i=0;i<desired.Count;i++){if(i<target.Count&&EqualityComparer<T>.Default.Equals(target[i],desired[i]))continue;var found=target.IndexOf(desired[i]);if(found>=0)target.Move(found,i);else target.Insert(i,desired[i]);}
    }
    public void Move(object item, int direction)
    {
        if (item is SoopGame game) { var index = Games.IndexOf(game); Games.Move(index, Math.Clamp(direction == -99 ? 0 : index + direction, 0, Games.Count - 1)); }
        if (item is SoopCampaign campaign)
        { var list = Games.First(g => g.Id == campaign.GameId).Campaigns; var index = list.IndexOf(campaign); list.Move(index, Math.Clamp(direction == -99 ? 0 : index + direction, 0, list.Count - 1)); }
        MarkDirty();
    }
}
