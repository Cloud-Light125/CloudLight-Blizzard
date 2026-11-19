using System.Text.Json;
using System.Windows;

namespace CloudLightBlizzard.ViewModels.Visuals;

public sealed record BenefitVisual(string Id,string Name,string Type,string ImageUrl,string Brand)
{
    public string TypeText=>Type switch{"DIRECT_ENTITLEMENT"=>"游戏物品","BADGE"=>"徽章","EMOTE"=>"表情",_=>"奖励"};
}

/// <summary>Immutable, read-only UI projection. Official values never include timer estimates.</summary>
public sealed record RewardVisual
{
    public TwitchClaimAction? ClaimAction { get; init; }
    public string Id { get; init; } = "";
    public string Title { get; init; } = "尚未选择奖励";
    public string Campaign { get; init; } = "";
    public string CampaignId { get; init; } = "";
    public string Game { get; init; } = "";
    public string GameId { get; init; } = "";
    public string ImageUrl { get; init; } = "";
    public IReadOnlyList<BenefitVisual> Benefits { get; init; } = [];
    public IReadOnlyList<string> PreconditionDropIds { get; init; } = [];
    public bool? PreconditionsMet { get; init; }
    public bool? PreconditionsSatisfiedForMining { get; init; }
    public string GameImageUrl { get; init; } = "";
    public string Brand { get; init; } = "Twitch";
    public double Current { get; init; }
    public double Required { get; init; }
    public double Estimate { get; init; }
    public string Unit { get; init; } = "分钟";
    public string SyncAt { get; init; } = "";
    public bool Completed { get; init; }
    public bool Claimed { get; init; }
    public bool CanClaim { get; init; }
    public bool? Eligible { get; init; }
    public bool IsCurrent { get; init; }
    public bool Expired { get; init; }
    public bool Upcoming { get; init; }
    public bool IsRandom { get; init; }
    public bool OfficialProgressKnown { get; init; } = true;
    public string AccountId { get; init; } = "";
    public string EndsAt { get; init; } = "";
    public bool WatchCompleted => Completed || !IsRandom && OfficialProgressKnown && Required > 0 && Current >= Required;
    public double Percent => !IsRandom && OfficialProgressKnown && Required > 0 ? Math.Clamp(Current * 100 / Required, 0, 100) : 0;
    public string PercentText => !IsRandom && OfficialProgressKnown && Required > 0 ? $"{Percent:0.#}%" : "—";
    public string ProgressText => IsRandom ? "随机掉宝" : !OfficialProgressKnown ? "尚无账号官方进度" : Required > 0 ? $"{Current:0.#} / {Required:0.#} {Unit}" : "等待官方进度";
    public string RemainingText => IsRandom ? "参与不代表必定获奖" : !OfficialProgressKnown ? Required > 0 ? $"官方要求 {Required:0.#} 分钟 · 进度待确认" : "官方要求待确认" : Required > 0 ? $"还需 {Math.Max(0, Required - Current):0.#} {Unit}" : "所需进度尚未确认";
    public Visibility ProgressVisibility => !IsRandom && OfficialProgressKnown && Required > 0 ? Visibility.Visible : Visibility.Collapsed;
    public string EstimateText => Estimate > 0 ? $"本地预计新增约 {Estimate:0.#} 分钟 · 等待官方确认" : "官方进度与本地观察时长独立记录";
    public string StateText => Claimed ? "已领取" : CanClaim ? "官方标记可领取" : IsRandom ? "随机掉宝 · 参与不代表必定获奖" : !OfficialProgressKnown ? "官方进度待确认" : WatchCompleted ? "已完成 · 领取未开放" : Current > 0 ? "部分完成" : "尚未开始";
    public string StateKind => Claimed ? "claimed" : CanClaim ? "claimable" : WatchCompleted ? "completed" : IsRandom ? "random" : !OfficialProgressKnown ? "unknown" : Current>0 ? "progress" : "pending";
    public string StateBadge => StateKind switch {"claimed"=>"✓ 已领取","claimable"=>"● 待领取","completed"=>"✓ 已完成","progress"=>"部分完成","random"=>"随机型","unknown"=>"待确认",_=>"未开始"};
    public string EligibilityText => Expired ? "活动已结束" : Upcoming ? "活动未开始" : Eligible==false ? "当前不符合资格" : PreconditionsSatisfiedForMining==false ? "前置阶段未满足" : Eligible==true ? "可参与" : "资格状态待确认";
    public Visibility EligibilityVisibility => Expired||Upcoming||Eligible==false||PreconditionsSatisfiedForMining==false ? Visibility.Visible : Visibility.Collapsed;
    public string OfficialProgressText => IsRandom ? "随机掉宝 · 参与不代表必定获奖" : OfficialProgressKnown && Required>0 ? $"{(Brand=="Soop"?"官方 viewTime":"官方进度")}：{ProgressText}" : "等待官方进度确认";
    public string CompletionText => Claimed ? "✓ 已领取" : IsRandom ? "官方标记可领取" : !OfficialProgressKnown ? "官方标记已完成" : "100% 已完成";
    public Visibility IncompleteVisibility => WatchCompleted||Claimed||CanClaim ? Visibility.Collapsed : Visibility.Visible;
    public Visibility CompleteVisibility => IncompleteVisibility==Visibility.Visible ? Visibility.Collapsed : Visibility.Visible;
    public string PrerequisiteSummary => PreconditionsSatisfiedForMining==false ? "需前置阶段" : PreconditionDropIds.Count==0 ? "" : PreconditionsSatisfiedForMining==true ? "前置已满足" : "前置待确认";
    public string LocalEstimateText => Estimate>0 ? EstimateText : "";
    public Visibility StateExplanationVisibility => StateKind is "pending" or "progress" or "claimed" or "claimable" ? Visibility.Collapsed : Visibility.Visible;
    public string PrerequisiteText => PreconditionDropIds.Count == 0 ? "无前置阶段" : PreconditionsSatisfiedForMining == true ? "前置观看条件已满足" : "需要前置阶段 · 详见奖励详情";
    public string SyncText => DateTimeOffset.TryParse(SyncAt, out var at)
        ? $"上次官方同步 {at.ToLocalTime():HH:mm:ss}" : "尚无官方同步时间";
    public Visibility ClaimVisibility => CanClaim && !Claimed ? Visibility.Visible : Visibility.Collapsed;
    public string BenefitsText=>Benefits.Count==0?"官方未提供物品图片元数据":string.Join(" · ",Benefits.Select(b=>b.Name));
    public IReadOnlyList<RewardCardVisual> Cards => Benefits.Count==0 ? [new(Id,this,null)] : Benefits.Select((benefit,index)=>new RewardCardVisual(Id+"/"+benefit.Id+"/"+index,this,benefit)).ToArray();
    public string QualificationText=>EligibilityText+" · "+(PreconditionsSatisfiedForMining==false?"等待前置任务完成":PreconditionsSatisfiedForMining==true?"前置任务条件已满足":"前置条件以官方数据为准");

