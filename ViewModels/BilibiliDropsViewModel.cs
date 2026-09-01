using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Input;
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
    public string RoomText => RoomId > 0 ? $"Room ID {RoomId}" : "直播间未知";
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
    public string StateText => State switch
    {
        "active" or "running" => "活动",
        "connecting" => "连接中",
        "retrying" => "等待重连",
        "failed" => "失败",
        "stopped" => "已停止",
        _ => string.IsNullOrWhiteSpace(State) ? "未知" : State,
    };
    public string Detail { get; init; } = "";
    public int Failures { get; init; }
    public int ReconnectCount { get; init; }
}

internal sealed record BilibiliCommandHandlers(
    Func<Task> ScanQrLogin,
    Func<Task> CancelQr,
    Func<Task> ReacquireQr,
    Func<Task> ManualCookie,
    Func<Task> Logout,
    Func<Task> Discover,
    Func<Task> Refresh,
    Func<Task> AddRoom,
    Func<object?, Task> RemoveRoom,
    Func<object?, Task> SetRoomEnabled,
    Func<Task> Start,
    Func<Task> Stop,
    Func<Task> SaveSettings,
    Func<Task> ClearNotifier,
    Func<Task> RefreshSessions,
    Func<object?, Task> ClaimReward);

internal sealed class BilibiliAsyncCommand : ICommand
{
    private readonly Func<object?, bool> _canExecute;
    private Func<object?, Task>? _execute;
    private int _isExecuting;

    public BilibiliAsyncCommand(Func<object?, bool>? canExecute = null) =>
        _canExecute = canExecute ?? (_ => true);

    public event EventHandler? CanExecuteChanged;
    public event EventHandler<Exception>? Failed;

    public bool CanExecute(object? parameter) =>
        _execute is not null && Volatile.Read(ref _isExecuting) == 0 && _canExecute(parameter);

    public void Execute(object? parameter)
    {
        if (!CanExecute(parameter)) return;
        _ = ExecuteCoreAsync(parameter);
    }

    public void SetHandler(Func<object?, Task> handler)
    {
        _execute = handler;
        RaiseCanExecuteChanged();
    }

    public void RaiseCanExecuteChanged() => CanExecuteChanged?.Invoke(this, EventArgs.Empty);

    private async Task ExecuteCoreAsync(object? parameter)
    {
        if (Interlocked.Exchange(ref _isExecuting, 1) != 0) return;
        RaiseCanExecuteChanged();
        try
        {
            if (_execute is not null) await _execute(parameter);
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) { Failed?.Invoke(this, ex); }
        finally
        {
            Volatile.Write(ref _isExecuting, 0);
            RaiseCanExecuteChanged();
        }
    }
}

/// <summary>Structured Bilibili-specific state projected from the Worker events.</summary>
public sealed class BilibiliDropsViewModel : ObservableObject
{
    private readonly DropsPlatformViewModel _platform;
    private readonly List<BilibiliAsyncCommand> _commands = [];

    public BilibiliDropsViewModel(DropsPlatformViewModel platform)
    {
        _platform = platform;
        ScanQrLoginCommand = NewCommand();
        CancelQrCommand = NewCommand();
        ReacquireQrCommand = NewCommand();
        ManualCookieCommand = NewCommand();
        LogoutCommand = NewCommand();
        DiscoverCommand = NewCommand();
        RefreshCommand = NewCommand();
        AddRoomCommand = NewCommand();
        RemoveRoomCommand = NewCommand(parameter => parameter is BilibiliRoomViewModel);
        SetRoomEnabledCommand = NewCommand(parameter => parameter is BilibiliRoomViewModel);
        StartCommand = NewCommand();
        StopCommand = NewCommand();
        SaveSettingsCommand = NewCommand();
        ClearNotifierCommand = NewCommand();
        RefreshSessionsCommand = NewCommand();
        ClaimRewardCommand = NewCommand(parameter => parameter is BilibiliTaskViewModel task && task.CanClaim);
    }

    public DropsPlatformViewModel Platform => _platform;
    public ObservableCollection<BilibiliRoomViewModel> Rooms { get; } = new();
    public ObservableCollection<BilibiliActivityViewModel> Activities { get; } = new();
    public ObservableCollection<BilibiliTaskViewModel> Tasks { get; } = new();
    public ObservableCollection<BilibiliRewardViewModel> Rewards { get; } = new();
    public ObservableCollection<BilibiliSessionViewModel> Sessions { get; } = new();

