using System.Collections.ObjectModel;
using System.Text.Json;
using System.Windows;
using CloudLightBlizzard.Services.Drops;

namespace CloudLightBlizzard.ViewModels;

public sealed class DropsRow : ObservableObject
{
    public string Id { get; init; } = "";
    public string Primary { get; init; } = "";
    public string Secondary { get; init; } = "";
    public string Status { get; init; } = "";
    public string Detail { get; init; } = "";
    public bool Enabled { get; init; }
    public double Progress { get; init; }
    public JsonElement Payload { get; init; }

    public bool Completed { get; init; }
    public bool Claimed { get; init; }
    public bool CanClaim { get; init; }
    public Visibility TwitchClaimVisibility => Completed && !Claimed && CanClaim
        ? Visibility.Visible : Visibility.Collapsed;
    private bool _isClaiming;
    public bool IsClaiming
    {
        get => _isClaiming;
        private set
        {
            if (_isClaiming == value) return;
            Set(ref _isClaiming, value);
            Raise(nameof(CanStartTwitchClaim));
            Raise(nameof(TwitchClaimButtonText));
        }
    }
    public bool CanStartTwitchClaim => Completed && !Claimed && CanClaim && !IsClaiming;
    public string TwitchClaimButtonText => IsClaiming ? "领取中…" : "领取";

    public bool TryBeginTwitchClaim()
    {
        if (!CanStartTwitchClaim) return false;
        IsClaiming = true;
        return true;
    }

    public void EndTwitchClaim() => IsClaiming = false;
}

public sealed class SoopProgressRow
{
    public string Id { get; init; } = "";
    public string Account { get; init; } = "";
    public string Channel { get; init; } = "";
    public string Campaign { get; init; } = "";
    public string Reward { get; init; } = "";
    public int CurrentMinutes { get; init; }
    public int RequiredMinutes { get; init; }
    public double Percent { get; init; }
    public string ProgressText => $"{CurrentMinutes} / {RequiredMinutes} 分钟 · {Percent:0}%";
}

public sealed class DropsQuickStartStep : ObservableObject
{
    public string Number { get; }
    public string Title { get; }
    public string Description { get; }
    public string ActionKey { get; }
    private string _stateKind = "incomplete";
    public string StateKind { get => _stateKind; private set => Set(ref _stateKind, value); }
    private string _stateText = "○ 未完成";
    public string StateText { get => _stateText; private set => Set(ref _stateText, value); }
    private string _actionText = "";
    public string ActionText { get => _actionText; private set { Set(ref _actionText, value); Raise(nameof(ActionVisibility)); } }
    public Visibility ActionVisibility => string.IsNullOrWhiteSpace(ActionText) ? Visibility.Collapsed : Visibility.Visible;
    public bool Satisfied { get; private set; }

    public DropsQuickStartStep(string number, string title, string description, string actionKey)
    {
        Number = number;
        Title = title;
        Description = description;
        ActionKey = actionKey;
    }

    public void Update(string stateKind, string stateText, bool satisfied, string actionText = "")
    {
        StateKind = stateKind;
        StateText = stateText;
        Satisfied = satisfied;
        ActionText = actionText;
    }
}

public sealed class DropsQuickStartGuide : ObservableObject
{
    public ObservableCollection<DropsQuickStartStep> Steps { get; }
    private string _summary = "快速开始";
    public string Summary { get => _summary; private set => Set(ref _summary, value); }
    private bool _isExpanded = true;
    public bool IsExpanded { get => _isExpanded; set => Set(ref _isExpanded, value); }

    public DropsQuickStartGuide(params DropsQuickStartStep[] steps) => Steps = new(steps);

    public void RefreshSummary()
    {
        var completed = Steps.Count(step => step.Satisfied);
        Summary = $"快速开始 · {completed} / {Steps.Count} 已完成";
        if (completed == Steps.Count) IsExpanded = false;
    }
}

public enum TwitchCampaignScope { Available, Priority, All }

public enum TwitchConnectionStage
{
    Unconnected,
    WorkerStarting,
    Connecting,
    CheckingSession,
    RestoringSession,
    RequestingAuthorization,
    WaitingAuthorization,
    LoginSucceeded,
    LoadingCampaigns,
    LoadingChannels,
    ConnectingRealtime,
    Connected,
    Running,
    Reconnecting,
    Slow,
    RetryWaiting,
    AuthenticationExpired,
    NetworkFailed,
    ProxyUnavailable,
    SslCertificateError,
    SslRuntimeError,
    WorkerError,
    Stopped,
}

public enum TwitchConnectionFailureKind
{
    None,
    Timeout,
    Dns,
    Network,
    Proxy,
    ProxyAndDirect,
    SslCertificate,
    SslRuntime,
    Authentication,
    Worker,
}

public sealed class DropsPlatformViewModel : ObservableObject
{
    public DropsPlatform Platform { get; }
    public string Name { get; }
    private string _status = "未运行";
    public string Status { get => _status; set => Set(ref _status, value); }
    private string _summary = "正在读取状态";
    public string Summary { get => _summary; set => Set(ref _summary, value); }
    private bool _running;
    public bool Running { get => _running; set { Set(ref _running, value); Raise(nameof(StartVisibility)); Raise(nameof(StopVisibility)); } }
    public Visibility StartVisibility => Running ? Visibility.Collapsed : Visibility.Visible;
    public Visibility StopVisibility => Running ? Visibility.Visible : Visibility.Collapsed;

    public DropsPlatformViewModel(DropsPlatform platform, string name) { Platform = platform; Name = name; }
}

public sealed class DropsViewModel : ObservableObject, IDisposable
{
    private enum SoopFailureKind
    {
        TransientNetwork,
        Authentication,
        RuntimeDependency,
        Configuration,
        Unknown,
    }

    private readonly DropsHostService _host;
    public DropsPlatformViewModel Soop { get; } = new(DropsPlatform.Soop, "SOOP");
    public DropsPlatformViewModel YouTube { get; } = new(DropsPlatform.YouTube, "YouTube");
    public DropsPlatformViewModel Twitch { get; } = new(DropsPlatform.Twitch, "Twitch");
    public IReadOnlyList<DropsPlatformViewModel> Platforms { get; }
    public ObservableCollection<DropsRow> Accounts { get; } = new();
    public ObservableCollection<DropsRow> Tasks { get; } = new();
    public ObservableCollection<DropsRow> Inventory { get; } = new();
    public ObservableCollection<DropsRow> Channels { get; } = new();
    public ObservableCollection<DropsRow> History { get; } = new();
    public ObservableCollection<SoopProgressRow> SoopCurrentProgress { get; } = new();
    public ObservableCollection<string> TwitchAvailableGames { get; } = new();
    public ObservableCollection<string> TwitchPriorityChoices { get; } = new();
    public ObservableCollection<string> TwitchExcludeChoices { get; } = new();
    public ObservableCollection<string> TwitchPriorityGames { get; } = new();
    public ObservableCollection<string> TwitchExcludedGames { get; } = new();
    private readonly List<JsonElement> _twitchCampaigns = [];
    private TwitchCampaignScope _twitchCampaignScope = TwitchCampaignScope.Available;
    private bool _twitchCampaignScopeInitialized;
    private bool _twitchLoggedIn;
    public bool IsTwitchLoggedIn => _twitchLoggedIn;
    public Visibility TwitchLoginVisibility => _twitchLoggedIn ? Visibility.Collapsed : Visibility.Visible;
    public Visibility TwitchLogoutVisibility => Visibility.Visible;
    private string _twitchAuthState = "logged_out";
    public string TwitchAuthState => _twitchAuthState;
    private string _twitchAuthorizationUrl = "";
    public string TwitchAuthorizationUrl
    {
        get => _twitchAuthorizationUrl;
        private set
        {
            if (_twitchAuthorizationUrl == value) return;
            Set(ref _twitchAuthorizationUrl, value);
            Raise(nameof(TwitchLoginButtonText));
        }
    }
    private string _twitchAuthorizationCode = "";
    public string TwitchAuthorizationCode { get => _twitchAuthorizationCode; private set => Set(ref _twitchAuthorizationCode, value); }
    public Visibility TwitchAuthorizationVisibility => _twitchAuthState == "authorization_required"
        ? Visibility.Visible : Visibility.Collapsed;
    private TwitchConnectionStage _twitchConnectionStage = TwitchConnectionStage.Unconnected;
    public TwitchConnectionStage TwitchConnectionStage => _twitchConnectionStage;
    private DateTimeOffset _twitchStageStartedAt = DateTimeOffset.Now;
    public DateTimeOffset TwitchStageStartedAt => _twitchStageStartedAt;
    private DateTimeOffset? _lastTwitchConnectedAt;
    public DateTimeOffset? LastTwitchConnectedAt => _lastTwitchConnectedAt;
    private DateTimeOffset? _lastSoopSuccessfulAt;
    private DateTimeOffset? _lastYouTubeSuccessfulAt;
    private string _soopLastSuccessText = "";
    public string SoopLastSuccessText { get => _soopLastSuccessText; private set { Set(ref _soopLastSuccessText, value); Raise(nameof(SoopLastSuccessVisibility)); } }
    public Visibility SoopLastSuccessVisibility => string.IsNullOrWhiteSpace(SoopLastSuccessText) ? Visibility.Collapsed : Visibility.Visible;
    private string _twitchLastSuccessText = "";
    public string TwitchLastSuccessText { get => _twitchLastSuccessText; private set { Set(ref _twitchLastSuccessText, value); Raise(nameof(TwitchLastSuccessVisibility)); } }
    public Visibility TwitchLastSuccessVisibility => string.IsNullOrWhiteSpace(TwitchLastSuccessText) ? Visibility.Collapsed : Visibility.Visible;
    private string _youtubeLastSuccessText = "";
    public string YouTubeLastSuccessText { get => _youtubeLastSuccessText; private set { Set(ref _youtubeLastSuccessText, value); Raise(nameof(YouTubeLastSuccessVisibility)); } }
    public Visibility YouTubeLastSuccessVisibility => string.IsNullOrWhiteSpace(YouTubeLastSuccessText) ? Visibility.Collapsed : Visibility.Visible;
    private string _twitchLastError = "";
    public string TwitchLastError => _twitchLastError;
    private TwitchConnectionFailureKind _twitchLastFailureKind;
    public TwitchConnectionFailureKind TwitchLastConnectionFailureKind => _twitchLastFailureKind;
    private bool _twitchRetryBlocked;
    private bool _isClearingTwitchLogin;
    public bool IsClearingTwitchLogin
    {
        get => _isClearingTwitchLogin;
        private set
        {
            if (_isClearingTwitchLogin == value) return;
            Set(ref _isClearingTwitchLogin, value);
            Raise(nameof(CanClearTwitchLogin));
            Raise(nameof(CanTwitchLogin));
            Raise(nameof(TwitchLoginButtonText));
        }
    }
    public bool CanClearTwitchLogin => !IsClearingTwitchLogin;
    private bool _isTwitchLoginInProgress;
    public bool IsTwitchLoginInProgress
    {
        get => _isTwitchLoginInProgress;
        private set
        {
            if (_isTwitchLoginInProgress == value) return;
            Set(ref _isTwitchLoginInProgress, value);
            Raise(nameof(CanTwitchLogin));
            Raise(nameof(TwitchLoginButtonText));
        }
    }
    public bool CanTwitchLogin => !IsTwitchLoginInProgress && !IsClearingTwitchLogin && !_twitchLoggedIn;
    public string TwitchLoginButtonText => IsClearingTwitchLogin ? "正在清除…"
        : IsTwitchLoginInProgress ? "正在登录…"
        : !string.IsNullOrWhiteSpace(TwitchAuthorizationUrl) ? "打开登录页面"
        : _twitchAuthState == "needs_login" ? "重新登录" : "登录 Twitch";
    public Visibility TwitchRetryVisibility => TwitchRetryNowVisibility == Visibility.Visible
        ? Visibility.Collapsed : _twitchConnectionStage is
        TwitchConnectionStage.NetworkFailed or TwitchConnectionStage.ProxyUnavailable or
        TwitchConnectionStage.SslCertificateError or TwitchConnectionStage.SslRuntimeError or
        TwitchConnectionStage.WorkerError or TwitchConnectionStage.RetryWaiting
        ? Visibility.Visible : Visibility.Collapsed;
    private readonly TimeSpan _twitchRetryDelay;
    private readonly TimeSpan _twitchSlowThreshold;
    private readonly TimeSpan _twitchFailureThreshold;
    private readonly Func<int, CancellationToken, Task>? _twitchRetryOverride;
    private CancellationTokenSource? _twitchStageMonitorCts;
    private CancellationTokenSource? _twitchRetryCts;
    private Task? _twitchRetryTask;
    private TaskCompletionSource<bool>? _twitchRetryWakeSignal;
    private DateTimeOffset? _twitchNextRetryAt;
    private string _twitchRetryStatusText = "";
    public string TwitchRetryStatusText { get => _twitchRetryStatusText; private set { Set(ref _twitchRetryStatusText, value); Raise(nameof(TwitchRetryStatusVisibility)); } }
    public Visibility TwitchRetryStatusVisibility => string.IsNullOrWhiteSpace(TwitchRetryStatusText) ? Visibility.Collapsed : Visibility.Visible;
    public Visibility TwitchRetryNowVisibility => _twitchNextRetryAt.HasValue && HasTwitchRetryIntent()
        ? Visibility.Visible : Visibility.Collapsed;
    private readonly object _twitchRetrySync = new();
    private long _twitchStageRevision;
    private int _twitchRetryAttempt;
    private int _twitchRetryLoopStarts;
    private bool _twitchConnectionIntent;
    private bool _twitchManualIntent;
    private bool _twitchStartIntent;
    private bool _twitchAutoStartEnabled;
    private bool _twitchUserStopped;
    private bool _twitchUserLoggedOut;
    private bool _twitchApplicationStopping;
    private bool _twitchRetryAttemptInProgress;
    internal bool TwitchRetryLoopActive => _twitchRetryTask is { IsCompleted: false };
    internal int TwitchRetryLoopStarts => _twitchRetryLoopStarts;
    private readonly TimeSpan _soopRetryDelay;
    private readonly Func<int, CancellationToken, Task>? _soopRetryOverride;
    private CancellationTokenSource? _soopRetryCts;
    private Task? _soopRetryTask;
    private TaskCompletionSource<bool>? _soopRetryWakeSignal;
    private DateTimeOffset? _soopNextRetryAt;
    private string _soopRetryStatusText = "";
    public string SoopRetryStatusText { get => _soopRetryStatusText; private set { Set(ref _soopRetryStatusText, value); Raise(nameof(SoopRetryStatusVisibility)); } }
    public Visibility SoopRetryStatusVisibility => string.IsNullOrWhiteSpace(SoopRetryStatusText) ? Visibility.Collapsed : Visibility.Visible;
    public Visibility SoopRetryNowVisibility => _soopNextRetryAt.HasValue && HasSoopRetryIntent()
        ? Visibility.Visible : Visibility.Collapsed;
    private readonly object _soopRetrySync = new();
    private int _soopRetryAttempt;
    private int _soopRetryLoopStarts;
    private string _soopRecoveryUid = "";
    private bool _soopConnectionIntent;
    private bool _soopManualIntent;
    private bool _soopAutoStartEnabled;
    private bool _soopUserStopped;
    private bool _soopUserLoggedOut;
    private bool _soopApplicationStopping;
    private bool _soopRetryBlocked;
    private bool _soopRefreshPending;
    private int _soopRecoveryConfirmationInProgress;
    internal bool SoopRetryLoopActive => _soopRetryTask is { IsCompleted: false };
    internal int SoopRetryLoopStarts => _soopRetryLoopStarts;
    private bool _soopHasRefreshed;
    private bool _soopSettingsReady;
    private bool _twitchSettingsReady;
    private string _youtubeCurrentLabel = "YouTube";
    private string _recentNetworkError = "";
    private DateTimeOffset? _recentNetworkErrorAt;

