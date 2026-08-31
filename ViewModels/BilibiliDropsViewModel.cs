using System.Collections.ObjectModel;
using System.IO;
using System.Text.Json;
using System.Windows;
using CloudLightBlizzard.Services.Drops;

namespace CloudLightBlizzard.ViewModels;

public sealed class BilibiliRoomViewModel : ObservableObject
{
    public long Id { get; init; }
    public string Name { get; init; } = "";
    public string Url { get; init; } = "";
    private bool _enabled = true;
    public bool Enabled { get => _enabled; set => Set(ref _enabled, value); }
    public string RoomIdText => Id.ToString();
    public string Status { get; init; } = "待发现";
    private string _sessionText = "—";
    public string SessionText { get => _sessionText; internal set => Set(ref _sessionText, value); }
}

public sealed class BilibiliActivityViewModel
{
    public string Id { get; init; } = "";
    public long RoomId { get; init; }
    public string Name { get; init; } = "";
    public string TaskText { get; init; } = "";
    public string Status { get; init; } = "";
    public string SourceText => "当前直播间自动发现";
}

public sealed class BilibiliTaskViewModel : ObservableObject
{
    public string Id { get; init; } = "";
    public string Name { get; init; } = "";
    public double Current { get; init; }
    public double Limit { get; init; }
    public double Percent { get; init; }
    public bool Completed { get; init; }
    public bool Claimed { get; init; }
    public bool Claimable { get; init; }
    public string Status { get; init; } = "进行中";
    public string ProgressText => $"{FormatNumber(Current)} / {FormatNumber(Limit)} · {Percent:0}%";
    public string OfficialText => "官方任务接口";
    public Visibility ClaimVisibility => Claimable ? Visibility.Visible : Visibility.Collapsed;
    private bool _isClaiming;
    public bool IsClaiming
    {
        get => _isClaiming;
        set
        {
            if (_isClaiming == value) return;
            Set(ref _isClaiming, value);
            Raise(nameof(CanClaim));
            Raise(nameof(ClaimButtonText));
        }
    }
    public bool CanClaim => Claimable && !IsClaiming;
    public string ClaimButtonText => IsClaiming ? "领取中…" : "领取奖励";

    private static string FormatNumber(double value) =>
        Math.Abs(value - Math.Round(value)) < 0.0001 ? ((long)Math.Round(value)).ToString() : value.ToString("0.##");
}

public sealed class BilibiliRewardViewModel
{
    public string TaskId { get; init; } = "";
    public string TaskName { get; init; } = "";
    public string Reward { get; init; } = "";
    public string State { get; init; } = "领取失败";
    public string Message { get; init; } = "";
    public string ClaimedAt { get; init; } = "";
}

public sealed class BilibiliSessionViewModel
{
    public string Id { get; init; } = "";
    public long RoomId { get; init; }
    public int SessionNo { get; init; }
    public string State { get; init; } = "";
    public string Detail { get; init; } = "";
    public int Failures { get; init; }
    public int ReconnectCount { get; init; }
}

/// <summary>Structured Bilibili-specific state projected from the Worker events.</summary>
public sealed class BilibiliDropsViewModel : ObservableObject
{
    private readonly DropsPlatformViewModel _platform;

    public BilibiliDropsViewModel(DropsPlatformViewModel platform) => _platform = platform;

    public DropsPlatformViewModel Platform => _platform;
    public ObservableCollection<BilibiliRoomViewModel> Rooms { get; } = new();
    public ObservableCollection<BilibiliActivityViewModel> Activities { get; } = new();
    public ObservableCollection<BilibiliTaskViewModel> Tasks { get; } = new();
    public ObservableCollection<BilibiliRewardViewModel> Rewards { get; } = new();
    public ObservableCollection<BilibiliSessionViewModel> Sessions { get; } = new();

    private bool _loggedIn;
    public bool LoggedIn { get => _loggedIn; private set { Set(ref _loggedIn, value); Raise(nameof(AccountStatus)); Raise(nameof(LoginVisibility)); Raise(nameof(LogoutVisibility)); } }
    private string _userName = "";
    public string UserName { get => _userName; private set => Set(ref _userName, value); }
    private long _uid;
    public long Uid { get => _uid; private set => Set(ref _uid, value); }
    public string AccountStatus => LoggedIn
        ? $"{UserName} · UID {Uid} · 已登录"
        : "尚未登录 Bilibili";
    public Visibility LoginVisibility => LoggedIn ? Visibility.Collapsed : Visibility.Visible;
    public Visibility LogoutVisibility => LoggedIn ? Visibility.Visible : Visibility.Collapsed;

