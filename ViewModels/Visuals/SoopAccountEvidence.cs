using System.Text.Json;
using CloudLightBlizzard.Services.Drops;

namespace CloudLightBlizzard.ViewModels.Visuals;

/// <summary>Evidence is copied only from the account's correlated Worker payload.</summary>
public sealed record SoopAccountEvidence
{
    public string Authentication { get; init; } = "unverified";
    public WorkerLifecycle Worker { get; init; }
    public bool Recovering { get; init; }
    public string ChannelState { get; init; } = "未选择频道";
    public string ChannelId { get; init; } = "";
    public string ChannelName { get; init; } = "";
    public string Campaign { get; init; } = "";
    public string CampaignName { get; init; } = "";
    public string HeartbeatState { get; init; } = "not_sent";
    public string HeartbeatTime { get; init; } = "";
    public string HeartbeatUid { get; init; } = "";
    public string HeartbeatChannel { get; init; } = "";
    public string HeartbeatCampaign { get; init; } = "";
    public bool Correlated { get; init; }
    public string OfficialState { get; init; } = "unread";
    public string OfficialTime { get; init; } = "";
    public string OfficialError { get; init; } = "";
    public SoopQueueDiagnostics? Queue { get; init; }
    public string LoginText => "登录状态：" + (Authentication switch { "verified"=>"已验证登录", "verifying"=>"正在验证", "expired"=>"认证失效", "awaiting_login"=>"等待重新登录", "failed"=>"登录失败", _=>"未验证" });
    public string WorkerText => "Worker：" + (Worker switch { WorkerLifecycle.Starting=>Recovering?"恢复中":"启动中", WorkerLifecycle.Running=>"正在运行", WorkerLifecycle.Stopping=>"停止中", WorkerLifecycle.Crashed=>"异常退出", WorkerLifecycle.Stopped=>Recovering?"恢复中":"已停止", _=>"未启动" });
    public bool HeartbeatFresh => Correlated && !Recovering && Worker==WorkerLifecycle.Running && HeartbeatState == "success" && Fresh(HeartbeatTime,150);
    public bool OfficialFresh => OfficialState is not ("unread" or "failed" or "stale") && Fresh(OfficialTime,300);
    public bool GrowthConfirmed => OfficialFresh && OfficialState == "grown";
    public string ChannelText => $"直播间：{(ChannelId.Length == 0 ? "尚未选择" : ChannelId + (ChannelName.Length == 0 ? "" : " · " + ChannelName))} · {ChannelState}";
    public string TargetText => "目标活动：" + (Campaign.Length == 0 ? "尚未选择" : CampaignName.Length>0 ? CampaignName+"（ID: "+Campaign+"）" : Campaign);
    public string HeartbeatText => "观看心跳：" + (HeartbeatState switch { "sending"=>"正在发送", "success" when HeartbeatFresh=>"最近一次成功 " + Time(HeartbeatTime), "failure"=>"失败", "timeout"=>"超时", "expired" or "success"=>"已失效", _=>"尚未发送" });
    public string HeartbeatDetails => $"成功记录：{Time(HeartbeatTime)} · UID {Empty(HeartbeatUid)} · 直播间 {Empty(HeartbeatChannel)} · 活动 {Empty(HeartbeatCampaign)} · 请求{(HeartbeatFresh ? "未过期" : "无有效成功证据或已过期")}";
    public string OfficialText => "官方进度状态：" + (OfficialState switch { "unread"=>"尚未读取", "failed"=>"官方读取失败 · 保留上次值（已过期）", _ when !OfficialFresh=>"数据已过期", "grown"=>"官方进度已增长", "unchanged"=>"官方进度暂未增长", _=>"官方同步成功 · 增长尚未确认" });
    public string SyncText => "最近官方同步：" + Time(OfficialTime);
    public string WatchText => Authentication != "verified" ? "观看状态：等待认证"
        : Worker != WorkerLifecycle.Running ? "观看状态：等待 Worker"
        : HeartbeatFresh ? "观看心跳正常 · 官方分钟单独确认"
        : ChannelId.Length == 0 ? Queue?.Message ?? "观看状态：等待选台"
        : "观看状态：等待当前目标的成功心跳";
    private static string Empty(string s) => s.Length == 0 ? "—" : s;
    private static string Time(string s) => DateTimeOffset.TryParse(s,out var time) ? time.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss") : "尚无记录";
    private static bool Fresh(string s,int seconds) => DateTimeOffset.TryParse(s,out var at) && DateTimeOffset.UtcNow-at >= TimeSpan.Zero && DateTimeOffset.UtcNow-at <= TimeSpan.FromSeconds(seconds);
    internal static string ProgressCampaign(JsonElement progress,string uid)
    {
        var explicitId=V.Text(progress,"campaignId");
        if(explicitId.Length>0)return explicitId;
        // Preserve the established Worker currentProgress contract: its ID is
        // exactly UID:dropsIdx. Do not join by translated campaign titles.
        var id=V.Text(progress,"id");var prefix=uid+":";
        return id.StartsWith(prefix,StringComparison.Ordinal)?id[prefix.Length..]:"";
    }