    internal static RewardVisual From(JsonElement item, string brand, string sync, bool current = false) => new()
    {
        Id = V.Text(item,"id",V.Text(item,"taskId")), Title=V.Text(item,"name",V.Text(item,"reward",V.Text(item,"rewardTitle","未命名奖励"))),
        Campaign=V.Text(item,"campaign",V.Text(item,"missionTitle")), CampaignId=V.Text(item,"campaignId"),
        Game=V.Text(item,"game"), GameId=V.Text(item,"gameId"), ImageUrl=V.Text(item,"imageUrl"), GameImageUrl=V.Text(item,"gameImageUrl"), Brand=brand,
        Benefits=V.Rows(item,"benefits").Select(b=>new BenefitVisual(V.Text(b,"id"),V.Text(b,"name"),V.Text(b,"type","UNKNOWN"),V.Text(b,"imageUrl"),brand)).ToArray(),
        PreconditionDropIds=V.Rows(item,"preconditionDropIds").Where(p=>p.ValueKind==JsonValueKind.String).Select(p=>p.GetString()??"").ToArray(),
        PreconditionsMet=V.Get(item,"preconditionsMet").ValueKind is JsonValueKind.True or JsonValueKind.False?V.Bool(item,"preconditionsMet"):null,
        PreconditionsSatisfiedForMining=V.Get(item,"preconditionsSatisfiedForMining").ValueKind is JsonValueKind.True or JsonValueKind.False?V.Bool(item,"preconditionsSatisfiedForMining"):null,
        Current=V.Number(item,"currentMinutes",V.Number(item,"current")), Required=V.Number(item,"requiredMinutes",V.Number(item,"limit")),
        Estimate=V.Number(item,"estimatedExtraMinutes"), Unit=brand == "Bilibili" ? "" : "分钟",
        SyncAt=V.Text(item,"lastInventorySyncAt",V.Text(item,"lastConfirmedAt",sync)),
        Completed=V.Bool(item,"completed"), Claimed=V.Bool(item,"claimed"), CanClaim=V.Bool(item,"canClaim",V.Bool(item,"claimable")),
        Eligible=V.Get(item,"eligible").ValueKind is JsonValueKind.True or JsonValueKind.False ? V.Bool(item,"eligible") : null, IsCurrent=current,
        Expired=V.Bool(item,"expired") || DateTimeOffset.TryParse(V.Text(item,"endsAt"),out var end)&&end<DateTimeOffset.UtcNow,
        Upcoming=V.Bool(item,"upcoming") || DateTimeOffset.TryParse(V.Text(item,"startsAt"),out var start)&&start>DateTimeOffset.UtcNow,
        EndsAt=V.Text(item,"endsAt"),
    };
}