    public DropsQuickStartGuide SoopQuickStart { get; } = new(
        new("①", "添加 SOOP 账号", "首次使用请先添加 SOOP 账号。账号信息仅保存在本机。", "soop_add_account"),
        new("②", "设置直播间与任务策略", "设置程序如何寻找直播间和处理掉宝任务。不确定时保持默认即可。", "soop_settings"),
        new("③", "刷新掉宝信息", "读取直播间、任务与奖励背包，完成后再启动账号。", "soop_refresh"),
        new("④", "开始运行", "添加账号和刷新数据后，还需要明确启动账号。", "soop_start"));
    public DropsQuickStartGuide YouTubeQuickStart { get; } = new(
        new("①", "选择浏览器", "选择 Chrome、Brave，或指定自定义浏览器程序位置。", "youtube_browser"),
        new("②", "添加观看账号", "首次使用请在打开的浏览器中完成 YouTube 登录。", "youtube_account"),
        new("③", "添加并启用频道", "程序只会自动检查已启用的频道。", "youtube_channel"),
        new("④", "开始观看", "启动后会定期检查频道，发现直播后自动开始观看。", "youtube_start"));
    public DropsQuickStartGuide TwitchQuickStart { get; } = new(
        new("①", "Twitch 登录", "首次使用需要通过 Twitch 登录流程完成授权。", "twitch_login"),
        new("②", "设置掉宝偏好", "不确定如何设置时保持默认即可；优先游戏会影响活动和频道顺序。", "twitch_settings"),
        new("③", "启动 Twitch 掉宝", "登录成功不代表当前已经启动掉宝。", "twitch_start"));

    private string _networkProxyStatus = "网络与代理：正在读取设置";
    public string NetworkProxyStatus { get => _networkProxyStatus; private set => Set(ref _networkProxyStatus, value); }

    private bool _isSoopRefreshing;
    public bool IsSoopRefreshing
    {
        get => _isSoopRefreshing;
        private set
        {
            if (_isSoopRefreshing == value) return;
            Set(ref _isSoopRefreshing, value);
            Raise(nameof(CanRefreshSoop));
            Raise(nameof(SoopRefreshButtonText));
            Raise(nameof(SoopRefreshProgressVisibility));
        }
    }
    public bool CanRefreshSoop => !IsSoopRefreshing;
    public string SoopRefreshButtonText => IsSoopRefreshing ? "正在刷新掉宝信息…" : "刷新掉宝信息";
    public Visibility SoopRefreshProgressVisibility => IsSoopRefreshing ? Visibility.Visible : Visibility.Collapsed;
    private string _soopRefreshStatus = "尚未刷新掉宝信息";
    public string SoopRefreshStatus { get => _soopRefreshStatus; private set => Set(ref _soopRefreshStatus, value); }

    public string TwitchCampaignScopeKey => _twitchCampaignScope switch
    {
        TwitchCampaignScope.Priority => "priority",
        TwitchCampaignScope.All => "all",
        _ => "available",
    };

    private bool _isTwitchRefreshing;
    public bool IsTwitchRefreshing
    {
        get => _isTwitchRefreshing;
        private set
        {
            if (_isTwitchRefreshing == value) return;
            _isTwitchRefreshing = value;
            Raise();
            Raise(nameof(CanRefreshTwitch));
            Raise(nameof(TwitchRefreshButtonText));
            Raise(nameof(TwitchRefreshProgressVisibility));
        }
    }
    public bool CanRefreshTwitch => !IsTwitchRefreshing;
    public string TwitchRefreshButtonText => IsTwitchRefreshing ? "刷新中…" : "刷新进度";
    public Visibility TwitchRefreshProgressVisibility => IsTwitchRefreshing ? Visibility.Visible : Visibility.Collapsed;
    private string _twitchRefreshStatus = "";
    public string TwitchRefreshStatus
    {
        get => _twitchRefreshStatus;
        private set
        {
            Set(ref _twitchRefreshStatus, value);
            Raise(nameof(TwitchRefreshStatusVisibility));
        }
    }
    public Visibility TwitchRefreshStatusVisibility => string.IsNullOrWhiteSpace(TwitchRefreshStatus)
        ? Visibility.Collapsed : Visibility.Visible;

    private string _twitchCampaignsEmptyText = "请先登录 Twitch 账号。";
    public string TwitchCampaignsEmptyText { get => _twitchCampaignsEmptyText; private set => Set(ref _twitchCampaignsEmptyText, value); }
    private string _twitchInventoryEmptyText = "请先登录 Twitch 账号。";
    public string TwitchInventoryEmptyText { get => _twitchInventoryEmptyText; private set => Set(ref _twitchInventoryEmptyText, value); }
    private string _twitchChannelsEmptyText = "请先登录 Twitch 账号。";
    public string TwitchChannelsEmptyText { get => _twitchChannelsEmptyText; private set => Set(ref _twitchChannelsEmptyText, value); }

    public DropsViewModel(DropsHostService host)
        : this(host, TimeSpan.FromMinutes(1), TimeSpan.FromSeconds(15),
            TimeSpan.FromSeconds(45), null, TimeSpan.FromMinutes(1), null)
    {
    }

    internal DropsViewModel(DropsHostService host, TimeSpan retryDelay,
        TimeSpan slowThreshold, TimeSpan failureThreshold,
        Func<int, CancellationToken, Task>? retryOverride)
        : this(host, retryDelay, slowThreshold, failureThreshold, retryOverride,
            TimeSpan.FromMinutes(1), null)
    {
    }

    internal DropsViewModel(DropsHostService host, TimeSpan twitchRetryDelay,
        TimeSpan slowThreshold, TimeSpan failureThreshold,
        Func<int, CancellationToken, Task>? twitchRetryOverride,
        TimeSpan soopRetryDelay, Func<int, CancellationToken, Task>? soopRetryOverride)
    {
        _host = host;
        _twitchRetryDelay = twitchRetryDelay;
        _twitchSlowThreshold = slowThreshold;
        _twitchFailureThreshold = failureThreshold;
        _twitchRetryOverride = twitchRetryOverride;
        _soopRetryDelay = soopRetryDelay;
        _soopRetryOverride = soopRetryOverride;
        Platforms = [Soop, YouTube, Twitch];
        Twitch.Status = "Twitch 尚未连接";
        Twitch.Summary = "尚未登录 Twitch";
        _host.SnapshotChanged += OnSnapshotChanged;
        _host.EventReceived += OnEventReceived;
    }

    public void UpdateProxySettings(bool enabled, string proxyUrl, bool fallbackDirect)
    {
        if (!enabled)
        {
            NetworkProxyStatus = "网络与代理：当前直连";
            return;
        }
        var endpoint = string.IsNullOrWhiteSpace(proxyUrl) ? "尚未填写代理地址" : proxyUrl.Trim();
        NetworkProxyStatus = $"网络与代理：已启用 · {endpoint}" +
                             (fallbackDirect ? " · 代理失败时允许直连" : "");
    }

    public void RefreshTemporalStatus(DateTimeOffset now)
    {
        if (_soopNextRetryAt is { } soopDeadline)
        {
            var seconds = Math.Max(0, (int)Math.Ceiling((soopDeadline - now).TotalSeconds));
            SoopRetryStatusText = _soopRetryAttempt == 0
                ? $"网络异常，{seconds} 秒后自动重试"
                : $"网络仍不可用，{seconds} 秒后继续尝试";
        }
        if (_twitchNextRetryAt is { } twitchDeadline)
        {
            var seconds = Math.Max(0, (int)Math.Ceiling((twitchDeadline - now).TotalSeconds));
            TwitchRetryStatusText = _twitchRetryAttempt == 0
                ? $"网络异常，{seconds} 秒后自动重试"
                : $"网络仍不可用，{seconds} 秒后继续尝试";
        }
        SoopLastSuccessText = _lastSoopSuccessfulAt.HasValue ? LastSuccessText(_lastSoopSuccessfulAt, now) : "";
        TwitchLastSuccessText = _lastTwitchConnectedAt.HasValue ? LastSuccessText(_lastTwitchConnectedAt, now) : "";
        YouTubeLastSuccessText = _lastYouTubeSuccessfulAt.HasValue ? LastSuccessText(_lastYouTubeSuccessfulAt, now) : "";
    }

    public bool RetrySoopNow()
    {
        TaskCompletionSource<bool>? signal;
        lock (_soopRetrySync)
        {
            if (!_soopNextRetryAt.HasValue || !HasSoopRetryIntent()) return false;
            signal = _soopRetryWakeSignal;
        }
        SoopRetryStatusText = "正在重新连接……";
        return signal?.TrySetResult(true) == true;
    }

    public bool RetryTwitchNow()
    {
        TaskCompletionSource<bool>? signal;
        lock (_twitchRetrySync)
        {
            if (!_twitchNextRetryAt.HasValue || !HasTwitchRetryIntent()) return false;
            signal = _twitchRetryWakeSignal;
        }
        TwitchRetryStatusText = "正在重新连接……";
        return signal?.TrySetResult(true) == true;
    }

    public DropsRuntimeDiagnosticSnapshot CreateDiagnosticSnapshot()
    {
        var now = DateTimeOffset.Now;
        var recentError = string.IsNullOrWhiteSpace(_recentNetworkError) ? "无" :
            $"{_recentNetworkError}（{LastSuccessText(_recentNetworkErrorAt, now).Replace("最后成功：", "", StringComparison.Ordinal)}）";
        return new DropsRuntimeDiagnosticSnapshot(
            Soop.Status, Twitch.Status, YouTube.Status,
            LastSuccessText(_lastSoopSuccessfulAt, now).Replace("最后成功：", "", StringComparison.Ordinal),
            LastSuccessText(_lastTwitchConnectedAt, now).Replace("最后成功：", "", StringComparison.Ordinal),
            LastSuccessText(_lastYouTubeSuccessfulAt, now).Replace("最后成功：", "", StringComparison.Ordinal),
            recentError);
    }

