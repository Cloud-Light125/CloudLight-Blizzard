using System.Collections.ObjectModel;
using System.Text.Json;
using System.Windows;
using CloudLightBlizzard.Services.Drops;

namespace CloudLightBlizzard.ViewModels;

public sealed class DropsRow
{
    public string Id { get; init; } = "";
    public string Primary { get; init; } = "";
    public string Secondary { get; init; } = "";
    public string Status { get; init; } = "";
    public string Detail { get; init; } = "";
    public bool Enabled { get; init; }
    public double Progress { get; init; }
    public JsonElement Payload { get; init; }
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
    public ObservableCollection<string> TwitchAvailableGames { get; } = new();
    public ObservableCollection<string> TwitchPriorityChoices { get; } = new();
    public ObservableCollection<string> TwitchExcludeChoices { get; } = new();
    public ObservableCollection<string> TwitchPriorityGames { get; } = new();
    public ObservableCollection<string> TwitchExcludedGames { get; } = new();
    private readonly List<JsonElement> _twitchCampaigns = [];
    private TwitchCampaignScope _twitchCampaignScope = TwitchCampaignScope.Available;
    private bool _twitchCampaignScopeInitialized;
    private bool _twitchLoggedIn;
    public Visibility TwitchLoginVisibility => _twitchLoggedIn ? Visibility.Collapsed : Visibility.Visible;
    public Visibility TwitchLogoutVisibility => _twitchLoggedIn ? Visibility.Visible : Visibility.Collapsed;
    private bool _soopHasRefreshed;
    private bool _soopSettingsReady;
    private string _youtubeCurrentLabel = "YouTube";

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
    {
        _host = host;
        Platforms = [Soop, YouTube, Twitch];
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

    public void BeginSoopRefresh()
    {
        IsSoopRefreshing = true;
        SoopRefreshStatus = "正在刷新掉宝信息…";
        UpdateSoopQuickStart();
    }

    public void CompleteSoopRefresh()
    {
        _soopHasRefreshed = true;
        IsSoopRefreshing = false;
        var available = Tasks.Count(row => Bool(row.Payload, "active"));
        SoopRefreshStatus = $"已刷新 · 发现 {available} 个可用任务";
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

    public void BeginTwitchRefresh()
    {
        TwitchRefreshStatus = "正在重新获取活动、奖励与频道…";
        IsTwitchRefreshing = true;
    }

    public void CompleteTwitchRefresh(DateTimeOffset completedAt)
    {
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
        switch (platform)
        {
            case DropsPlatform.Soop: ApplySoop(state, vm); break;
            case DropsPlatform.YouTube: ApplyYouTube(state, vm); break;
            case DropsPlatform.Twitch: ApplyTwitch(state, vm); break;
        }
    }

    private void ApplySoop(JsonElement state, DropsPlatformViewModel vm)
    {
        _soopSettingsReady = state.TryGetProperty("settings", out var soopSettings) &&
                             soopSettings.ValueKind == JsonValueKind.Object;
        AddRows(state, "accounts", Accounts, item => new DropsRow
        {
            Id = Text(item, "uid"), Primary = Text(item, "uid"),
            Secondary = $"登录：已保存 · 直播间：{Text(item, "channelName", "尚未进入")} · 任务：{Text(item, "missionTitle", "等待刷新")}",
            Status = Bool(item, "running") ? "运行中" : "已停止", Payload = item.Clone(),
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
        var activeAccounts = Accounts.Count(row => Bool(row.Payload, "running"));
        var availableTasks = Tasks.Count(row => Bool(row.Payload, "active"));
        vm.Status = activeAccounts > 0 || vm.Running
            ? "正在运行"
            : Accounts.Count == 0 ? "未配置" : availableTasks > 0 ? "就绪" : "待刷新";
        vm.Summary = $"{Accounts.Count} 个账号 · {availableTasks} 个可用任务";
        UpdateSoopQuickStart(state);
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
            Status = Bool(item, "claimed") ? "已领取" : $"{Number(item, "currentMinutes"):0}/{Number(item, "requiredMinutes"):0} 分钟",
            Progress = Number(item, "progress") * 100, Payload = item.Clone(),
        });
        AddRows(state, "channels", Channels, item => new DropsRow
        {
            Id = Text(item, "id"), Primary = Text(item, "name"),
            Secondary = $"{Text(item, "game", "未知游戏")} · {(Bool(item, "dropsEnabled") ? "可获得掉宝" : "无掉宝")}",
            Status = Bool(item, "online") ? "直播中" : "离线", Payload = item.Clone(),
        });
        var loggedIn = state.TryGetProperty("accounts", out var accountArray) && accountArray.ValueKind == JsonValueKind.Array &&
                       accountArray.EnumerateArray().Any(item => Bool(item, "loggedIn"));
        _twitchLoggedIn = loggedIn;
        Raise(nameof(TwitchLoginVisibility));
        Raise(nameof(TwitchLogoutVisibility));
        RebuildTwitchCampaigns();
        var availableCampaigns = _twitchCampaigns.Count(item => Bool(item, "available"));
        var hasCurrentChannel = state.TryGetProperty("currentChannel", out var currentChannel) &&
                                currentChannel.ValueKind == JsonValueKind.Object;
        if (!loggedIn)
        {
            vm.Status = vm.Running ? "正在登录" : "未登录";
            vm.Summary = vm.Running ? "请在浏览器完成 Twitch 授权" : "尚未登录 Twitch";
        }
        else if (!vm.Running)
        {
            vm.Status = "已登录 · 未运行";
            vm.Summary = availableCampaigns > 0 ? $"{availableCampaigns} 个当前可掉宝活动" : "可以开始 Twitch 掉宝";
        }
        else if (hasCurrentChannel)
        {
            vm.Status = "正在观看";
            vm.Summary = $"{Text(currentChannel, "name", "当前频道")} · {Text(currentChannel, "game", "掉宝进行中")}";
        }
        else
        {
            vm.Status = availableCampaigns > 0 ? "正在寻找频道" : "Twitch 正在运行";
            vm.Summary = availableCampaigns > 0
                ? $"{availableCampaigns} 个当前可掉宝活动"
                : "正在获取或等待可用掉宝活动";
        }
        TwitchInventoryEmptyText = loggedIn ? "当前没有正在进行的掉宝。" : "请先登录 Twitch 账号。";
        TwitchChannelsEmptyText = loggedIn ? "当前没有符合条件的在线频道。" : "请先登录 Twitch 账号。";
        UpdateTwitchQuickStart(state, loggedIn);
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
        SoopQuickStart.Steps[2].Update(IsSoopRefreshing ? "progress" : _soopHasRefreshed ? "complete" : "incomplete",
            IsSoopRefreshing ? "● 正在进行" : _soopHasRefreshed ? "✓ 已完成" : "○ 未完成",
            _soopHasRefreshed, IsSoopRefreshing ? "" : "刷新掉宝信息");
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
        var settingsReady = state.TryGetProperty("settings", out var settings) && settings.ValueKind == JsonValueKind.Object;
        TwitchQuickStart.Steps[0].Update(loggedIn ? "complete" : Twitch.Running ? "progress" : "incomplete",
            loggedIn ? "✓ 已登录 Twitch" : Twitch.Running ? "● 正在进行 · 等待授权" : "○ 尚未登录 Twitch",
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
            var claimed = Number(item, "claimedDrops");
            var total = Number(item, "totalDrops");
            var remaining = Number(item, "remainingMinutes");
            Tasks.Add(new DropsRow
            {
                Id = Text(item, "id"),
                Primary = Text(item, "name"),
                Secondary = Text(item, "game"),
                Status = CampaignStatus(Text(item, "availability")),
                Detail = remaining > 0
                    ? $"{claimed:0}/{total:0} 个奖励 · 剩余 {remaining:0} 分钟"
                    : $"{claimed:0}/{total:0} 个奖励",
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
            vm.Status = "运行异常";
            vm.Running = false;
        }
        else if (snapshot.Lifecycle == WorkerLifecycle.Stopped)
            vm.Running = false;
    });

    private void OnEventReceived(object? sender, WorkerEvent message) => Dispatch(() =>
    {
        if (message.Name == "status")
        {
            var vm = For(message.Platform);
            if (message.Payload.TryGetProperty("status", out var status)) vm.Status = status.GetString() ?? vm.Status;
            if (message.Payload.TryGetProperty("summary", out var summary) && !string.IsNullOrWhiteSpace(summary.GetString()))
                vm.Summary = summary.GetString()!;
            if (message.Payload.TryGetProperty("running", out var running)) vm.Running = running.GetBoolean();
        }
        if (message.Platform == DropsPlatform.YouTube && message.Name == "stream")
        {
            _youtubeCurrentLabel = Text(message.Payload, "channel", Text(message.Payload, "title", "YouTube"));
            YouTube.Status = "正在观看";
            YouTube.Summary = $"{_youtubeCurrentLabel} · 刚刚开始观看";
        }
        if (message.Platform == DropsPlatform.YouTube && message.Name == "watch_time")
        {
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
        _host.SnapshotChanged -= OnSnapshotChanged;
        _host.EventReceived -= OnEventReceived;
    }
}