public sealed record CampaignVisual(string Id, string Title, string Game, string ImageUrl, string Status, string EndsAt)
{
    public IReadOnlyList<RewardVisual> Rewards { get; init; } = [];
    public string EndText => DateTimeOffset.TryParse(EndsAt, out var at) ? $"结束于 {at.ToLocalTime():MM-dd HH:mm}" : "结束时间未提供";
}

public sealed record AccountVisual(string Id, string Name, bool IsPrimary, bool Running, bool LoggedIn, string Channel, string Task, RewardVisual? Reward, string ImageUrl)
{
    public SoopAccountEvidence? Evidence {get;init;}
    public string LoginText => Evidence?.LoginText ?? (LoggedIn ? "登录状态：已验证登录" : "登录状态：未验证");
    public string ChannelText => Evidence?.ChannelText ?? Channel;
    public string TargetText => Evidence?.TargetText ?? Task;
    public string HeartbeatText => Evidence?.HeartbeatText ?? "观看心跳：尚未发送";
    public string HeartbeatDetails => Evidence?.HeartbeatDetails ?? "";
    public string OfficialText => Evidence?.OfficialText ?? "官方进度状态：尚未读取";
    public IReadOnlyList<RewardVisual> Rewards {get;init;}=[];
    public string EmptyTaskText => Rewards.Count==0 ? LoggedIn ? "等待该账号官方任务刷新" : "连接账号后查看官方任务" : "";
    public DateTimeOffset? WatchAcceptedAt {get;init;}
    public bool OfficialGrowth {get;init;}
    public bool Historical {get;init;}
    public string ChannelId {get;init;}="";
    public string ConnectionText {get;init;}="等待合资格频道";
    public bool HasWatchEvidence=>LoggedIn&&Running&&!Historical&&(Evidence?.HeartbeatFresh ?? (WatchAcceptedAt is {} at&&WatchEvidence.Recent(at)))&&Rewards.Any(r=>!r.WatchCompleted&&!r.Claimed&&!r.Expired&&!r.Upcoming&&r.Eligible!=false);
    public string WorkerText=>Evidence?.WorkerText ?? (Running?"Worker 运行中":"Worker 已停止");
    public string StateText => Historical ? "历史官方快照 · 非实时观看" : !LoggedIn ? Evidence?.WatchText ?? "尚未登录" : !Running ? "已停止观看" :
        Evidence?.WatchText ?? (HasWatchEvidence ? OfficialGrowth ? "官方 viewTime 已确认增长" : "观看请求已接受 · 官方进度待确认" : ConnectionText);
    public string SyncText => Evidence?.SyncText ?? Reward?.SyncText ?? "尚无该账号官方同步";
    public string Phase {get;init;}="";
    public string RoleText => IsPrimary ? "主账号" : "独立账号";
    public string ProgressText => Reward?.ProgressText ?? "尚无该账号官方分钟";
    public double Percent => Reward?.Percent ?? 0;
    public Visibility StartVisibility=>Running?Visibility.Collapsed:Visibility.Visible;
    public Visibility StopVisibility=>Running?Visibility.Visible:Visibility.Collapsed;
}

internal static class V
{
    internal static bool Has(JsonElement item,string name) => item.ValueKind==JsonValueKind.Object && item.TryGetProperty(name,out _);
    internal static JsonElement Get(JsonElement item,string name) => Has(item,name) ? item.GetProperty(name) : default;
    internal static string Text(JsonElement item,string name,string fallback="") => Get(item,name) is var v && v.ValueKind is JsonValueKind.String or JsonValueKind.Number ? v.ToString() : fallback;
    internal static double Number(JsonElement item,string name,double fallback=0) => Get(item,name) is var v && v.ValueKind==JsonValueKind.Number && v.TryGetDouble(out var n) && double.IsFinite(n) ? Math.Max(0,n) : fallback;
    internal static bool Bool(JsonElement item,string name,bool fallback=false) => Get(item,name) is var v && v.ValueKind is JsonValueKind.True or JsonValueKind.False ? v.GetBoolean() : fallback;
    internal static IEnumerable<JsonElement> Rows(JsonElement item,string name) => Get(item,name) is var v && v.ValueKind==JsonValueKind.Array ? v.EnumerateArray() : [];
}