    private static string LastSuccessText(DateTimeOffset? timestamp, DateTimeOffset now)
    {
        if (timestamp is null) return "无";
        var elapsed = now - timestamp.Value;
        var relative = elapsed.TotalSeconds < 60 ? "刚刚" : elapsed.TotalMinutes < 60
            ? $"{Math.Max(1, (int)elapsed.TotalMinutes)} 分钟前"
            : elapsed.TotalHours < 24 ? $"{Math.Max(1, (int)elapsed.TotalHours)} 小时前"
            : timestamp.Value.ToLocalTime().ToString("yyyy-MM-dd HH:mm");
        return $"最后成功：{relative}";
    }

    private void RecordRecentNetworkError(string message)
    {
        _recentNetworkError = SensitiveDataRedactor.Redact(message);
        _recentNetworkErrorAt = DateTimeOffset.Now;
    }

    public void SetSoopAutoStartEnabled(bool enabled)
    {
        _soopAutoStartEnabled = enabled;
        if (!enabled && !_soopManualIntent)
        {
            _soopConnectionIntent = false;
            _soopRefreshPending = false;
            CancelSoopRetry();
        }
    }

    public void BeginSoopStart(string uid, bool automatic)
    {
        CancelSoopRetry();
        _soopConnectionIntent = true;
        _soopManualIntent |= !automatic;
        _soopAutoStartEnabled |= automatic;
        _soopUserStopped = false;
        _soopUserLoggedOut = false;
        _soopRetryBlocked = false;
        _soopRefreshPending = true;
        _soopRecoveryUid = uid.Trim();
        _soopRetryAttempt = 0;
    }

    public void StopSoopByUser()
    {
        _soopUserStopped = true;
        _soopConnectionIntent = false;
        _soopManualIntent = false;
        _soopRefreshPending = false;
        CancelSoopRetry();
    }

    public void LogoutSoopByUser(string uid)
    {
        if (!string.IsNullOrWhiteSpace(uid) &&
            !string.Equals(_soopRecoveryUid, uid, StringComparison.Ordinal)) return;
        _soopUserLoggedOut = true;
        _soopConnectionIntent = false;
        _soopManualIntent = false;
        _soopRefreshPending = false;
        _soopRecoveryUid = "";
        CancelSoopRetry();
    }

    public void SetSoopFailure(string message, bool refreshPending = true)
    {
        var kind = ClassifySoopFailure(message);
        switch (kind)
        {
            case SoopFailureKind.Authentication:
                _soopRetryBlocked = true;
                _soopRefreshPending = false;
                Soop.Running = false;
                Soop.Status = "SOOP 登录已失效";
                Soop.Summary = "请重新添加或登录 SOOP 账号。";
                CancelSoopRetry();
                break;
            case SoopFailureKind.RuntimeDependency:
                _soopRetryBlocked = true;
                _soopRefreshPending = false;
                Soop.Running = false;
                Soop.Status = "后台组件异常";
                Soop.Summary = FriendlySoopFailure(kind, message);
                CancelSoopRetry();
                break;
            case SoopFailureKind.Configuration:
            case SoopFailureKind.Unknown:
                _soopRetryBlocked = true;
                _soopRefreshPending = false;
                Soop.Running = false;
                Soop.Status = "SOOP 启动失败";
                Soop.Summary = FriendlySoopFailure(kind, message);
                CancelSoopRetry();
                break;
            default:
                _soopRetryBlocked = false;
                _soopRefreshPending |= refreshPending;
                RecordRecentNetworkError(FriendlySoopFailure(kind, message));
                Soop.Running = false;
                if (!HasSoopRetryIntent())
                {
                    Soop.Status = "SOOP 网络连接异常";
                    Soop.Summary = FriendlySoopFailure(kind, message);
                    break;
                }
                var alreadyWaiting = SoopRetryLoopActive;
                Soop.Status = "SOOP 网络连接异常，正在等待恢复…";
                Soop.Summary = "将在 1 分钟后自动重试";
                SoopRefreshStatus = "网络异常，等待自动重试";
                if (!alreadyWaiting)
                    _host.PublishUserLog(DropsPlatform.Soop, "warning",
                        "SOOP 网络连接异常，正在自动重试。");
                ScheduleSoopRetry();
                break;
        }
        UpdateSoopQuickStart();
    }

    private static SoopFailureKind ClassifySoopFailure(string message)
    {
        var value = message.ToLowerInvariant();
        if (value.Contains("needs_login") || value.Contains("auth_required") ||
            value.Contains("authentication") || value.Contains("登录已失效") ||
            value.Contains("session 已失效") || value.Contains("未找到该账号的 session") ||
            value.Contains("401"))
            return SoopFailureKind.Authentication;
        if (value.Contains("ssl_runtime_unavailable") || value.Contains("_ssl") ||
            value.Contains("dll load failed") || value.Contains("modulenotfounderror") ||
            value.Contains("no module named") || value.Contains("功能组件加载失败") ||
            value.Contains("功能组件不可用") || value.Contains("未找到 soop 功能组件"))
            return SoopFailureKind.RuntimeDependency;
        if (value.Contains("配置格式") || value.Contains("顶层必须是对象") ||
            value.Contains("settings 必须是对象") || value.Contains("代理地址只支持") ||
            value.Contains("代理端口无效") || value.Contains("代理地址不能包含"))
            return SoopFailureKind.Configuration;
        if (value.Contains("timeout") || value.Contains("timed out") || value.Contains("超时") ||
            value.Contains("connection refused") || value.Contains("connection reset") ||
            value.Contains("connection aborted") || value.Contains("connection closed") ||
            value.Contains("连接被拒绝") || value.Contains("连接被重置") ||
            value.Contains("temporary failure") || value.Contains("temporarily unavailable") ||
            value.Contains("name or service not known") || value.Contains("getaddrinfo") ||
            value.Contains("dns") || value.Contains("域名解析") || value.Contains("代理连接") ||
            value.Contains("proxy") || value.Contains("websocket") || value.Contains("网络") ||
            value.Contains("无法连接") || value.Contains("http 5"))
            return SoopFailureKind.TransientNetwork;
        return SoopFailureKind.Unknown;
    }

    private static string FriendlySoopFailure(SoopFailureKind kind, string fallback) => kind switch
    {
        SoopFailureKind.Authentication => "SOOP 登录信息已失效，请重新登录。",
        SoopFailureKind.RuntimeDependency => fallback.Contains("SSL", StringComparison.OrdinalIgnoreCase)
            ? "Python SSL 组件无法加载，SOOP 后台无法建立 HTTPS 连接。请重新安装 CloudLight Blizzard。"
            : string.IsNullOrWhiteSpace(fallback) ? "SOOP 本地运行组件不可用。" : fallback,
        SoopFailureKind.Configuration => string.IsNullOrWhiteSpace(fallback)
            ? "SOOP 配置无效，请检查当前设置。" : fallback,
        SoopFailureKind.TransientNetwork => "SOOP 暂时无法连接网络。",
        _ => string.IsNullOrWhiteSpace(fallback) ? "SOOP 后台请求失败。" : fallback,
    };

    private bool HasSoopRetryIntent() =>
        (_soopConnectionIntent || _soopAutoStartEnabled) &&
        !_soopUserStopped && !_soopUserLoggedOut && !_soopApplicationStopping &&
        !_soopRetryBlocked && !string.IsNullOrWhiteSpace(_soopRecoveryUid);

    private void ScheduleSoopRetry()
    {
        if (!HasSoopRetryIntent()) return;
        lock (_soopRetrySync)
        {
            if (_soopRetryTask is { IsCompleted: false } &&
                _soopRetryCts is { IsCancellationRequested: false }) return;
            _soopRetryCts = new CancellationTokenSource();
            _soopRetryLoopStarts++;
            _soopRetryTask = RunSoopRetryLoopAsync(_soopRetryCts);
        }
    }

    private async Task WaitForSoopRetryAsync(CancellationTokenSource owner, CancellationToken token)
    {
        var signal = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        lock (_soopRetrySync)
        {
            if (!ReferenceEquals(_soopRetryCts, owner)) throw new OperationCanceledException(token);
            _soopRetryWakeSignal = signal;
        }
        Dispatch(() => SetSoopRetryDeadline(DateTimeOffset.Now + _soopRetryDelay));
        try
        {
            var delay = Task.Delay(_soopRetryDelay, token);
            await Task.WhenAny(delay, signal.Task).ConfigureAwait(false);
            token.ThrowIfCancellationRequested();
        }
        finally
        {
            lock (_soopRetrySync)
                if (ReferenceEquals(_soopRetryWakeSignal, signal)) _soopRetryWakeSignal = null;
            Dispatch(() => SetSoopRetryDeadline(null,
                token.IsCancellationRequested ? "" : "正在重新连接……"));
        }
    }

    private void SetSoopRetryDeadline(DateTimeOffset? deadline, string status = "")
    {
        _soopNextRetryAt = deadline;
        SoopRetryStatusText = status;
        Raise(nameof(SoopRetryNowVisibility));
        if (deadline.HasValue) RefreshTemporalStatus(DateTimeOffset.Now);
    }

    private async Task RunSoopRetryLoopAsync(CancellationTokenSource owner)
    {
        var token = owner.Token;
        try
        {
            while (HasSoopRetryIntent())
            {
                await WaitForSoopRetryAsync(owner, token).ConfigureAwait(false);
                if (!HasSoopRetryIntent()) break;
                var attempt = Interlocked.Increment(ref _soopRetryAttempt);
                try
                {
                    if (_soopRetryOverride is not null)
                    {
                        await _soopRetryOverride(attempt, token).ConfigureAwait(false);
                    }
                    else
                    {
                        try
                        {
                            await _host.RequestAsync(DropsPlatform.Soop, "stop_account",
                                new { userid = _soopRecoveryUid }, token).ConfigureAwait(false);
                        }
                        catch (OperationCanceledException) when (token.IsCancellationRequested) { throw; }
                        catch { }
                        await _host.RequestAsync(DropsPlatform.Soop, "start_account",
                            new { userid = _soopRecoveryUid, retryAttempt = attempt }, token).ConfigureAwait(false);
                        var state = await _host.RequestAsync(DropsPlatform.Soop, "refresh",
                            cancellationToken: token).ConfigureAwait(false);
                        if (!Bool(state, "refreshCompleted"))
                            throw new InvalidOperationException("SOOP 刷新未返回完成状态。");
                    }
                    Dispatch(CompleteSoopNetworkRecovery);
                    break;
                }
                catch (OperationCanceledException) when (token.IsCancellationRequested) { break; }
                catch (Exception ex)
                {
                    var kind = ClassifySoopFailure(ex.Message);
                    if (kind != SoopFailureKind.TransientNetwork)
                    {
                        Dispatch(() => SetSoopFailure(ex.Message));
                        break;
                    }
                    Dispatch(() =>
                    {
                        Soop.Status = "SOOP 网络连接异常，正在等待恢复…";
                        Soop.Summary = "仍无法连接，后台会继续重试";
                        SoopRefreshStatus = "网络异常，等待自动重试";
                        UpdateSoopQuickStart();
                    });
                }
            }
        }
        catch (OperationCanceledException) { }
        finally
        {
            lock (_soopRetrySync)
            {
                if (ReferenceEquals(_soopRetryCts, owner))
                {
                    _soopRetryCts = null;
                    _soopRetryTask = null;
                }
            }
            owner.Dispose();
        }
    }

    private void CancelSoopRetry()
    {
        lock (_soopRetrySync) _soopRetryCts?.Cancel();
        Dispatch(() => SetSoopRetryDeadline(null));
    }

    private async Task ConfirmSoopNetworkRecoveryAsync()
    {
        if (Interlocked.Exchange(ref _soopRecoveryConfirmationInProgress, 1) != 0) return;
        try
        {
            var state = await _host.RequestAsync(DropsPlatform.Soop, "refresh").ConfigureAwait(false);
            if (!Bool(state, "refreshCompleted"))
                throw new InvalidOperationException("SOOP 刷新未返回完成状态。");
            Dispatch(CompleteSoopNetworkRecovery);
        }
        catch (Exception ex)
        {
            if (!_soopApplicationStopping)
                Dispatch(() => SetSoopFailure(ex.Message));
        }
        finally
        {
            Interlocked.Exchange(ref _soopRecoveryConfirmationInProgress, 0);
        }
    }

    private void CompleteSoopNetworkRecovery()
    {
        var hadRetries = _soopRetryAttempt > 0 || SoopRetryLoopActive;
        _soopRetryBlocked = false;
        _soopRefreshPending = false;
        Soop.Running = true;
        CompleteSoopRefresh();
        Soop.Status = "SOOP 网络连接已恢复";
        Soop.Summary = "账号和掉宝频道已重新加载";
        CancelSoopRetry();
        if (hadRetries)
            _host.PublishUserLog(DropsPlatform.Soop, "info", "SOOP 网络连接已恢复。");
    }