    public ICommand ScanQrLoginCommand { get; }
    public ICommand CancelQrCommand { get; }
    public ICommand ReacquireQrCommand { get; }
    public ICommand ManualCookieCommand { get; }
    public ICommand LogoutCommand { get; }
    public ICommand DiscoverCommand { get; }
    public ICommand RefreshCommand { get; }
    public ICommand AddRoomCommand { get; }
    public ICommand RemoveRoomCommand { get; }
    public ICommand SetRoomEnabledCommand { get; }
    public ICommand StartCommand { get; }
    public ICommand StopCommand { get; }
    public ICommand SaveSettingsCommand { get; }
    public ICommand ClearNotifierCommand { get; }
    public ICommand RefreshSessionsCommand { get; }
    public ICommand ClaimRewardCommand { get; }

    private BilibiliAsyncCommand NewCommand(Func<object?, bool>? canExecute = null)
    {
        var command = new BilibiliAsyncCommand(canExecute);
        _commands.Add(command);
        return command;
    }

    internal void ConfigureCommands(BilibiliCommandHandlers handlers)
    {
        ((BilibiliAsyncCommand)ScanQrLoginCommand).SetHandler(_ => handlers.ScanQrLogin());
        ((BilibiliAsyncCommand)CancelQrCommand).SetHandler(_ => handlers.CancelQr());
        ((BilibiliAsyncCommand)ReacquireQrCommand).SetHandler(_ => handlers.ReacquireQr());
        ((BilibiliAsyncCommand)ManualCookieCommand).SetHandler(_ => handlers.ManualCookie());
        ((BilibiliAsyncCommand)LogoutCommand).SetHandler(_ => handlers.Logout());
        ((BilibiliAsyncCommand)DiscoverCommand).SetHandler(_ => handlers.Discover());
        ((BilibiliAsyncCommand)RefreshCommand).SetHandler(_ => handlers.Refresh());
        ((BilibiliAsyncCommand)AddRoomCommand).SetHandler(_ => handlers.AddRoom());
        ((BilibiliAsyncCommand)RemoveRoomCommand).SetHandler(handlers.RemoveRoom);
        ((BilibiliAsyncCommand)SetRoomEnabledCommand).SetHandler(handlers.SetRoomEnabled);
        ((BilibiliAsyncCommand)StartCommand).SetHandler(_ => handlers.Start());
        ((BilibiliAsyncCommand)StopCommand).SetHandler(_ => handlers.Stop());
        ((BilibiliAsyncCommand)SaveSettingsCommand).SetHandler(_ => handlers.SaveSettings());
        ((BilibiliAsyncCommand)ClearNotifierCommand).SetHandler(_ => handlers.ClearNotifier());
        ((BilibiliAsyncCommand)RefreshSessionsCommand).SetHandler(_ => handlers.RefreshSessions());
        ((BilibiliAsyncCommand)ClaimRewardCommand).SetHandler(handlers.ClaimReward);
    }

    internal event EventHandler<Exception>? CommandFailed
    {
        add
        {
            foreach (var command in _commands) command.Failed += value;
        }
        remove
        {
            foreach (var command in _commands) command.Failed -= value;
        }
    }

    private bool _loggedIn;
    public bool LoggedIn
    {
        get => _loggedIn;
        private set
        {
            Set(ref _loggedIn, value);
            Raise(nameof(AccountStatus));
            Raise(nameof(LoginStateText));
            Raise(nameof(LoginVisibility));
            Raise(nameof(LogoutVisibility));
            RaiseCommandsCanExecuteChanged();
        }
    }
    private string _userName = "";
    public string UserName
    {
        get => _userName;
        private set { Set(ref _userName, value); Raise(nameof(AccountStatus)); }
    }
    private long _uid;
    public long Uid
    {
        get => _uid;
        private set { Set(ref _uid, value); Raise(nameof(AccountStatus)); }
    }
    public string AccountStatus => LoggedIn
        ? $"{UserName} · UID {Uid} · 已登录"
        : "尚未登录";
    public string LoginStateText => LoggedIn ? "● 已登录" : "尚未登录";
    public Visibility LoginVisibility => LoggedIn ? Visibility.Collapsed : Visibility.Visible;
    public Visibility LogoutVisibility => LoggedIn ? Visibility.Visible : Visibility.Collapsed;