    private string _qrState = "idle";
    public string QrState { get => _qrState; private set { Set(ref _qrState, value); Raise(nameof(QrStateText)); Raise(nameof(QrVisibility)); } }
    private string _qrMessage = "点击扫码登录后，二维码只会在本机生成。";
    public string QrStateText { get => _qrMessage; private set => Set(ref _qrMessage, value); }
    private string _qrImagePath = "";
    public string QrImagePath { get => _qrImagePath; private set { Set(ref _qrImagePath, value); Raise(nameof(QrImageVisibility)); } }
    public Visibility QrVisibility => QrState is "waiting_scan" or "scanned_pending" ? Visibility.Visible : Visibility.Collapsed;
    public Visibility QrImageVisibility => File.Exists(QrImagePath) ? Visibility.Visible : Visibility.Collapsed;

    private string _watchMode = "standard";
    public string WatchMode { get => _watchMode; set { Set(ref _watchMode, value); Raise(nameof(ModeText)); } }
    public string ModeText => WatchMode == "multi" ? "多线程加速模式" : "标准模式";
    private int _sessionsPerRoom = 1;
    public int SessionsPerRoom { get => _sessionsPerRoom; set { Set(ref _sessionsPerRoom, value); Raise(nameof(SessionEstimateText)); } }
    public string SessionEstimateText => $"启用房间 {Rooms.Count(room => room.Enabled)} × 每房间 {SessionsPerRoom} = 预计 {Rooms.Count(room => room.Enabled) * Math.Max(1, SessionsPerRoom)} 个 Session";
    private int _configuredSessions;
    public int ConfiguredSessions { get => _configuredSessions; private set { Set(ref _configuredSessions, value); Raise(nameof(SessionSummary)); } }
    private int _activeSessions;
    public int ActiveSessions { get => _activeSessions; private set { Set(ref _activeSessions, value); Raise(nameof(SessionSummary)); } }
    private int _connectingSessions;
    public int ConnectingSessions { get => _connectingSessions; private set => Set(ref _connectingSessions, value); }
    private int _retryingSessions;
    public int RetryingSessions { get => _retryingSessions; private set => Set(ref _retryingSessions, value); }
    private int _failedSessions;
    public int FailedSessions { get => _failedSessions; private set => Set(ref _failedSessions, value); }
    public string SessionSummary => $"{ActiveSessions} / {ConfiguredSessions} 活动 · {ConnectingSessions} 连接中 · {RetryingSessions} 等待重连 · {FailedSessions} 失败";

    private string _lastProgressAt = "";
    public string LastProgressAt { get => _lastProgressAt; private set => Set(ref _lastProgressAt, value); }
    private string _lastApiSuccessAt = "";
    public string LastApiSuccessAt { get => _lastApiSuccessAt; private set => Set(ref _lastApiSuccessAt, value); }
    private string _directNetworkText = "DIRECT · 不使用 CloudLight Blizzard 全局代理";
    public string DirectNetworkText { get => _directNetworkText; private set => Set(ref _directNetworkText, value); }
    private bool _autoClaim;
    public bool AutoClaim { get => _autoClaim; set => Set(ref _autoClaim, value); }
    private bool _taskNotifications = true;
    public bool TaskNotifications { get => _taskNotifications; set => Set(ref _taskNotifications, value); }
    private bool _autoDiscover = true;
    public bool AutoDiscover { get => _autoDiscover; set => Set(ref _autoDiscover, value); }
    private bool _reconnectEnabled = true;
    public bool ReconnectEnabled { get => _reconnectEnabled; set => Set(ref _reconnectEnabled, value); }
    private bool _autoRestore;
    public bool AutoRestore { get => _autoRestore; set => Set(ref _autoRestore, value); }
    private bool _autoResume;
    public bool AutoResume { get => _autoResume; set => Set(ref _autoResume, value); }
    private bool _notifierConfigured;
    public bool NotifierConfigured { get => _notifierConfigured; private set { Set(ref _notifierConfigured, value); Raise(nameof(NotifierText)); } }
    public string NotifierText => NotifierConfigured ? "第三方通知 URL 已加密保存" : "未配置第三方通知";