    public void BeginSoopRefresh()
    {
        IsSoopRefreshing = true;
        SoopRefreshStatus = "正在刷新掉宝信息…";
        UpdateSoopQuickStart();
    }

    public void CompleteSoopRefresh()
    {
        _soopHasRefreshed = true;
        _lastSoopSuccessfulAt = DateTimeOffset.Now;
        RefreshTemporalStatus(DateTimeOffset.Now);
        IsSoopRefreshing = false;
        var available = Tasks.Count(row => Bool(row.Payload, "active"));
        var channels = Accounts.SelectMany(row => row.Payload.TryGetProperty("channels", out var items) &&
                items.ValueKind == JsonValueKind.Array
                ? items.EnumerateArray().Select(item => Text(item, "id"))
                : Array.Empty<string>())
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .Distinct(StringComparer.Ordinal)
            .Count();
        SoopRefreshStatus = channels == 0
            ? "已刷新 · 当前没有符合条件的频道"
            : $"已刷新 · {channels} 个频道 · {available} 个可用任务";
        UpdateSoopQuickStart();
    }

    public void FailSoopRefresh()
    {
        IsSoopRefreshing = false;
        SoopRefreshStatus = "刷新失败，请检查网络或运行日志后重试。";
        UpdateSoopQuickStart();
    }

    public Task<JsonElement> LoadAsync(DropsPlatform platform, CancellationToken token = default) =>
        _host.RequestAsync(platform, "load_state", cancellationToken: token);

    public Task<JsonElement> RequestAsync(DropsPlatform platform, string command, object? payload = null,
        CancellationToken token = default) => _host.RequestAsync(platform, command, payload, token);

    public Task<JsonElement> StartAsync(DropsPlatform platform) => _host.StartAsync(platform);
    public Task<JsonElement> StopAsync(DropsPlatform platform) => _host.StopAsync(platform);
    public Task<JsonElement> ClearTwitchAuthenticationAsync(CancellationToken token = default) =>
        _host.ClearTwitchAuthenticationAsync(token);

    public void BeginTwitchRefresh()
    {
        TwitchRefreshStatus = "正在重新获取活动、奖励与频道…";
        IsTwitchRefreshing = true;
    }

    public void BeginTwitchLogin()
    {
        CancelTwitchRetry();
        _twitchConnectionIntent = true;
        _twitchManualIntent = true;
        _twitchStartIntent = false;
        _twitchUserStopped = false;
        _twitchUserLoggedOut = false;
        _twitchRetryAttempt = 0;
        _twitchRetryBlocked = false;
        SetTwitchAuthState("checking");
        IsTwitchLoginInProgress = true;
        SetTwitchStage(TwitchConnectionStage.Connecting,
            "正在连接 Twitch 服务器并请求登录验证码…", "正在启动 Twitch Device Code 登录流程", monitor: true);
        UpdateTwitchQuickStart(default, false);
    }

    public void BeginTwitchStart(bool automatic)
    {
        CancelTwitchRetry();
        _twitchConnectionIntent = true;
        _twitchStartIntent = true;
        _twitchManualIntent |= !automatic;
        _twitchAutoStartEnabled |= automatic;
        _twitchUserStopped = false;
        _twitchUserLoggedOut = false;
        SetTwitchStage(TwitchConnectionStage.Connecting,
            "正在连接 Twitch 服务器…", "正在启动 Twitch 掉宝服务", monitor: true);
    }

    public void SetTwitchAutoStartEnabled(bool enabled)
    {
        _twitchAutoStartEnabled = enabled;
        if (!enabled && !_twitchManualIntent)
        {
            _twitchConnectionIntent = false;
            _twitchStartIntent = false;
            CancelTwitchRetry();
        }
    }

    public void StopTwitchByUser()
    {
        _twitchUserStopped = true;
        _twitchConnectionIntent = false;
        _twitchManualIntent = false;
        _twitchStartIntent = false;
        CancelTwitchRetry();
        SetTwitchStage(TwitchConnectionStage.Stopped,
            "Twitch 已停止", _twitchLoggedIn ? "已登录，可重新开始 Twitch 掉宝" : "Twitch 尚未连接");
    }

    public void LogoutTwitchByUser()
        => BeginClearTwitchLogin();

    public void BeginClearTwitchLogin()
    {
        _twitchUserLoggedOut = true;
        _twitchConnectionIntent = false;
        _twitchManualIntent = false;
        _twitchStartIntent = false;
        SetTwitchLoggedIn(false);
        CancelTwitchRetry();
        ClearTwitchFailure();
        IsTwitchLoginInProgress = false;
        IsClearingTwitchLogin = true;
        SetTwitchAuthState("logged_out");
        SetTwitchStage(TwitchConnectionStage.Unconnected,
            "正在清除 Twitch 登录信息…", "正在停止当前登录与连接流程");
        Raise(nameof(TwitchLoginVisibility));
        Raise(nameof(TwitchLogoutVisibility));
    }

    public void CompleteClearTwitchLogin(JsonElement state)
    {
        IsClearingTwitchLogin = false;
        _twitchUserLoggedOut = false;
        _twitchRetryBlocked = false;
        ApplyState(DropsPlatform.Twitch, state);
        if (state.TryGetProperty("runtime", out var runtime) && runtime.ValueKind == JsonValueKind.Object &&
            !Bool(runtime, "available", true)) return;
        SetTwitchAuthState("logged_out");
        SetTwitchStage(TwitchConnectionStage.Unconnected,
            "未登录 Twitch", "登录信息已清除，可以重新登录");
    }

    public void FailClearTwitchLogin(string message)
    {
        IsClearingTwitchLogin = false;
        SetTwitchFailure(TwitchConnectionFailureKind.Worker,
            string.IsNullOrWhiteSpace(message) ? "清除 Twitch 登录信息失败。" : message,
            retryable: false, scheduleRetry: false);
    }

    public void SetTwitchAuthorization(string url, string code, bool automatic)
    {
        if (automatic) SetTwitchLoggedIn(false);
        TwitchAuthorizationUrl = url;
        TwitchAuthorizationCode = code;
        SetTwitchAuthState(automatic ? "needs_login" : "authorization_required", clearAuthorization: false);
        Twitch.Running = false;
        IsTwitchLoginInProgress = false;
        CancelTwitchRetry();
        SetTwitchStage(automatic ? TwitchConnectionStage.AuthenticationExpired : TwitchConnectionStage.WaitingAuthorization,
            automatic ? "Twitch 登录已失效" : "等待完成 Twitch 授权",
            automatic ? "请重新完成 Twitch 授权" : "请在浏览器中输入上方验证码完成登录");
        UpdateTwitchQuickStart(default, false);
    }

    public void SetTwitchFailure(string message)
    {
        var kind = ClassifyTwitchFailure("", message);
        SetTwitchFailure(kind, FriendlyTwitchFailure(kind, message),
            retryable: IsTwitchRetryableFailure(kind, message),
            scheduleRetry: true);
    }

    internal void SetTwitchTemporaryNetworkFailure(string message)
    {
        var kind = ClassifyTwitchFailure("", message);
        SetTwitchTemporaryFailure(kind, FriendlyTwitchFailure(kind, message));
    }

    private void SetTwitchTemporaryFailure(TwitchConnectionFailureKind kind, string message)
    {
        if (!IsTwitchRetryableFailure(kind, message))
        {
            SetTwitchFailure(kind, message, retryable: false, scheduleRetry: false);
            return;
        }
        if (!HasTwitchRetryIntent())
        {
            RecordTwitchFailure(kind, message, retryable: true);
            IsTwitchLoginInProgress = false;
            SetTwitchStage(StageForFailure(kind), StatusForFailure(kind), message);
            return;
        }
        var alreadyWaiting = _twitchConnectionStage == TwitchConnectionStage.RetryWaiting &&
                             TwitchRetryLoopActive;
        RecordTwitchFailure(kind, message, retryable: true);
        IsTwitchLoginInProgress = false;
        SetTwitchStage(TwitchConnectionStage.RetryWaiting,
            "暂时无法连接 Twitch", $"{message} 将于 1 分钟后自动重试。");
        if (!alreadyWaiting)
            _host.PublishUserLog(DropsPlatform.Twitch, "warning",
                "Twitch 连接超时，将在 60 秒后自动重试。");
        ScheduleTwitchRetry(showWaiting: false);
    }

    private void SetTwitchFailure(TwitchConnectionFailureKind kind, string message, bool retryable,
        bool scheduleRetry)
    {
        RecordTwitchFailure(kind, message, retryable);
        if (kind == TwitchConnectionFailureKind.Authentication) SetTwitchLoggedIn(false);
        SetTwitchAuthState(kind == TwitchConnectionFailureKind.Authentication ? "needs_login" : "failed");
        Twitch.Running = false;
        IsTwitchLoginInProgress = false;
        SetTwitchStage(StageForFailure(kind), StatusForFailure(kind),
            retryable ? $"{message} 将于 1 分钟后自动重试。" : kind == TwitchConnectionFailureKind.SslRuntime
                ? $"{message} 自动重试已停止。"
                : message);
        if (retryable && scheduleRetry)
            ScheduleTwitchRetry(showWaiting: false);
        else if (!retryable)
            CancelTwitchRetry();
        UpdateTwitchQuickStart(default, false);
    }

    private void RecordTwitchFailure(TwitchConnectionFailureKind kind, string message, bool retryable)
    {
        if (_twitchLastFailureKind != TwitchConnectionFailureKind.None &&
            FailureSpecificity(_twitchLastFailureKind) > FailureSpecificity(kind)) return;
        _twitchLastFailureKind = kind;
        _twitchLastError = message;
        _twitchRetryBlocked = !retryable;
        if (retryable) RecordRecentNetworkError(message);
        Raise(nameof(TwitchLastConnectionFailureKind));
        Raise(nameof(TwitchLastError));
    }

    private void ClearTwitchFailure()
    {
        _twitchLastFailureKind = TwitchConnectionFailureKind.None;
        _twitchLastError = "";
        _twitchRetryBlocked = false;
        Raise(nameof(TwitchLastConnectionFailureKind));
        Raise(nameof(TwitchLastError));
    }

    private void SetTwitchLoggedIn(bool value)
    {
        if (_twitchLoggedIn == value) return;
        _twitchLoggedIn = value;
        Raise(nameof(IsTwitchLoggedIn));
        Raise(nameof(TwitchLoginVisibility));
        Raise(nameof(TwitchLogoutVisibility));
        Raise(nameof(CanTwitchLogin));
    }

    private static int FailureSpecificity(TwitchConnectionFailureKind kind) => kind switch
    {
        TwitchConnectionFailureKind.SslRuntime => 100,
        TwitchConnectionFailureKind.SslCertificate => 90,
        TwitchConnectionFailureKind.Authentication => 90,
        TwitchConnectionFailureKind.ProxyAndDirect => 80,
        TwitchConnectionFailureKind.Dns => 70,
        TwitchConnectionFailureKind.Timeout => 70,
        TwitchConnectionFailureKind.Proxy => 60,
        TwitchConnectionFailureKind.Worker => 50,
        TwitchConnectionFailureKind.Network => 10,
        _ => 0,
    };

    private static TwitchConnectionFailureKind ClassifyTwitchFailure(string code, string message)
    {
        var value = $"{code} {message}".ToLowerInvariant();
        if (value.Contains("ssl_runtime_unavailable") || value.Contains("_ssl") ||
            value.Contains("ssl is not supported") || value.Contains("python ssl"))
            return TwitchConnectionFailureKind.SslRuntime;
        if (value.Contains("certificate_verify_failed") || value.Contains("证书"))
            return TwitchConnectionFailureKind.SslCertificate;
        if (value.Contains("authentication") || value.Contains("401") || value.Contains("登录信息已失效"))
            return TwitchConnectionFailureKind.Authentication;
        if (value.Contains("proxy_and_direct") || value.Contains("代理和直连") || value.Contains("代理失败，直连"))
            return TwitchConnectionFailureKind.ProxyAndDirect;
        if (value.Contains("dns") || value.Contains("域名解析") || value.Contains("getaddrinfo"))
            return TwitchConnectionFailureKind.Dns;
        if (value.Contains("timeout") || value.Contains("超时") || value.Contains("长时间没有进展"))
            return TwitchConnectionFailureKind.Timeout;
        if (value.Contains("proxy") || value.Contains("代理"))
            return TwitchConnectionFailureKind.Proxy;
        if (value.Contains("worker") || value.Contains("后台服务") || value.Contains("后台组件") ||
            value.Contains("modulenotfounderror") || value.Contains("no module named") ||
            value.Contains("业务核心加载失败") || value.Contains("业务核心不可用") ||
            value.Contains("未找到 twitchdropsminer") || value.Contains("配置格式") ||
            value.Contains("顶层必须是对象") || value.Contains("settings 必须是对象") ||
            value.Contains("代理地址只支持") || value.Contains("代理端口无效"))
            return TwitchConnectionFailureKind.Worker;
        return TwitchConnectionFailureKind.Network;
    }