    internal static SoopAccountEvidence From(JsonElement account, string uid, WorkerLifecycle worker, SoopAccountEvidence? old)
    {
        var heartbeat = V.Get(account,"heartbeatEvidence");
        var official = V.Get(account,"officialEvidence");
        var target = V.Get(account,"queueTarget");
        var channel = V.Has(account, "channelId") ? V.Text(account, "channelId") : old?.ChannelId ?? "";
        var campaign = V.Has(account, "queueTarget") ? V.Text(target, "campaignId") : old?.Campaign ?? "";
        var displayedCampaign = campaign.Length>0 ? campaign : V.Rows(account,"currentProgress").Concat(V.Rows(account,"officialProgress")).Select(p=>ProgressCampaign(p,uid)).FirstOrDefault(s=>s.Length>0)??"";
        var officialStatus = V.Text(official,"status",old?.OfficialState??"unread");
        if(officialStatus is "grown" or "unchanged" or "synced")
            officialStatus = V.Text(V.Get(official,"campaignStates"),displayedCampaign,"synced");
        var authentication = V.Text(account,"authenticationState",old?.Authentication??"unverified");
        var connected = V.Bool(account,"bridgeConnected");
        var result = new SoopAccountEvidence
        {
            Authentication=authentication, Worker=worker, ChannelId=channel, ChannelName=channel.Length == 0 ? "" : V.Text(account,"channelName",old?.ChannelName??""), Campaign=campaign, CampaignName=campaign.Length == 0 ? "" : V.Text(target,"campaignTitle",old?.CampaignName??""),
            ChannelState=channel.Length==0 ? V.Bool(account,"running") ? "等待合资格频道" : "未选择频道" : connected ? "连接成功" : V.Text(account,"status").Contains("离线") ? "频道离线" : V.Text(account,"status").Contains("失效") ? "频道失效" : V.Bool(account,"running") ? "正在连接" : "已选择频道",
            HeartbeatState=V.Text(heartbeat,"status",old?.HeartbeatState??"not_sent"), HeartbeatTime=V.Text(heartbeat,"lastSuccessAt",old?.HeartbeatTime??""),
            HeartbeatUid=V.Text(heartbeat,"uid",old?.HeartbeatUid??""), HeartbeatChannel=V.Text(heartbeat,"channelId",old?.HeartbeatChannel??""), HeartbeatCampaign=V.Text(heartbeat,"campaignId",old?.HeartbeatCampaign??""),
            OfficialState=official.ValueKind==JsonValueKind.Object && V.Text(official,"uid")!=uid ? "stale" : officialStatus, OfficialTime=V.Text(official,"syncedAt",old?.OfficialTime??""), OfficialError=V.Text(official,"error",old?.OfficialError??""),
            Queue = V.Has(account, "queueDiagnostics") ? SoopQueueDiagnostics.From(V.Get(account, "queueDiagnostics")) : old?.Queue
        };
        return result with { Correlated = authentication=="verified" && connected && worker==WorkerLifecycle.Running && V.Bool(account,"running") && !V.Bool(heartbeat,"requestExpired") && result.HeartbeatUid==uid && channel.Length>0 && result.HeartbeatChannel==channel && campaign.Length>0 && result.HeartbeatCampaign==campaign && V.Text(account,"broadcastNo").Length>0 && V.Text(heartbeat,"broadcastNo")==V.Text(account,"broadcastNo") };
    }
}

public sealed record SoopQueueDiagnostics(string Reason, string Message, string Counts, string Filters, string Details)
{
    internal static SoopQueueDiagnostics? From(JsonElement payload)
    {
        var reason = V.Text(payload, "reason");
        if (reason.Length == 0) return null;

        var activities = V.Get(payload, "activityFilters");
        var channels = V.Get(payload, "channelFilters");
        var errors = V.Get(payload, "sourceErrors");
        var blocks = V.Get(payload, "blockReasons");
        var errorCount = errors.ValueKind == JsonValueKind.Object
            ? errors.EnumerateObject().Sum(item => item.Value.GetInt32()) : 0;
        var counts = $"候选活动 {V.Number(payload, "candidateActivities"):0}/{V.Number(payload, "totalActivities"):0} · 已查活动 {V.Number(payload, "activitiesChecked"):0} · 发现在线频道 {V.Number(payload, "onlineChannels"):0}";
        var activityFilters = $"活动排除 · 未开放或已结束 {V.Number(activities,"inactive"):0} · 已完成 {V.Number(activities,"completed"):0} · 游戏筛选 {V.Number(activities,"gameFilter"):0} · 活动筛选 {V.Number(activities,"activityFilter"):0} · 账号资格 {V.Number(activities,"eligibility"):0} · 规则待确认 {V.Number(activities,"rules"):0} · 官方拒绝 {V.Number(activities,"refused"):0}";
        var channelFilters = $"频道检查 · 官方主播探测 {V.Number(payload,"officialProbes"):0} · 指定主播离线 {V.Number(channels,"officialOffline"):0} · 主播不匹配 {V.Number(channels,"streamerMismatch"):0} · 分类不匹配 {V.Number(channels,"categoryMismatch"):0} · 未标记 Drops {V.Number(channels,"dropsDisabled"):0} · 接口错误 {errorCount}";
        var details = blocks.ValueKind == JsonValueKind.Object
            ? string.Join(" · ", blocks.EnumerateObject().Select(item => $"{item.Name} {item.Value}")) : "";
        return new(reason, V.Text(payload, "message"), counts, $"{activityFilters}\n{channelFilters}", details);
    }
}