    public void ApplyState(JsonElement state)
    {
        if (state.ValueKind != JsonValueKind.Object) return;
        if (state.TryGetProperty("account", out var account) && account.ValueKind == JsonValueKind.Object)
            ApplyAccount(account);
        if (state.TryGetProperty("rooms", out var rooms)) ApplyRooms(rooms);
        if (state.TryGetProperty("activities", out var activities)) ApplyActivities(activities);
        if (state.TryGetProperty("tasks", out var tasks)) ApplyTasks(tasks);
        if (state.TryGetProperty("rewards", out var rewards)) ApplyRewards(rewards);
        if (state.TryGetProperty("settings", out var settings) && settings.ValueKind == JsonValueKind.Object)
            ApplySettings(settings);
        if (state.TryGetProperty("sessions", out var sessions)) ApplySessions(sessions);
        LastProgressAt = Text(state, "lastProgressAt");
        LastApiSuccessAt = Text(state, "lastApiSuccessAt");
        DirectNetworkText = "DIRECT · 不使用 CloudLight Blizzard 全局代理";
        Raise(nameof(SessionEstimateText));
    }

    public void HandleEvent(string name, JsonElement payload)
    {
        if (payload.ValueKind != JsonValueKind.Object) return;
        switch (name)
        {
            case "account": ApplyAccount(payload); break;
            case "qr_login":
                QrState = Text(payload, "state", "idle");
                QrStateText = Text(payload, "message", QrStateText);
                if (payload.TryGetProperty("imagePath", out _)) QrImagePath = Text(payload, "imagePath");
                if (QrState is "success" or "cancelled" or "expired")
                {
                    if (QrState != "success") QrImagePath = "";
                    Raise(nameof(QrVisibility));
                }
                if (payload.TryGetProperty("account", out var qrAccount)) ApplyAccount(qrAccount);
                break;
            case "room": if (payload.TryGetProperty("rooms", out var roomArray)) ApplyRooms(roomArray); break;
            case "activity":
                if (payload.TryGetProperty("activities", out var activityArray)) ApplyActivities(activityArray);
                break;
            case "task":
            case "progress":
                if (payload.TryGetProperty("tasks", out var taskArray)) ApplyTasks(taskArray);
                LastProgressAt = Text(payload, "lastProgressAt", LastProgressAt);
                LastApiSuccessAt = Text(payload, "lastApiSuccessAt", LastApiSuccessAt);
                break;
            case "reward": ApplyReward(payload); break;
            case "session": ApplySessions(payload); break;
            case "status":
                if (payload.TryGetProperty("sessions", out var statusSessions)) ApplySessions(statusSessions);
                DirectNetworkText = "DIRECT · 不使用 CloudLight Blizzard 全局代理";
                break;
        }
        Raise(nameof(SessionEstimateText));
    }

    private void ApplyAccount(JsonElement account)
    {
        if (account.ValueKind != JsonValueKind.Object) return;
        LoggedIn = Bool(account, "loggedIn");
        UserName = Text(account, "userName");
        Uid = Long(account, "uid");
    }

    private void ApplyRooms(JsonElement value)
    {
        if (value.ValueKind != JsonValueKind.Array) return;
        Rooms.Clear();
        foreach (var item in value.EnumerateArray())
            Rooms.Add(new BilibiliRoomViewModel
            {
                Id = Long(item, "id"), Name = Text(item, "name", "直播间"), Url = Text(item, "url"),
                Enabled = Bool(item, "enabled", true), Status = RoomStatus(Text(item, "liveStatus")),
            });
    }

    private void ApplyActivities(JsonElement value)
    {
        if (value.ValueKind != JsonValueKind.Array) return;
        Activities.Clear();
        foreach (var item in value.EnumerateArray())
        {
            var ids = item.TryGetProperty("taskIds", out var taskIds) && taskIds.ValueKind == JsonValueKind.Array
                ? string.Join(", ", taskIds.EnumerateArray().Select(id => id.ToString())) : "";
            Activities.Add(new BilibiliActivityViewModel
            {
                Id = Text(item, "id"), RoomId = Long(item, "roomId"), Name = Text(item, "name", "活动"),
                TaskText = string.IsNullOrWhiteSpace(ids) ? "未读取任务" : $"任务 {ids}", Status = Bool(item, "active") ? "当前活动" : "已发现",
            });
        }
    }