    private static bool IsTwitchRetryableFailure(TwitchConnectionFailureKind kind, string message)
    {
        if (kind is TwitchConnectionFailureKind.SslRuntime or TwitchConnectionFailureKind.SslCertificate or
            TwitchConnectionFailureKind.Authentication) return false;
        if (kind != TwitchConnectionFailureKind.Worker) return true;
        var value = message.ToLowerInvariant();
        return !(value.Contains("modulenotfounderror") || value.Contains("no module named") ||
                 value.Contains("业务核心加载失败") || value.Contains("业务核心不可用") ||
                 value.Contains("未找到 twitchdropsminer") || value.Contains("配置格式") ||
                 value.Contains("顶层必须是对象") || value.Contains("settings 必须是对象") ||
                 value.Contains("代理地址只支持") || value.Contains("代理端口无效") ||
                 value.Contains("本地运行"));
    }

    private static string FriendlyTwitchFailure(TwitchConnectionFailureKind kind, string fallback) => kind switch
    {
        TwitchConnectionFailureKind.Timeout => "Twitch 连接失败：连接超时。",
        TwitchConnectionFailureKind.Dns => "Twitch 连接失败：域名解析失败。",
        TwitchConnectionFailureKind.Proxy => "Twitch 代理连接失败。",
        TwitchConnectionFailureKind.ProxyAndDirect => "Twitch 无法连接：代理失败，直连也不可用。",
        TwitchConnectionFailureKind.SslCertificate => "Twitch HTTPS 证书验证失败。",
        TwitchConnectionFailureKind.SslRuntime =>
            "Twitch 后台无法启动 HTTPS：Python SSL 运行库无法加载。请重新安装 CloudLight Blizzard。",
        TwitchConnectionFailureKind.Authentication => "Twitch 登录信息已失效，请重新登录。",
        TwitchConnectionFailureKind.Worker => string.IsNullOrWhiteSpace(fallback)
            ? "Twitch 后台组件异常。" : fallback,
        _ => string.IsNullOrWhiteSpace(fallback) ? "Twitch 连接失败：无法连接服务器。" : fallback,
    };

    private static TwitchConnectionStage StageForFailure(TwitchConnectionFailureKind kind) => kind switch
    {
        TwitchConnectionFailureKind.Proxy or TwitchConnectionFailureKind.ProxyAndDirect =>
            TwitchConnectionStage.ProxyUnavailable,
        TwitchConnectionFailureKind.SslCertificate => TwitchConnectionStage.SslCertificateError,
        TwitchConnectionFailureKind.SslRuntime => TwitchConnectionStage.SslRuntimeError,
        TwitchConnectionFailureKind.Worker => TwitchConnectionStage.WorkerError,
        TwitchConnectionFailureKind.Authentication => TwitchConnectionStage.AuthenticationExpired,
        _ => TwitchConnectionStage.NetworkFailed,
    };

    private static string StatusForFailure(TwitchConnectionFailureKind kind) => kind switch
    {
        TwitchConnectionFailureKind.SslRuntime => "Twitch 后台组件异常",
        TwitchConnectionFailureKind.SslCertificate => "Twitch HTTPS 连接失败",
        TwitchConnectionFailureKind.Authentication => "Twitch 登录已失效",
        TwitchConnectionFailureKind.Proxy or TwitchConnectionFailureKind.ProxyAndDirect => "Twitch 代理连接失败",
        TwitchConnectionFailureKind.Worker => "Twitch 后台组件异常",
        _ => "Twitch 连接失败",
    };

    private void ApplyRuntimeError(DropsPlatform platform, string code, string message)
    {
        var vm = For(platform);
        vm.Running = false;
        if (platform == DropsPlatform.Twitch)
        {
            var kind = ClassifyTwitchFailure(code, message);
            SetTwitchFailure(kind, FriendlyTwitchFailure(kind, message), retryable: false, scheduleRetry: false);
            return;
        }
        if (platform == DropsPlatform.Soop)
        {
            _soopRetryBlocked = true;
            _soopRefreshPending = false;
            CancelSoopRetry();
        }
        vm.Status = "后台组件异常";
        vm.Summary = platform == DropsPlatform.YouTube
            ? "Python SSL 组件无法加载，无法访问 YouTube。请重新安装 CloudLight Blizzard。"
            : "Python SSL 组件无法加载，SOOP 后台无法建立 HTTPS 连接。请重新安装 CloudLight Blizzard。";
    }

    private void SetTwitchAuthState(string state, bool clearAuthorization = true)
    {
        _twitchAuthState = state;
        if (clearAuthorization && state != "authorization_required")
        {
            TwitchAuthorizationUrl = "";
            TwitchAuthorizationCode = "";
        }
        Raise(nameof(TwitchAuthState));
        Raise(nameof(TwitchAuthorizationVisibility));
        Raise(nameof(CanTwitchLogin));
        Raise(nameof(TwitchLoginButtonText));
    }

    private void SetTwitchStage(TwitchConnectionStage stage, string status, string summary,
        bool monitor = false)
    {
        _twitchStageMonitorCts?.Cancel();
        _twitchStageMonitorCts?.Dispose();
        _twitchStageMonitorCts = null;
        _twitchConnectionStage = stage;
        _twitchStageStartedAt = DateTimeOffset.Now;
        var revision = ++_twitchStageRevision;
        if (!string.IsNullOrWhiteSpace(_twitchLastError) && stage is
            TwitchConnectionStage.WorkerStarting or TwitchConnectionStage.Connecting or
            TwitchConnectionStage.CheckingSession or TwitchConnectionStage.RestoringSession or
            TwitchConnectionStage.RequestingAuthorization or TwitchConnectionStage.Slow or
            TwitchConnectionStage.Reconnecting)
            summary = $"{summary} · 最近失败原因：{_twitchLastError}";
        Twitch.Status = status;
        Twitch.Summary = summary;
        Raise(nameof(TwitchConnectionStage));
        Raise(nameof(TwitchStageStartedAt));
        Raise(nameof(TwitchRetryVisibility));

        if (stage is TwitchConnectionStage.Connected or TwitchConnectionStage.Running)
        {
            var wasRetrying = _twitchRetryAttempt > 0;
            _lastTwitchConnectedAt = DateTimeOffset.Now;
            RefreshTemporalStatus(DateTimeOffset.Now);
            ClearTwitchFailure();
            Raise(nameof(LastTwitchConnectedAt));
            CancelTwitchRetry();
            if (wasRetrying)
            {
                _host.PublishUserLog(DropsPlatform.Twitch, "info", "Twitch 已重新连接。");
                _twitchRetryAttempt = 0;
            }
        }
        else if (stage == TwitchConnectionStage.LoginSucceeded && !_twitchStartIntent)
        {
            CancelTwitchRetry();
        }
        else if (stage is TwitchConnectionStage.WaitingAuthorization or TwitchConnectionStage.AuthenticationExpired)
        {
            CancelTwitchRetry();
        }

        if (monitor && CanRetryTwitchConnection())
        {
            _twitchStageMonitorCts = new CancellationTokenSource();
            _ = MonitorTwitchStageAsync(revision, _twitchStageMonitorCts.Token);
        }
    }

    private async Task MonitorTwitchStageAsync(long revision, CancellationToken token)
    {
        try
        {
            await Task.Delay(_twitchSlowThreshold, token).ConfigureAwait(false);
            Dispatch(() =>
            {
                if (revision != _twitchStageRevision || !CanRetryTwitchConnection()) return;
                _twitchConnectionStage = TwitchConnectionStage.Slow;
                Twitch.Status = "Twitch 连接较慢，仍在尝试…";
                Twitch.Summary = string.IsNullOrWhiteSpace(_twitchLastError)
                    ? "网络响应较慢，连接仍在继续"
                    : $"仍在尝试 · 最近失败原因：{_twitchLastError}";
                Raise(nameof(TwitchConnectionStage));
                Raise(nameof(TwitchRetryVisibility));
            });
            var remaining = _twitchFailureThreshold - _twitchSlowThreshold;
            if (remaining > TimeSpan.Zero)
                await Task.Delay(remaining, token).ConfigureAwait(false);
            Dispatch(() =>
            {
                if (revision == _twitchStageRevision && CanRetryTwitchConnection())
                {
                    var kind = _twitchLastFailureKind == TwitchConnectionFailureKind.None
                        ? TwitchConnectionFailureKind.Timeout : _twitchLastFailureKind;
                    var message = string.IsNullOrWhiteSpace(_twitchLastError)
                        ? FriendlyTwitchFailure(kind, "") : _twitchLastError;
                    SetTwitchTemporaryFailure(kind, message);
                }
            });
        }
        catch (OperationCanceledException) { }
    }

    private bool CanRetryTwitchConnection()
    {
        return HasTwitchRetryIntent() &&
               _twitchConnectionStage is not TwitchConnectionStage.Connected and
               not TwitchConnectionStage.Running;
    }

    private bool HasTwitchRetryIntent()
    {
        if ((!_twitchConnectionIntent && !_twitchAutoStartEnabled) ||
            _twitchUserStopped || _twitchUserLoggedOut ||
            _twitchApplicationStopping || _twitchRetryBlocked ||
            _twitchAuthState is "authorization_required" or "needs_login")
            return false;
        return _twitchStartIntent || !_twitchLoggedIn;
    }

    private void ScheduleTwitchRetry(bool showWaiting)
    {
        if (!HasTwitchRetryIntent()) return;
        if (showWaiting)
            SetTwitchStage(TwitchConnectionStage.RetryWaiting,
                "暂时无法连接 Twitch", string.IsNullOrWhiteSpace(_twitchLastError)
                    ? "将于 1 分钟后自动重试，请检查网络或代理设置。"
                    : $"{_twitchLastError} 将于 1 分钟后自动重试。");
        if (!CanRetryTwitchConnection()) return;
        lock (_twitchRetrySync)
        {
            if (_twitchRetryTask is { IsCompleted: false } &&
                _twitchRetryCts is { IsCancellationRequested: false }) return;
            _twitchRetryCts = new CancellationTokenSource();
            _twitchRetryLoopStarts++;
            _twitchRetryTask = RunTwitchRetryLoopAsync(_twitchRetryCts);
        }
    }

    private async Task WaitForTwitchRetryAsync(CancellationTokenSource owner, CancellationToken token)
    {
        var signal = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        lock (_twitchRetrySync)
        {
            if (!ReferenceEquals(_twitchRetryCts, owner)) throw new OperationCanceledException(token);
            _twitchRetryWakeSignal = signal;
        }
        Dispatch(() => SetTwitchRetryDeadline(DateTimeOffset.Now + _twitchRetryDelay));
        try
        {
            var delay = Task.Delay(_twitchRetryDelay, token);
            await Task.WhenAny(delay, signal.Task).ConfigureAwait(false);
            token.ThrowIfCancellationRequested();
        }
        finally
        {
            lock (_twitchRetrySync)
                if (ReferenceEquals(_twitchRetryWakeSignal, signal)) _twitchRetryWakeSignal = null;
            Dispatch(() => SetTwitchRetryDeadline(null,
                token.IsCancellationRequested ? "" : "正在重新连接……"));
        }
    }

    private void SetTwitchRetryDeadline(DateTimeOffset? deadline, string status = "")
    {
        _twitchNextRetryAt = deadline;
        TwitchRetryStatusText = status;
        Raise(nameof(TwitchRetryNowVisibility));
        Raise(nameof(TwitchRetryVisibility));
        if (deadline.HasValue) RefreshTemporalStatus(DateTimeOffset.Now);
    }