    private string _qrState = "idle";
    public string QrState
    {
        get => _qrState;
        private set
        {
            Set(ref _qrState, value);
            Raise(nameof(QrStateText));
            Raise(nameof(QrVisibility));
            Raise(nameof(QrAreaVisibility));
            Raise(nameof(QrRetryVisibility));
            Raise(nameof(QrCancelVisibility));
            RaiseCommandsCanExecuteChanged();
        }
    }
    private string _qrMessage = "点击扫码登录后，二维码只会在本机生成。";
    public string QrStateText { get => _qrMessage; private set => Set(ref _qrMessage, value); }
    private string _qrImagePath = "";
    public string QrImagePath
    {
        get => _qrImagePath;
        private set
        {
            Set(ref _qrImagePath, value);
            Raise(nameof(QrImageVisibility));
            Raise(nameof(QrAreaVisibility));
        }
    }
    public Visibility QrVisibility => QrCancelVisibility;
    public Visibility QrCancelVisibility => QrState is "waiting_scan" or "scanned_pending"
        ? Visibility.Visible : Visibility.Collapsed;
    public Visibility QrAreaVisibility => QrState is "waiting_scan" or "scanned_pending" or "expired" or "failed"
        ? Visibility.Visible : Visibility.Collapsed;
    public Visibility QrRetryVisibility => QrState is "expired" or "failed"
        ? Visibility.Visible : Visibility.Collapsed;
    public Visibility QrImageVisibility => File.Exists(QrImagePath) ? Visibility.Visible : Visibility.Collapsed;

    private string _watchMode = "standard";
    public string WatchMode
    {
        get => _watchMode;
        set
        {
            var normalized = value == "multi" ? "multi" : "standard";
            Set(ref _watchMode, normalized);
            Raise(nameof(ModeText));
            if (normalized == "standard") SessionsPerRoom = 1;
        }
    }
    public string ModeText => WatchMode == "multi" ? "多线程加速模式" : "标准模式";
    private int _sessionsPerRoom = 1;
    public int SessionsPerRoom
    {
        get => _sessionsPerRoom;
        set
        {
            var normalized = Math.Clamp(value, 1, 128);
            if (_sessionsPerRoom != normalized)
            {
                _sessionsPerRoom = normalized;
                Raise(nameof(SessionsPerRoom));
            }
            var text = normalized.ToString();
            if (_sessionsPerRoomText != text)
            {
                _sessionsPerRoomText = text;
                Raise(nameof(SessionsPerRoomText));
            }
            Raise(nameof(SessionEstimateText));
        }
    }
    private string _sessionsPerRoomText = "1";
    public string SessionsPerRoomText
    {
        get => _sessionsPerRoomText;
        set
        {
            if (_sessionsPerRoomText != value)
            {
                _sessionsPerRoomText = value;
                Raise(nameof(SessionsPerRoomText));
            }
            if (int.TryParse(value, out var sessions))
                SessionsPerRoom = sessions;
            Raise(nameof(SessionEstimateText));
        }
    }
    public string SessionEstimateText => $"启用房间 {Rooms.Count(room => room.Enabled)} × 每房间 {SessionsPerRoom} = 预计 {Rooms.Count(room => room.Enabled) * Math.Max(1, SessionsPerRoom)} 个 Session";
    private int _configuredSessions;
    public int ConfiguredSessions { get => _configuredSessions; private set { Set(ref _configuredSessions, value); Raise(nameof(SessionSummary)); } }
    private int _activeSessions;
    public int ActiveSessions { get => _activeSessions; private set { Set(ref _activeSessions, value); Raise(nameof(SessionSummary)); } }
    private int _connectingSessions;
    public int ConnectingSessions { get => _connectingSessions; private set { Set(ref _connectingSessions, value); Raise(nameof(SessionSummary)); } }
    private int _retryingSessions;
    public int RetryingSessions { get => _retryingSessions; private set { Set(ref _retryingSessions, value); Raise(nameof(SessionSummary)); } }
    private int _failedSessions;
    public int FailedSessions { get => _failedSessions; private set { Set(ref _failedSessions, value); Raise(nameof(SessionSummary)); } }
    public string SessionSummary => $"{ActiveSessions} / {ConfiguredSessions} 活动 · {ConnectingSessions} 连接中 · {RetryingSessions} 等待重连 · {FailedSessions} 失败";

    private bool _enabled;
    public bool Enabled { get => _enabled; set => Set(ref _enabled, value); }
    private string _roomReference = "";
    public string RoomReference { get => _roomReference; set => Set(ref _roomReference, value); }
    private string _roomName = "";
    public string RoomName { get => _roomName; set => Set(ref _roomName, value); }
    private string _taskIdsText = "";
    public string TaskIdsText { get => _taskIdsText; set => Set(ref _taskIdsText, value); }
    private string _reconnectDelayText = "8";
    public string ReconnectDelayText { get => _reconnectDelayText; set => Set(ref _reconnectDelayText, value); }
    private string _taskIntervalText = "30";
    public string TaskIntervalText { get => _taskIntervalText; set => Set(ref _taskIntervalText, value); }
    public int ReconnectDelay => int.TryParse(ReconnectDelayText, out var value) ? value : 0;
    public int TaskInterval => int.TryParse(TaskIntervalText, out var value) ? value : 0;