    private void ApplyTasks(JsonElement value)
    {
        if (value.ValueKind != JsonValueKind.Array) return;
        Tasks.Clear();
        foreach (var item in value.EnumerateArray())
            Tasks.Add(new BilibiliTaskViewModel
            {
                Id = Text(item, "id"), Name = Text(item, "name", "任务"), Current = Number(item, "current"),
                Limit = Number(item, "limit"), Percent = Number(item, "percent"), Completed = Bool(item, "completed"),
                Claimed = Bool(item, "claimed"), Claimable = Bool(item, "claimable"), Status = Text(item, "status", "进行中"),
            });
    }

    private void ApplyRewards(JsonElement value)
    {
        if (value.ValueKind != JsonValueKind.Array) return;
        Rewards.Clear();
        foreach (var item in value.EnumerateArray())
            Rewards.Add(ToReward(item));
    }

    private void ApplyReward(JsonElement item)
    {
        var row = ToReward(item);
        var existing = Rewards.FirstOrDefault(reward => reward.TaskId == row.TaskId);
        if (existing is not null) Rewards[Rewards.IndexOf(existing)] = row;
        else Rewards.Insert(0, row);
    }

    private void ApplySettings(JsonElement settings)
    {
        WatchMode = Text(settings, "watchMode", WatchMode);
        SessionsPerRoom = Math.Max(1, Int(settings, "sessionsPerRoom", SessionsPerRoom));
        AutoClaim = Bool(settings, "autoClaim", AutoClaim);
        TaskNotifications = Bool(settings, "taskNotifications", TaskNotifications);
        AutoDiscover = Bool(settings, "autoDiscover", AutoDiscover);
        ReconnectEnabled = Bool(settings, "reconnectEnabled", ReconnectEnabled);
        AutoRestore = Bool(settings, "autoRestore", AutoRestore);
        AutoResume = Bool(settings, "autoResumeDrops", AutoResume);
        NotifierConfigured = Bool(settings, "notifyUrlsConfigured", NotifierConfigured);
    }

    private void ApplySessions(JsonElement value)
    {
        if (value.ValueKind != JsonValueKind.Object) return;
        ConfiguredSessions = Int(value, "configuredSessions", ConfiguredSessions);
        ActiveSessions = Int(value, "activeSessions", ActiveSessions);
        ConnectingSessions = Int(value, "connectingSessions", ConnectingSessions);
        RetryingSessions = Int(value, "retryingSessions", RetryingSessions);
        FailedSessions = Int(value, "failedSessions", FailedSessions);
        if (!value.TryGetProperty("sessions", out var sessionArray) || sessionArray.ValueKind != JsonValueKind.Array) return;
        Sessions.Clear();
        foreach (var item in sessionArray.EnumerateArray())
            Sessions.Add(new BilibiliSessionViewModel
            {
                Id = Text(item, "id"), RoomId = Long(item, "roomId"), SessionNo = Int(item, "sessionNo"),
                State = Text(item, "state"), Detail = Text(item, "detail"), Failures = Int(item, "failures"),
                ReconnectCount = Int(item, "reconnectCount"),
            });
        foreach (var room in Rooms)
        {
            var count = Sessions.Count(session => session.RoomId == room.Id);
            room.SessionText = count == 0 ? "无活动 Session" : $"{count} 个 Session";
        }
    }

    private static BilibiliRewardViewModel ToReward(JsonElement item) => new()
    {
        TaskId = Text(item, "taskId"), TaskName = Text(item, "taskName", "任务"), Reward = Text(item, "reward"),
        State = Text(item, "state", "领取失败"), Message = Text(item, "message"), ClaimedAt = Text(item, "claimedAt"),
    };

    private static string RoomStatus(string status) => status switch { "1" => "直播中", "2" => "轮播中", _ => "待发现" };
    private static string Text(JsonElement owner, string name, string fallback = "") =>
        owner.TryGetProperty(name, out var value) && value.ValueKind != JsonValueKind.Null && value.ValueKind != JsonValueKind.Undefined
            ? value.ToString() : fallback;
    private static bool Bool(JsonElement owner, string name, bool fallback = false) =>
        owner.TryGetProperty(name, out var value) && value.ValueKind is JsonValueKind.True or JsonValueKind.False ? value.GetBoolean() : fallback;
    private static int Int(JsonElement owner, string name, int fallback = 0) =>
        owner.TryGetProperty(name, out var value) && value.TryGetInt32(out var result) ? result : fallback;
    private static long Long(JsonElement owner, string name) =>
        owner.TryGetProperty(name, out var value) && value.TryGetInt64(out var result) ? result : 0;
    private static double Number(JsonElement owner, string name) =>
        owner.TryGetProperty(name, out var value) && value.TryGetDouble(out var result) ? result : 0;
}