    private async Task RunTwitchRetryLoopAsync(CancellationTokenSource owner)
    {
        var token = owner.Token;
        try
        {
            while (CanRetryTwitchConnection())
            {
                await WaitForTwitchRetryAsync(owner, token).ConfigureAwait(false);
                if (!CanRetryTwitchConnection()) break;
                var attempt = Interlocked.Increment(ref _twitchRetryAttempt);
                Dispatch(() => SetTwitchStage(TwitchConnectionStage.Connecting,
                    "正在连接 Twitch 服务器…", $"正在进行第 {attempt} 次重连", monitor: true));
                try
                {
                    if (_twitchRetryOverride is not null)
                    {
                        await _twitchRetryOverride(attempt, token).ConfigureAwait(false);
                    }
                    else
                    {
                        _twitchRetryAttemptInProgress = true;
                        var snapshot = _host.Snapshots.First(item => item.Platform == DropsPlatform.Twitch);
                        if (snapshot.Lifecycle is WorkerLifecycle.Starting or WorkerLifecycle.Running)
                        {
                            try { await _host.StopAsync(DropsPlatform.Twitch, token).ConfigureAwait(false); }
                            catch { }
                        }
                        var command = _twitchStartIntent ? "start" : "login";
                        await _host.RequestAsync(DropsPlatform.Twitch, command,
                            new { automatic = _twitchStartIntent, retryAttempt = attempt }, token).ConfigureAwait(false);
                    }
                }
                catch (OperationCanceledException) when (token.IsCancellationRequested) { break; }
                catch (Exception ex)
                {
                    Dispatch(() =>
                    {
                        if (!CanRetryTwitchConnection()) return;
                        var kind = ClassifyTwitchFailure("", ex.Message);
                        var message = FriendlyTwitchFailure(kind, ex.Message);
                        if (!IsTwitchRetryableFailure(kind, message))
                        {
                            SetTwitchFailure(kind, message, retryable: false, scheduleRetry: false);
                            return;
                        }
                        RecordTwitchFailure(kind, message, retryable: true);
                        SetTwitchStage(TwitchConnectionStage.RetryWaiting,
                            "暂时无法连接 Twitch", string.IsNullOrWhiteSpace(_twitchLastError)
                                ? "将在 1 分钟后继续自动重试"
                                : $"{_twitchLastError} 将在 1 分钟后继续自动重试");
                    });
                }
                finally { _twitchRetryAttemptInProgress = false; }
            }
        }
        catch (OperationCanceledException) { }
        finally
        {
            lock (_twitchRetrySync)
            {
                if (ReferenceEquals(_twitchRetryCts, owner))
                {
                    _twitchRetryCts = null;
                    _twitchRetryTask = null;
                }
            }
            owner.Dispose();
        }
    }

    private void CancelTwitchRetry()
    {
        _twitchStageMonitorCts?.Cancel();
        _twitchStageMonitorCts?.Dispose();
        _twitchStageMonitorCts = null;
        lock (_twitchRetrySync) _twitchRetryCts?.Cancel();
        Dispatch(() => SetTwitchRetryDeadline(null));
    }

    public void CompleteTwitchRefresh(DateTimeOffset completedAt)
    {
        _lastTwitchConnectedAt = completedAt;
        Raise(nameof(LastTwitchConnectedAt));
        RefreshTemporalStatus(DateTimeOffset.Now);
        TwitchRefreshStatus = $"最后刷新：{completedAt.ToLocalTime():HH:mm:ss}";
        IsTwitchRefreshing = false;
    }

    public void FailTwitchRefresh()
    {
        TwitchRefreshStatus = "刷新失败，请检查网络或运行日志后重试。";
        IsTwitchRefreshing = false;
    }

    public void SetTwitchCampaignScope(string scope)
    {
        _twitchCampaignScope = scope switch
        {
            "priority" => TwitchCampaignScope.Priority,
            "all" => TwitchCampaignScope.All,
            _ => TwitchCampaignScope.Available,
        };
        _twitchCampaignScopeInitialized = true;
        Raise(nameof(TwitchCampaignScopeKey));
        RebuildTwitchCampaigns();
    }

    public void ApplyState(DropsPlatform platform, JsonElement state)
    {
        var vm = For(platform);
        if (state.TryGetProperty("running", out var running)) vm.Running = running.GetBoolean();
        if (state.TryGetProperty("status", out var status)) vm.Status = status.GetString() ?? vm.Status;
        Accounts.Clear(); Tasks.Clear(); Inventory.Clear(); Channels.Clear(); History.Clear();
        SoopCurrentProgress.Clear();
        switch (platform)
        {
            case DropsPlatform.Soop: ApplySoop(state, vm); break;
            case DropsPlatform.YouTube: ApplyYouTube(state, vm); break;
            case DropsPlatform.Twitch: ApplyTwitch(state, vm); break;
        }
        if (state.TryGetProperty("runtime", out var runtime) && runtime.ValueKind == JsonValueKind.Object &&
            !Bool(runtime, "available", true))
            ApplyRuntimeError(platform, Text(runtime, "code"), Text(runtime, "message"));
    }

    private void ApplySoop(JsonElement state, DropsPlatformViewModel vm)
    {
        if (Bool(state, "refreshCompleted"))
        {
            _soopRefreshPending = false;
            _soopRetryBlocked = false;
            _soopHasRefreshed = true;
        }
        _soopSettingsReady = state.TryGetProperty("settings", out var soopSettings) &&
                             soopSettings.ValueKind == JsonValueKind.Object;
        AddRows(state, "accounts", Accounts, item => new DropsRow
        {
            Id = Text(item, "uid"), Primary = Text(item, "uid"),
            Secondary = $"登录：已保存 · 直播间：{Text(item, "channelName", "尚未进入")} · 任务：{Text(item, "missionTitle", "等待刷新")}",
            Status = (Bool(item, "primary") ? "主账号 · " : "") + (Bool(item, "running") ? "运行中" : "已停止"),
            Payload = item.Clone(),
        });
        AddRows(state, "tasks", Tasks, item => new DropsRow
        {
            Id = Text(item, "id"), Primary = Text(item, "title"),
            Secondary = $"{Text(item, "type")} · {Text(item, "categoryName")}",
            Status = Bool(item, "active") ? "进行中" : "未进行", Payload = item.Clone(),
        });
        AddRows(state, "inventory", Inventory, item => new DropsRow
        {
            Id = Text(item, "id"), Primary = Text(item, "name"), Secondary = Text(item, "description"),
            Status = Bool(item, "claimed") ? "已领取" : "未领取", Payload = item.Clone(),
        });
        AddSoopProgressRows(state, replaceAccount: false);
        var activeAccounts = Accounts.Count(row => Bool(row.Payload, "running"));
        var availableTasks = Tasks.Count(row => Bool(row.Payload, "active"));
        vm.Status = activeAccounts > 0 || vm.Running
            ? "正在运行"
            : Accounts.Count == 0 ? "未配置" : availableTasks > 0 ? "就绪" : "待刷新";
        vm.Summary = $"{Accounts.Count} 个账号 · {availableTasks} 个可用任务";
        if (Bool(state, "refreshCompleted"))
        {
            if (SoopRetryLoopActive) CompleteSoopNetworkRecovery();
            else CompleteSoopRefresh();
        }
        UpdateSoopQuickStart(state);
    }

    private void AddSoopProgressRows(JsonElement owner, bool replaceAccount)
    {
        var account = Text(owner, "uid");
        if (replaceAccount && !string.IsNullOrWhiteSpace(account))
        {
            for (var index = SoopCurrentProgress.Count - 1; index >= 0; index--)
                if (string.Equals(SoopCurrentProgress[index].Account, account, StringComparison.Ordinal))
                    SoopCurrentProgress.RemoveAt(index);
        }
        if (!owner.TryGetProperty("currentProgress", out var rows) || rows.ValueKind != JsonValueKind.Array) return;
        foreach (var item in rows.EnumerateArray())
        {
            SoopCurrentProgress.Add(new SoopProgressRow
            {
                Id = Text(item, "id"),
                Account = Text(item, "account"),
                Channel = Text(item, "channel", "尚未进入直播间"),
                Campaign = Text(item, "campaign"),
                Reward = Text(item, "reward"),
                CurrentMinutes = (int)Number(item, "currentMinutes"),
                RequiredMinutes = (int)Number(item, "requiredMinutes"),
                Percent = Number(item, "percent"),
            });
        }
    }

    private void ApplyYouTube(JsonElement state, DropsPlatformViewModel vm)
    {
        var sessions = state.TryGetProperty("sessions", out var sessionArray) && sessionArray.ValueKind == JsonValueKind.Array
            ? sessionArray.EnumerateArray().Select(item => item.Clone()).ToArray()
            : [];
        var streamAvailable = state.TryGetProperty("stream", out var currentStream) && currentStream.ValueKind == JsonValueKind.Object;
        AddRows(state, "sessions", Accounts, item => new DropsRow
        {
            Id = Text(item, "profile"), Primary = Text(item, "profile"), Secondary = Text(item, "url"),
            Status = Bool(item, "running") && streamAvailable ? "正在观看" : "可用", Payload = item.Clone(),
        });
        if (state.TryGetProperty("config", out var config))
        {
            AddRows(config, "profiles", Accounts, item => new DropsRow
            {
                Id = item.GetString() ?? "", Primary = item.GetString() ?? "", Secondary = "独立浏览器登录资料",
                Status = sessions.Any(session => Text(session, "profile") == item.GetString() && Bool(session, "running"))
                    ? (streamAvailable ? "正在观看" : "可用") : "未验证登录", Payload = item.Clone(),
            }, distinct: true);
            AddRows(config, "channels", Channels, item => new DropsRow
            {
                Id = Text(item, "id", Text(item, "url")), Primary = Text(item, "name"), Secondary = Text(item, "url"),
                Enabled = Bool(item, "enabled", true), Payload = item.Clone(),
            });
        }
        if (streamAvailable)
        {
            var stream = currentStream;
            Tasks.Add(new DropsRow { Id = Text(stream, "videoId"), Primary = Text(stream, "title"),
                Secondary = Text(stream, "channel"), Status = "当前直播", Payload = stream.Clone() });
            _youtubeCurrentLabel = Text(stream, "channel", Text(stream, "title", "YouTube"));
        }
        if (state.TryGetProperty("history", out var history))
            AddRows(history, "rows", History, item => new DropsRow
            {
                Id = Text(item, "videoId"), Primary = Text(item, "title"),
                Secondary = $"{Text(item, "date")} · {Text(item, "profile")}",
                Status = FormatSeconds(Number(item, "watch_seconds")), Payload = item.Clone(),
            });
        var enabledChannels = Channels.Count(row => row.Enabled);
        vm.Status = vm.Running ? (Tasks.Count > 0 ? "正在观看" : "正在检查频道") : "未运行";
        if (Tasks.Count > 0)
        {
            var seconds = History.Where(row => row.Id == Tasks[0].Id).Sum(row => Number(row.Payload, "watch_seconds"));
            vm.Summary = seconds > 0 ? $"{_youtubeCurrentLabel} · 已观看 {FormatSeconds(seconds)}" : $"{_youtubeCurrentLabel} · 刚刚开始观看";
        }
        else
            vm.Summary = vm.Running
                ? $"正在检查 {enabledChannels} 个已启用频道"
                : $"{Accounts.Count} 个观看账号 · {enabledChannels} 个启用频道";
        UpdateYouTubeQuickStart(state, enabledChannels);
    }