    private string _lastProgressAt = "";
    public string LastProgressAt { get => _lastProgressAt; private set => Set(ref _lastProgressAt, value); }
    private string _lastApiSuccessAt = "";
    public string LastApiSuccessAt { get => _lastApiSuccessAt; private set => Set(ref _lastApiSuccessAt, value); }
    private string _directNetworkText = "DIRECT · 不使用 CloudLight Blizzard 全局代理";
    public string DirectNetworkText { get => _directNetworkText; private set => Set(ref _directNetworkText, value); }
    private string _networkMode = "DIRECT";
    public string NetworkMode { get => _networkMode; private set => Set(ref _networkMode, value); }
    private bool _autoClaim;
    public bool AutoClaim { get => _autoClaim; set => Set(ref _autoClaim, value); }
    private bool _autoTaskProgress = true;
    public bool AutoTaskProgress { get => _autoTaskProgress; set => Set(ref _autoTaskProgress, value); }
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
        TaskIdsText = state.TryGetProperty("taskIds", out var taskIds) && taskIds.ValueKind == JsonValueKind.Array
            ? string.Join(", ", taskIds.EnumerateArray().Select(item => item.ToString()))
            : TaskIdsText;
        LastProgressAt = Text(state, "lastProgressAt");
        LastApiSuccessAt = Text(state, "lastApiSuccessAt");
        Enabled = Bool(state, "settings", "enabled", Enabled);
        NetworkMode = "DIRECT";
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
                    QrImagePath = "";
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
                NetworkMode = "DIRECT";
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
        foreach (var room in Rooms) room.PropertyChanged -= OnRoomPropertyChanged;
        Rooms.Clear();
        foreach (var item in value.EnumerateArray())
        {
            var room = new BilibiliRoomViewModel
            {
                Id = Long(item, "id"), Name = Text(item, "name", "直播间"), Url = Text(item, "url"),
                Enabled = Bool(item, "enabled", true), Status = RoomStatus(Text(item, "liveStatus")),
            };
            room.PropertyChanged += OnRoomPropertyChanged;
            Rooms.Add(room);
        }
        Raise(nameof(SessionEstimateText));
    }

    private void OnRoomPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(BilibiliRoomViewModel.Enabled)) Raise(nameof(SessionEstimateText));
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
        Enabled = Bool(settings, "enabled", Enabled);
        WatchMode = Text(settings, "watchMode", WatchMode);
        SessionsPerRoom = Math.Max(1, Int(settings, "sessionsPerRoom", SessionsPerRoom));
        if (WatchMode == "standard") SessionsPerRoom = 1;
        ReconnectDelayText = Int(settings, "reconnectDelay", ReconnectDelay).ToString();
        TaskIntervalText = Int(settings, "taskInterval", TaskInterval).ToString();
        AutoTaskProgress = Bool(settings, "autoTaskProgress", AutoTaskProgress);
        AutoClaim = Bool(settings, "autoClaim", AutoClaim);
        TaskNotifications = Bool(settings, "taskNotifications", TaskNotifications);
        AutoDiscover = Bool(settings, "autoDiscover", AutoDiscover);
        ReconnectEnabled = Bool(settings, "reconnectEnabled", ReconnectEnabled);
        AutoRestore = Bool(settings, "autoRestore", AutoRestore);
        AutoResume = Bool(settings, "autoResumeDrops", AutoResume);
        NotifierConfigured = Bool(settings, "notifyUrlsConfigured", NotifierConfigured);
        if (settings.TryGetProperty("taskIds", out var taskIds) && taskIds.ValueKind == JsonValueKind.Array)
            TaskIdsText = string.Join(", ", taskIds.EnumerateArray().Select(item => item.ToString()));
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

    internal void SetQrError(string message)
    {
        QrImagePath = "";
        QrState = "failed";
        QrStateText = string.IsNullOrWhiteSpace(message) ? "登录失败" : message;
    }

    private void RaiseCommandsCanExecuteChanged()
    {
        foreach (var command in _commands) command.RaiseCanExecuteChanged();
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
    private static bool Bool(JsonElement owner, string parent, string name, bool fallback)
    {
        return owner.TryGetProperty(parent, out var nested) && nested.ValueKind == JsonValueKind.Object
            ? Bool(nested, name, fallback)
            : fallback;
    }
    private static int Int(JsonElement owner, string name, int fallback = 0) =>
        owner.TryGetProperty(name, out var value) && value.TryGetInt32(out var result) ? result : fallback;
    private static long Long(JsonElement owner, string name) =>
        owner.TryGetProperty(name, out var value) && value.TryGetInt64(out var result) ? result : 0;
    private static double Number(JsonElement owner, string name) =>
        owner.TryGetProperty(name, out var value) && value.TryGetDouble(out var result) ? result : 0;
}
