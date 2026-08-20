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
        vm.Status = vm.Running ? "正在运行" : "未运行";
        vm.Summary = $"{Accounts.Count} 个账号 · {Tasks.Count} 个任务";
    }

    private void ApplyYouTube(JsonElement state, DropsPlatformViewModel vm)
    {
        AddRows(state, "sessions", Accounts, item => new DropsRow
        {
            Id = Text(item, "profile"), Primary = Text(item, "profile"), Secondary = Text(item, "url"),
            Status = Bool(item, "running") ? "浏览器运行中" : "已停止", Payload = item.Clone(),
        });
        if (state.TryGetProperty("config", out var config))
        {
            AddRows(config, "profiles", Accounts, item => new DropsRow
            {
                Id = item.GetString() ?? "", Primary = item.GetString() ?? "", Secondary = "独立浏览器登录资料",
                Status = "可使用", Payload = item.Clone(),
            }, distinct: true);
            AddRows(config, "channels", Channels, item => new DropsRow
            {
                Id = Text(item, "id", Text(item, "url")), Primary = Text(item, "name"), Secondary = Text(item, "url"),
                Enabled = Bool(item, "enabled", true), Payload = item.Clone(),
            });
        }
        if (state.TryGetProperty("stream", out var stream) && stream.ValueKind == JsonValueKind.Object)
            Tasks.Add(new DropsRow { Id = Text(stream, "videoId"), Primary = Text(stream, "title"),
                Secondary = Text(stream, "channel"), Status = "当前直播", Payload = stream.Clone() });
        if (state.TryGetProperty("history", out var history))
            AddRows(history, "rows", History, item => new DropsRow
            {
                Id = Text(item, "videoId"), Primary = Text(item, "title"),
                Secondary = $"{Text(item, "date")} · {Text(item, "profile")}",
                Status = FormatSeconds(Number(item, "watch_seconds")), Payload = item.Clone(),
            });
        vm.Status = vm.Running ? (Tasks.Count > 0 ? "正在观看" : "等待直播") : "等待直播";
        vm.Summary = $"{Accounts.Count} 个观看账号";
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
        RebuildTwitchCampaigns();
        vm.Status = loggedIn ? (vm.Running ? "正在获取掉宝" : "已登录") : "未登录";
        vm.Summary = Inventory.Count > 0
            ? $"当前掉宝：{Inventory[0].Primary} · {Inventory[0].Status}"
            : "暂无进行中的掉宝";
        TwitchInventoryEmptyText = loggedIn ? "当前没有正在进行的掉宝。" : "请先登录 Twitch 账号。";
        TwitchChannelsEmptyText = loggedIn ? "当前没有符合条件的在线频道。" : "请先登录 Twitch 账号。";
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
        vm.Status = snapshot.Status;
        vm.Summary = string.IsNullOrWhiteSpace(snapshot.Summary) ? vm.Summary : snapshot.Summary;
        vm.Running = snapshot.Lifecycle == WorkerLifecycle.Running && snapshot.Status is not "已停止" and not "就绪";
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