    private void ApplyTwitch(JsonElement state, DropsPlatformViewModel vm)
    {
        AddRows(state, "accounts", Accounts, item => new DropsRow
        {
            Id = Text(item, "userId"), Primary = Text(item, "userId", "已保存 Session"),
            Status = Bool(item, "loggedIn") ? "已登录" : "待验证", Payload = item.Clone(),
        });
        if (!_twitchCampaignScopeInitialized && state.TryGetProperty("settings", out var settings) &&
            settings.ValueKind == JsonValueKind.Object)
        {
            _twitchCampaignScope = string.Equals(Text(settings, "priority_mode"), "PRIORITY_ONLY",
                StringComparison.OrdinalIgnoreCase)
                ? TwitchCampaignScope.Priority
                : TwitchCampaignScope.Available;
            _twitchCampaignScopeInitialized = true;
            Raise(nameof(TwitchCampaignScopeKey));
        }
        _twitchCampaigns.Clear();
        if (state.TryGetProperty("campaigns", out var campaigns) && campaigns.ValueKind == JsonValueKind.Array)
            _twitchCampaigns.AddRange(campaigns.EnumerateArray().Select(item => item.Clone()));
        AddRows(state, "inventory", Inventory, item => new DropsRow
        {
            Id = Text(item, "id"), Primary = Text(item, "name"), Secondary = Text(item, "campaign"),
            Status = TwitchDropStatus(item),
            Progress = Number(item, "progress") * 100, Payload = item.Clone(),
            Completed = Bool(item, "completed"), Claimed = Bool(item, "claimed"),
            CanClaim = Bool(item, "canClaim"),
        });
        AddRows(state, "channels", Channels, item => new DropsRow
        {
            Id = Text(item, "id"), Primary = Text(item, "name"),
            Secondary = $"{Text(item, "game", "未知游戏")} · {(Bool(item, "dropsEnabled") ? "可获得掉宝" : "无掉宝")}",
            Status = Bool(item, "online") ? "直播中" : "离线", Payload = item.Clone(),
        });
        var loggedIn = state.TryGetProperty("accounts", out var accountArray) && accountArray.ValueKind == JsonValueKind.Array &&
                       accountArray.EnumerateArray().Any(item => Bool(item, "loggedIn"));
        var authState = Text(state, "authState", loggedIn ? "logged_in" : vm.Running ? "checking" : "logged_out");
        if (loggedIn && (authState == "checking" || authState == "logged_out")) authState = "logged_in";
        if (state.TryGetProperty("authRequired", out var authRequired) && authRequired.ValueKind == JsonValueKind.Object)
            SetTwitchAuthorization(Text(authRequired, "url"), Text(authRequired, "code"), Bool(authRequired, "automatic"));
        else
            SetTwitchAuthState(authState);
        SetTwitchLoggedIn(loggedIn);
        if (loggedIn) IsTwitchLoginInProgress = false;
        Raise(nameof(TwitchLoginVisibility));
        Raise(nameof(TwitchLogoutVisibility));
        RebuildTwitchCampaigns();
        var availableCampaigns = _twitchCampaigns.Count(item => Bool(item, "available"));
        var hasCurrentChannel = state.TryGetProperty("currentChannel", out var currentChannel) &&
                                currentChannel.ValueKind == JsonValueKind.Object;
        var connectionState = Text(state, "connectionState");
        if (authState == "failed")
        {
            SetTwitchTemporaryNetworkFailure(
                Text(state, "loginError", "Twitch 暂时无法连接。"));
        }
        else if (authState == "needs_login")
        {
            SetTwitchAuthState("needs_login");
            IsTwitchLoginInProgress = false;
            SetTwitchStage(TwitchConnectionStage.AuthenticationExpired,
                "Twitch 登录已失效", "请重新完成 Twitch 授权");
        }
        else if (authState == "authorization_required")
        {
            SetTwitchStage(TwitchConnectionStage.WaitingAuthorization,
                "等待完成 Twitch 授权", "请在浏览器中输入上方验证码完成登录");
        }
        else if (!loggedIn)
        {
            if (authState == "checking")
                SetTwitchStage(TwitchConnectionStage.CheckingSession,
                    "正在检查 Twitch 登录状态…", "正在检查本机保存的登录信息", monitor: true);
            else if (!_twitchConnectionIntent)
                SetTwitchStage(TwitchConnectionStage.Unconnected,
                    "Twitch 尚未连接", "尚未登录 Twitch");
        }
        else if (!vm.Running)
        {
            SetTwitchStage(TwitchConnectionStage.LoginSucceeded,
                "Twitch 登录成功", availableCampaigns > 0
                    ? $"已加载 {availableCampaigns} 个当前可掉宝活动"
                    : "已登录，可开始 Twitch 掉宝");
        }
        else if (!string.IsNullOrWhiteSpace(connectionState) && connectionState != "unconnected")
        {
            ApplyTwitchConnectionPhase(connectionState);
        }
        else if (hasCurrentChannel)
        {
            SetTwitchStage(TwitchConnectionStage.Running,
                "Twitch 掉宝正在运行",
                $"{Text(currentChannel, "name", "当前频道")} · {Text(currentChannel, "game", "掉宝进行中")}");
        }
        else
        {
            SetTwitchStage(TwitchConnectionStage.Running,
                "Twitch 掉宝正在运行", availableCampaigns > 0
                    ? $"已加载 {availableCampaigns} 个当前可掉宝活动"
                    : "正在等待可用掉宝活动");
        }
        TwitchInventoryEmptyText = loggedIn ? "当前没有正在进行的掉宝。" : "请先登录 Twitch 账号。";
        TwitchChannelsEmptyText = loggedIn ? "当前没有符合条件的在线频道。" : "请先登录 Twitch 账号。";
        UpdateTwitchQuickStart(state, loggedIn);
    }

    private void ApplyTwitchConnectionPhase(string phase, string detail = "")
    {
        switch (phase)
        {
            case "worker_starting":
                SetTwitchStage(TwitchConnectionStage.WorkerStarting,
                    "正在启动 Twitch 服务…", "正在准备 Twitch 连接", monitor: true);
                break;
            case "connecting":
                SetTwitchStage(TwitchConnectionStage.Connecting,
                    "正在连接 Twitch 服务器…", "正在建立安全连接", monitor: true);
                break;
            case "checking_session":
                SetTwitchStage(TwitchConnectionStage.CheckingSession,
                    "正在检查 Twitch 登录状态…", "正在检查本机保存的登录信息", monitor: true);
                break;
            case "restoring_session":
                SetTwitchStage(TwitchConnectionStage.RestoringSession,
                    "正在恢复 Twitch 登录…", "正在验证已有登录信息", monitor: true);
                break;
            case "requesting_authorization":
                SetTwitchStage(TwitchConnectionStage.RequestingAuthorization,
                    "正在获取 Twitch 登录授权…", "正在向 Twitch 请求授权信息", monitor: true);
                break;
            case "login_succeeded":
                SetTwitchLoggedIn(true);
                IsTwitchLoginInProgress = false;
                SetTwitchAuthState("logged_in");
                SetTwitchStage(TwitchConnectionStage.LoginSucceeded,
                    "Twitch 登录成功", _twitchStartIntent
                        ? "正在继续加载 Twitch 掉宝数据" : "已登录，可开始 Twitch 掉宝");
                Raise(nameof(TwitchLoginVisibility));
                Raise(nameof(TwitchLogoutVisibility));
                break;
            case "loading_campaigns":
                SetTwitchStage(TwitchConnectionStage.LoadingCampaigns,
                    "正在获取 Twitch 掉宝活动…", "正在加载活动与奖励进度", monitor: true);
                break;
            case "campaigns_loaded":
                SetTwitchStage(TwitchConnectionStage.LoadingChannels,
                    "Twitch 掉宝活动已加载", "正在加载可用掉宝任务…", monitor: true);
                break;
            case "loading_channels":
                SetTwitchStage(TwitchConnectionStage.LoadingChannels,
                    "正在加载可用掉宝任务…", "正在查找符合条件的频道", monitor: true);
                break;
            case "realtime_connecting":
                SetTwitchStage(TwitchConnectionStage.ConnectingRealtime,
                    "正在建立 Twitch 实时连接…", "登录状态正常，正在连接实时服务", monitor: true);
                ScheduleTwitchRetry(showWaiting: false);
                break;
            case "realtime_connected":
                SetTwitchStage(TwitchConnectionStage.Connected,
                    "Twitch 已连接", "实时连接已建立");
                break;
            case "realtime_reconnecting":
                SetTwitchStage(TwitchConnectionStage.Reconnecting,
                    "Twitch 连接中断，正在重新连接…", "已保持登录，实时连接正在自动恢复");
                ScheduleTwitchRetry(showWaiting: false);
                break;
            case "realtime_disconnected":
                if (_twitchUserStopped || _twitchUserLoggedOut || _twitchApplicationStopping ||
                    _twitchRetryAttemptInProgress || !_twitchStartIntent) break;
                SetTwitchStage(TwitchConnectionStage.Reconnecting,
                    "Twitch 连接中断，正在重新连接…", "已保持登录，将自动恢复连接");
                ScheduleTwitchRetry(showWaiting: false);
                break;
            case "service_ready":
                SetTwitchStage(TwitchConnectionStage.Connected,
                    "Twitch 已连接", "已登录，当前没有可运行的掉宝活动");
                break;
            case "running":
                SetTwitchStage(TwitchConnectionStage.Running,
                    "Twitch 掉宝正在运行", "实时连接已建立");
                break;
            case "proxy_fallback":
                RecordTwitchFailure(TwitchConnectionFailureKind.Proxy,
                    "Twitch 代理连接失败，正在尝试直连。", retryable: true);
                SetTwitchStage(TwitchConnectionStage.Connecting,
                    "Twitch 代理连接失败，正在尝试直连", "正在尝试不使用代理连接 Twitch", monitor: true);
                break;
            case "proxy_failed":
                RecordTwitchFailure(TwitchConnectionFailureKind.Proxy,
                    FriendlyTwitchFailure(TwitchConnectionFailureKind.Proxy, ""), retryable: true);
                SetTwitchStage(TwitchConnectionStage.Connecting,
                    "Twitch 代理连接失败，仍在尝试…", _twitchLastError, monitor: true);
                break;
            case "proxy_and_direct_failed":
                RecordTwitchFailure(TwitchConnectionFailureKind.ProxyAndDirect,
                    FriendlyTwitchFailure(TwitchConnectionFailureKind.ProxyAndDirect, ""), retryable: true);
                SetTwitchStage(TwitchConnectionStage.Connecting,
                    "Twitch 无法连接，仍在尝试…", _twitchLastError, monitor: true);
                break;
            case "network_failed":
            {
                var networkKind = ClassifyTwitchFailure(detail, detail);
                RecordTwitchFailure(networkKind, FriendlyTwitchFailure(networkKind, ""), retryable: true);
                SetTwitchStage(TwitchConnectionStage.Connecting,
                    "Twitch 连接失败，仍在尝试…", _twitchLastError, monitor: true);
                break;
            }
            case "ssl_certificate_failed":
                SetTwitchFailure(TwitchConnectionFailureKind.SslCertificate,
                    FriendlyTwitchFailure(TwitchConnectionFailureKind.SslCertificate, ""),
                    retryable: false, scheduleRetry: false);
                break;
        }
    }

    private void UpdateSoopQuickStart(JsonElement state = default)
    {
        if (state.ValueKind == JsonValueKind.Object)
            _soopSettingsReady = state.TryGetProperty("settings", out var settings) &&
                                 settings.ValueKind == JsonValueKind.Object;
        var hasSettings = _soopSettingsReady;
        SoopQuickStart.Steps[0].Update(Accounts.Count > 0 ? "complete" : "incomplete",
            Accounts.Count > 0 ? $"✓ 已完成 · {Accounts.Count} 个账号" : "○ 未完成", Accounts.Count > 0,
            Accounts.Count == 0 ? "添加账号" : "");
        SoopQuickStart.Steps[1].Update(hasSettings ? "complete" : "incomplete",
            hasSettings ? "✓ 已完成 · 已采用当前策略" : "○ 未完成", hasSettings,
            hasSettings ? "" : "查看设置");
        var refreshWaiting = _soopRefreshPending && SoopRetryLoopActive;
        SoopQuickStart.Steps[2].Update(IsSoopRefreshing || refreshWaiting ? "progress" :
                _soopHasRefreshed ? "complete" : "incomplete",
            IsSoopRefreshing ? "● 正在进行" : refreshWaiting ? "● 网络异常，等待自动重试" :
                _soopHasRefreshed ? "✓ 已完成" : "○ 未完成",
            _soopHasRefreshed, IsSoopRefreshing || refreshWaiting ? "" : "刷新掉宝信息");
        SoopQuickStart.Steps[3].Update(Soop.Running ? "progress" : "incomplete",
            Soop.Running ? "● 正在进行" : "○ 未完成 · 当前未运行", Soop.Running,
            Soop.Running || Accounts.Count == 0 ? "" : "启动选中账号");
        SoopQuickStart.RefreshSummary();
    }

    private void UpdateYouTubeQuickStart(JsonElement state, int enabledChannels)
    {
        var browserPath = Text(state, "detectedBrowserPath");
        var browserName = state.TryGetProperty("config", out var config) &&
                          string.Equals(Text(config, "browser"), "brave", StringComparison.OrdinalIgnoreCase)
            ? "Brave" : "Google Chrome";
        var browserFound = !string.IsNullOrWhiteSpace(browserPath);
        YouTubeQuickStart.Steps[0].Update(browserFound ? "complete" : "incomplete",
            browserFound ? $"✓ 已找到 {browserName}" : "○ 尚未找到浏览器", browserFound,
            browserFound ? "" : "选择浏览器");
        YouTubeQuickStart.Steps[1].Update(Accounts.Count > 0 ? "complete" : "incomplete",
            Accounts.Count > 0 ? $"✓ 已完成 · {Accounts.Count} 个观看账号" : "○ 未完成", Accounts.Count > 0,
            Accounts.Count > 0 ? "打开登录窗口" : "添加观看账号");
        YouTubeQuickStart.Steps[2].Update(enabledChannels > 0 ? "complete" : "incomplete",
            enabledChannels > 0 ? $"✓ 已完成 · {enabledChannels} 个频道已启用" : "○ 未完成", enabledChannels > 0,
            enabledChannels > 0 ? "" : "添加频道");
        YouTubeQuickStart.Steps[3].Update(YouTube.Running ? "progress" : "incomplete",
            YouTube.Running ? "● 正在进行" : "○ 未完成 · 当前未运行", YouTube.Running,
            YouTube.Running ? "" : "开始观看");
        YouTubeQuickStart.RefreshSummary();
    }

    private void UpdateTwitchQuickStart(JsonElement state, bool loggedIn)
    {
        if (state.ValueKind == JsonValueKind.Object)
            _twitchSettingsReady = state.TryGetProperty("settings", out var settings) &&
                                   settings.ValueKind == JsonValueKind.Object;
        var settingsReady = _twitchSettingsReady;
        var checking = IsTwitchLoginInProgress || _twitchConnectionStage is
            TwitchConnectionStage.WorkerStarting or TwitchConnectionStage.Connecting or
            TwitchConnectionStage.CheckingSession or TwitchConnectionStage.RestoringSession or
            TwitchConnectionStage.RequestingAuthorization or TwitchConnectionStage.Slow or
            TwitchConnectionStage.RetryWaiting;
        var waiting = _twitchAuthState == "authorization_required";
        var failed = _twitchAuthState == "failed";
        TwitchQuickStart.Steps[0].Update(loggedIn ? "complete" : checking || waiting ? "progress" : "incomplete",
            loggedIn ? "✓ 已登录 Twitch"
                : waiting ? "● 正在进行 · 等待用户授权"
                : checking ? $"● 正在进行 · {Twitch.Status}"
                : failed ? "○ 暂时无法连接"
                : _twitchAuthState == "needs_login" ? "○ 需要重新登录" : "○ 尚未登录 Twitch",
            loggedIn, loggedIn ? "" : "登录 Twitch");
        TwitchQuickStart.Steps[1].Update(settingsReady ? "complete" : "incomplete",
            settingsReady ? "✓ 已完成 · 已采用当前设置" : "○ 未完成", settingsReady,
            settingsReady ? "" : "查看设置");
        TwitchQuickStart.Steps[2].Update(Twitch.Running && loggedIn ? "progress" : "incomplete",
            Twitch.Running && loggedIn ? "● 正在进行" : "○ Twitch 当前未运行", Twitch.Running && loggedIn,
            loggedIn && !Twitch.Running ? "开始 Twitch 掉宝" : "");
        TwitchQuickStart.RefreshSummary();
    }

    private void RebuildTwitchCampaigns()
    {
        Tasks.Clear();
        foreach (var item in _twitchCampaigns.Where(IsTwitchCampaignVisible))
        {
            var completed = Number(item, "completedDrops");
            var total = Number(item, "totalDrops");
            var remaining = Number(item, "remainingMinutes");
            Tasks.Add(new DropsRow
            {
                Id = Text(item, "id"),
                Primary = Text(item, "name"),
                Secondary = Text(item, "game"),
                Status = CampaignStatus(Text(item, "availability")),
                Detail = remaining > 0
                    ? $"{completed:0}/{total:0} 个奖励 · 剩余 {remaining:0} 分钟"
                    : $"{completed:0}/{total:0} 个奖励",
                Progress = Number(item, "progress") * 100,
                Payload = item.Clone(),
            });
        }
        TwitchCampaignsEmptyText = !_twitchLoggedIn
            ? "请先登录 Twitch 账号。"
            : _twitchCampaignScope switch
            {
                TwitchCampaignScope.Priority => "当前没有可掉宝的优先游戏活动。",
                TwitchCampaignScope.All => "当前没有已获取到的掉宝活动。",
                _ => "当前没有可参与的掉宝活动。",
            };
    }

    private bool IsTwitchCampaignVisible(JsonElement campaign) => _twitchCampaignScope switch
    {
        TwitchCampaignScope.All => true,
        TwitchCampaignScope.Priority => Bool(campaign, "available") && Bool(campaign, "priority"),
        _ => Bool(campaign, "available"),
    };

    private static string CampaignStatus(string availability) => availability switch
    {
        "available" => "当前可掉宝",
        "upcoming" => "即将开始",
        "expired" => "已过期",
        "finished" => "已完成",
        "excluded" => "已排除",
        "ineligible" => "不符合资格",
        _ => "当前不可参与",
    };

    private static string TwitchDropStatus(JsonElement item)
    {
        if (Bool(item, "claimed")) return "已领取";
        if (Bool(item, "completed"))
            return Bool(item, "canClaim") ? "已完成" : "已完成 · 等待可领取";
        var required = Math.Max(0, Number(item, "requiredMinutes"));
        var current = Math.Min(Math.Max(0, Number(item, "currentMinutes")), required);
        return $"{current:0} / {required:0} 分钟";
    }

    private static void AddRows(JsonElement owner, string property, ObservableCollection<DropsRow> target,
        Func<JsonElement, DropsRow> create, bool distinct = false)
    {
        if (!owner.TryGetProperty(property, out var array) || array.ValueKind != JsonValueKind.Array) return;
        foreach (var item in array.EnumerateArray())
        {
            var row = create(item);
            if (distinct && target.Any(existing => existing.Id == row.Id)) continue;
            target.Add(row);
        }
    }

    private void OnSnapshotChanged(object? sender, WorkerSnapshot snapshot) => Dispatch(() =>
    {
        var vm = For(snapshot.Platform);
        if (snapshot.Lifecycle == WorkerLifecycle.Crashed)
        {
            vm.Status = snapshot.Platform == DropsPlatform.Twitch ? "Twitch 连接中断" : "运行异常";
            vm.Running = false;
            if (snapshot.Platform == DropsPlatform.Twitch &&
                _twitchLastFailureKind != TwitchConnectionFailureKind.SslRuntime)
                SetTwitchTemporaryNetworkFailure("Twitch 后台服务意外退出。");
        }
        else if (snapshot.Lifecycle == WorkerLifecycle.Stopped)
        {
            vm.Running = false;
            if (snapshot.Platform == DropsPlatform.Twitch && !_twitchRetryAttemptInProgress &&
                !_twitchUserStopped && !_twitchUserLoggedOut && _twitchConnectionIntent)
                ScheduleTwitchRetry(showWaiting: true);
        }
    });

    private void OnEventReceived(object? sender, WorkerEvent message) => Dispatch(() =>
    {
        if (message.Platform == DropsPlatform.Soop && message.Name == "account_status")
        {
            AddSoopProgressRows(message.Payload, replaceAccount: true);
            var soopStatus = Text(message.Payload, "status");
            var failureKind = ClassifySoopFailure(soopStatus);
            if (failureKind is SoopFailureKind.Authentication or SoopFailureKind.TransientNetwork)
                SetSoopFailure(soopStatus);
            else if (SoopRetryLoopActive && Bool(message.Payload, "running") &&
                     Bool(message.Payload, "connectionHealthy"))
            {
                CancelSoopRetry();
                _ = ConfirmSoopNetworkRecoveryAsync();
            }
        }
        if (message.Name == "status" && message.Platform != DropsPlatform.Twitch)
        {
            var vm = For(message.Platform);
            if (message.Payload.TryGetProperty("status", out var status)) vm.Status = status.GetString() ?? vm.Status;
            if (message.Payload.TryGetProperty("summary", out var summary) && !string.IsNullOrWhiteSpace(summary.GetString()))
                vm.Summary = summary.GetString()!;
            if (message.Payload.TryGetProperty("running", out var running)) vm.Running = running.GetBoolean();
        }
        if (message.Platform == DropsPlatform.Twitch && message.Name == "status" &&
            message.Payload.TryGetProperty("running", out var twitchRunning))
            Twitch.Running = twitchRunning.GetBoolean();
        if (message.Platform == DropsPlatform.Twitch && message.Name == "connection_status")
            ApplyTwitchConnectionPhase(Text(message.Payload, "phase"), Text(message.Payload, "detail"));
        if (message.Name == "runtime_error" && Text(message.Payload, "component") == "ssl")
            ApplyRuntimeError(message.Platform, Text(message.Payload, "code"), Text(message.Payload, "message"));
        if (message.Name == "runtime_recovered" && Text(message.Payload, "component") == "ssl")
        {
            if (message.Platform == DropsPlatform.Twitch)
            {
                ClearTwitchFailure();
                SetTwitchStage(TwitchConnectionStage.Unconnected,
                    "Twitch SSL 组件已恢复", "可以重新检测或登录 Twitch");
            }
            else
            {
                var recovered = For(message.Platform);
                recovered.Status = "后台组件已恢复";
                recovered.Summary = "可以重新启动 Drops 后台。";
            }
        }
        if (message.Platform == DropsPlatform.Twitch && message.Name == "auth_state")
        {
            var state = Text(message.Payload, "state", "logged_out");
            SetTwitchAuthState(state);
            if (state == "checking")
                SetTwitchStage(TwitchConnectionStage.CheckingSession,
                    "正在检查 Twitch 登录状态…", "正在检查本机保存的登录信息", monitor: true);
            else if (state == "failed")
                SetTwitchTemporaryNetworkFailure(Text(message.Payload, "error", "Twitch 暂时无法连接。"));
            else if (state == "needs_login")
            {
                Twitch.Running = false;
                IsTwitchLoginInProgress = false;
                SetTwitchStage(TwitchConnectionStage.AuthenticationExpired,
                    "Twitch 登录已失效", "请重新完成 Twitch 授权");
            }
            UpdateTwitchQuickStart(default, _twitchLoggedIn);
        }
        if (message.Platform == DropsPlatform.Twitch && message.Name == "auth_required")
            SetTwitchAuthorization(Text(message.Payload, "url"), Text(message.Payload, "code"), Bool(message.Payload, "automatic"));
        if (message.Platform == DropsPlatform.Twitch && message.Name == "login_status" &&
            message.Payload.TryGetProperty("userId", out var twitchUserId) &&
            twitchUserId.ValueKind is not JsonValueKind.Null and not JsonValueKind.Undefined)
        {
            SetTwitchLoggedIn(true);
            IsTwitchLoginInProgress = false;
            SetTwitchAuthState("logged_in");
            SetTwitchStage(TwitchConnectionStage.LoginSucceeded,
                "Twitch 登录成功", _twitchStartIntent
                    ? "正在获取 Twitch 掉宝活动…" : "已登录，可开始 Twitch 掉宝");
            Raise(nameof(TwitchLoginVisibility));
            Raise(nameof(TwitchLogoutVisibility));
            UpdateTwitchQuickStart(default, true);
        }
        if (message.Platform == DropsPlatform.Twitch && message.Name == "games" && _twitchStartIntent)
            ApplyTwitchConnectionPhase("campaigns_loaded");
        if (message.Platform == DropsPlatform.Twitch && message.Name == "current_channel" &&
            message.Payload.ValueKind == JsonValueKind.Object && message.Payload.EnumerateObject().Any())
            SetTwitchStage(TwitchConnectionStage.Running,
                "Twitch 掉宝正在运行", "实时连接已建立");
        if (message.Platform == DropsPlatform.Twitch && message.Name == "error")
        {
            var category = Text(message.Payload, "category");
            var code = Text(message.Payload, "code");
            var kind = ClassifyTwitchFailure(code, Text(message.Payload, "message"));
            if (category == "authentication")
            {
                SetTwitchAuthState("needs_login");
                SetTwitchFailure(TwitchConnectionFailureKind.Authentication,
                    FriendlyTwitchFailure(TwitchConnectionFailureKind.Authentication, ""),
                    retryable: false, scheduleRetry: false);
            }
            else
            {
                var errorMessage = Text(message.Payload, "message");
                SetTwitchFailure(kind, FriendlyTwitchFailure(kind, errorMessage),
                    retryable: Bool(message.Payload, "retryable", true) &&
                               IsTwitchRetryableFailure(kind, errorMessage), scheduleRetry: true);
            }
        }
        if (message.Platform == DropsPlatform.YouTube && message.Name == "stream")
        {
            _lastYouTubeSuccessfulAt = DateTimeOffset.Now;
            RefreshTemporalStatus(DateTimeOffset.Now);
            _youtubeCurrentLabel = Text(message.Payload, "channel", Text(message.Payload, "title", "YouTube"));
            YouTube.Status = "正在观看";
            YouTube.Summary = $"{_youtubeCurrentLabel} · 刚刚开始观看";
        }
        if (message.Platform == DropsPlatform.YouTube && message.Name == "watch_time")
        {
            _lastYouTubeSuccessfulAt = DateTimeOffset.Now;
            RefreshTemporalStatus(DateTimeOffset.Now);
            YouTube.Status = "正在观看";
            YouTube.Summary = $"{_youtubeCurrentLabel} · 已观看 {FormatSeconds(Number(message.Payload, "seconds"))}";
        }
    });

    private DropsPlatformViewModel For(DropsPlatform platform) => platform switch
    {
        DropsPlatform.Soop => Soop, DropsPlatform.YouTube => YouTube, _ => Twitch,
    };

    private static string Text(JsonElement owner, string name, string fallback = "")
    {
        if (!owner.TryGetProperty(name, out var value) || value.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined) return fallback;
        return value.ValueKind == JsonValueKind.String ? value.GetString() ?? fallback : value.ToString();
    }
    private static bool Bool(JsonElement owner, string name, bool fallback = false) =>
        owner.TryGetProperty(name, out var value) && value.ValueKind is JsonValueKind.True or JsonValueKind.False ? value.GetBoolean() : fallback;
    private static double Number(JsonElement owner, string name) =>
        owner.TryGetProperty(name, out var value) && value.TryGetDouble(out var number) ? number : 0;
    private static string FormatSeconds(double seconds) => TimeSpan.FromSeconds(Math.Max(0, seconds)).ToString(@"hh\:mm\:ss");
    private static void Dispatch(Action action)
    {
        var dispatcher = Application.Current?.Dispatcher;
        if (dispatcher == null || dispatcher.CheckAccess()) action(); else dispatcher.BeginInvoke(action);
    }

    public void Dispose()
    {
        _soopApplicationStopping = true;
        _soopConnectionIntent = false;
        CancelSoopRetry();
        _twitchApplicationStopping = true;
        _twitchConnectionIntent = false;
        CancelTwitchRetry();
        _host.SnapshotChanged -= OnSnapshotChanged;
        _host.EventReceived -= OnEventReceived;
    }
}
