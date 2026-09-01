using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using Microsoft.Win32;
using CloudLightBlizzard.Services;
using CloudLightBlizzard.Services.Drops;
using CloudLightBlizzard.ViewModels;

namespace CloudLightBlizzard.Views.Pages;

public partial class DropsPage : UserControl
{
    private const string SoopInventoryUrl = "https://drops.sooplive.com/inventory";
    private const string TwitchInventoryUrl = "https://www.twitch.tv/drops/inventory";
    private MainViewModel? _main;
    private DropsViewModel? _vm;
    private DropsPlatform _platform = DropsPlatform.Soop;
    private bool _initialized;
    private bool _loading;
    private PlatformLogTailService? _logTail;
    private readonly Dictionary<DropsPlatform, StringBuilder> _logBuffers = Enum.GetValues<DropsPlatform>()
        .ToDictionary(platform => platform, _ => new StringBuilder());
    private readonly Dictionary<DropsPlatform, long> _logVisibleRevisions = Enum.GetValues<DropsPlatform>()
        .ToDictionary(platform => platform, _ => 0L);
    private readonly SemaphoreSlim _logStartGate = new(1, 1);
    private readonly HashSet<string> _twitchClaimsInProgress = new(StringComparer.Ordinal);
    private CancellationTokenSource? _bilibiliQrPollingCts;
    private Task? _bilibiliQrPollingTask;
    private readonly DispatcherTimer _retryDisplayTimer = new() { Interval = TimeSpan.FromSeconds(1) };
    private bool _logTailStarted;

    public DropsPage()
    {
        InitializeComponent();
        _retryDisplayTimer.Tick += (_, _) => _vm?.RefreshTemporalStatus(DateTimeOffset.Now);
        IsVisibleChanged += async (_, _) =>
        {
            if (!IsVisible || !_initialized) return;
            await EnsureLogTailStartedAsync();
            if (_logTail is not null) await _logTail.RefreshAsync(_platform);
            RenderLogBuffer();
            await RefreshAsync();
        };
    }

    public void Initialize(MainViewModel main)
    {
        _main = main;
        _logTail = new PlatformLogTailService(main.DropsLogSession);
        _logTail.Changed += OnLogTailChanged;
        _vm = new DropsViewModel(main.DropsHost);
        _vm.BilibiliDetails.ConfigureCommands(new(
            ScanQrLogin: StartBilibiliQrLoginAsync,
            CancelQr: CancelBilibiliQrAsync,
            ReacquireQr: StartBilibiliQrLoginAsync,
            ManualCookie: ImportBilibiliCookieAsync,
            Logout: LogoutBilibiliAsync,
            Discover: DiscoverBilibiliAsync,
            Refresh: () => RefreshBilibiliAsync(),
            AddRoom: AddBilibiliRoomAsync,
            RemoveRoom: RemoveBilibiliRoomAsync,
            SetRoomEnabled: SetBilibiliRoomEnabledAsync,
            Start: () => StartPlatformAsync(DropsPlatform.Bilibili),
            Stop: () => StopPlatformAsync(DropsPlatform.Bilibili),
            SaveSettings: () => SaveBilibiliSettingsAsync(showError: true),
            ClearNotifier: ClearBilibiliNotifierAsync,
            RefreshSessions: RefreshBilibiliSessionsAsync,
            ClaimReward: ClaimBilibiliRewardAsync));
        _vm.BilibiliDetails.CommandFailed += (_, ex) => ShowError(ex, "哔哩哔哩操作失败");
        main.SetDropsDiagnosticSnapshotProvider(_vm.CreateDiagnosticSnapshot);
        DataContext = _vm;
        _vm.UpdateProxySettings(main.Settings.EnableProxy, main.Settings.ProxyUrl, main.Settings.FallbackDirect);
        main.DropsHost.EventReceived += OnWorkerEvent;
        SoopAutoStart.IsChecked = main.Settings.AutoStartSoop;
        TwitchAutoStart.IsChecked = main.Settings.AutoStartTwitch;
        BilibiliEnabledCheck.IsChecked = main.Settings.BilibiliEnabled;
        BilibiliAutoStart.IsChecked = main.Settings.AutoStartBilibili;
        BilibiliAutoResume.IsChecked = main.Settings.AutoResumeBilibiliDrops;
        _vm.BilibiliDetails.AutoRestore = main.Settings.AutoStartBilibili;
        _vm.BilibiliDetails.AutoResume = main.Settings.AutoResumeBilibiliDrops;
        _vm.BilibiliDetails.Enabled = main.Settings.BilibiliEnabled;
        _vm.BilibiliDetails.WatchMode = main.Settings.BilibiliWatchMode;
        _vm.BilibiliDetails.SessionsPerRoom = Math.Max(1, main.Settings.BilibiliSessionsPerRoom);
        _vm.BilibiliDetails.ReconnectDelayText = Math.Max(1, main.Settings.BilibiliReconnectDelaySeconds).ToString();
        _vm.BilibiliDetails.TaskIntervalText = Math.Max(10, main.Settings.BilibiliTaskIntervalSeconds).ToString();
        _vm.BilibiliDetails.TaskIdsText = string.Join(", ", main.Settings.BilibiliTaskIds ?? []);
        _vm.BilibiliDetails.AutoClaim = main.Settings.BilibiliAutoClaim;
        _vm.BilibiliDetails.TaskNotifications = main.Settings.BilibiliTaskNotifications;
        _vm.SetSoopAutoStartEnabled(main.Settings.AutoStartSoop);
        _vm.SetTwitchAutoStartEnabled(main.Settings.AutoStartTwitch);
        SoopTab.IsChecked = true;
        _vm.SelectPlatform(DropsPlatform.Soop);
        _vm.RefreshTemporalStatus(DateTimeOffset.Now);
        _retryDisplayTimer.Start();
        _initialized = true;
    }

    public void StartAutomaticPlatforms()
    {
        if (_main?.Settings.AutoStartSoop == true) _ = AutoStartSoopAsync();
        if (_main?.Settings.AutoStartTwitch == true) _ = AutoStartTwitchAsync();
        if (_main?.Settings.AutoStartBilibili == true) _ = AutoStartBilibiliAsync();
        // YouTube intentionally remains manual-only.
    }

    private async Task AutoStartBilibiliAsync()
    {
        if (_vm == null || _main == null) return;
        try
        {
            var state = await _vm.LoadAsync(DropsPlatform.Bilibili);
            _vm.ApplyState(DropsPlatform.Bilibili, state);
            PopulateSettings(DropsPlatform.Bilibili, state);
            await EnsureBilibiliCredentialsAsync(showError: false);
            if (_main.Settings.AutoResumeBilibiliDrops && _main.Settings.BilibiliEnabled)
                await StartPlatformAsync(DropsPlatform.Bilibili, automatic: true);
        }
        catch (Exception ex)
        {
            _vm.Bilibili.Status = "恢复失败";
            _vm.Bilibili.Summary = "Bilibili 自动恢复失败，请打开页面检查登录状态。";
            _vm.BilibiliDetails.HandleEvent("warning", JsonSerializer.SerializeToElement(new
            {
                code = "auto_restore_failed", message = SensitiveDataRedactor.Redact(ex.Message), retryable = true,
            }));
        }
    }

    private async Task AutoStartSoopAsync()
    {
        if (_vm == null) return;
        try
        {
            _vm.Soop.Status = "正在恢复 SOOP 账号…";
            _vm.Soop.Summary = "正在读取已保存的主账号";

            // 自动启动与手动启动共用同一个 start_account 业务入口。
            // 自动流程只额外负责从已保存账号中找到明确设置的主账号。
            var initialState = await _vm.LoadAsync(DropsPlatform.Soop);
            JsonElement primaryAccount = default;
            if (initialState.TryGetProperty("accounts", out var accounts) &&
                accounts.ValueKind == JsonValueKind.Array)
            {
                primaryAccount = accounts.EnumerateArray()
                    .FirstOrDefault(account => Bool(account, "primary"));
            }

            var uid = primaryAccount.ValueKind == JsonValueKind.Object
                ? Text(primaryAccount, "uid")
                : "";
            if (string.IsNullOrWhiteSpace(uid))
            {
                _vm.Soop.Running = false;
                _vm.Soop.Status = "需要设置主账号";
                _vm.Soop.Summary = "SOOP 自动启动已开启，但尚未设置主账号。";
                return;
            }

            _vm.Soop.Status = "正在启动主账号…";
            _vm.Soop.Summary = uid;
            _vm.BeginSoopStart(uid, automatic: true);
            await _vm.RequestAsync(DropsPlatform.Soop, "start_account", new { userid = uid });

            // 自动启动成功后复用与手动按钮相同的正式 refresh 入口。
            // Worker 的结构化 refreshCompleted 状态决定快速开始第三步是否完成。
            await RefreshSoopAsync(showError: false);
        }
        catch (Exception ex)
        {
            _vm.SetSoopFailure(ex.Message);
        }
    }

    private async Task AutoStartTwitchAsync()
    {
        if (_vm == null) return;
        try
        {
            _vm.BeginTwitchStart(automatic: true);

            // 自动启动与手动“开始 Twitch 掉宝”共用 start 命令。
            // automatic=true 只用于避免 Session 失效时在后台强制打开授权页面；
            // 登录、初始化活动/频道和实际掉宝流程仍走与手动启动相同的 Worker 路径。
            var state = await _vm.RequestAsync(
                DropsPlatform.Twitch, "start", new { automatic = true });

            if (_platform == DropsPlatform.Twitch)
            {
                _vm.ApplyState(DropsPlatform.Twitch, state);
                PopulateSettings(DropsPlatform.Twitch, state);
            }

            // 不再调用 Worker 的 auto_start 阻塞流程。
            // 登录成功、需要重新登录、启动失败等状态均由 auth_state /
            // auth_required / login_status / status 实时事件驱动。
        }
        catch (Exception ex)
        {
            _vm.SetTwitchTemporaryNetworkFailure(ex.Message);
        }
    }

    public async Task RefreshAsync()
    {
        if (_vm == null || _loading) return;
        if (_main != null)
            _vm.UpdateProxySettings(_main.Settings.EnableProxy, _main.Settings.ProxyUrl, _main.Settings.FallbackDirect);
        _loading = true;
        try
        {
            JsonElement selectedState = default;
            foreach (var platform in Enum.GetValues<DropsPlatform>())
            {
                var state = await _vm.LoadAsync(platform);
                _vm.ApplyState(platform, state);
                if (platform == _platform) selectedState = state.Clone();
            }
            if (selectedState.ValueKind != JsonValueKind.Undefined)
            {
                _vm.ApplyState(_platform, selectedState);
                PopulateSettings(_platform, selectedState);
            }
        }
        catch (Exception ex) { ShowError(ex, "刷新掉宝总览失败"); }
        finally { _loading = false; }
    }

    private async Task LoadPlatformAsync(DropsPlatform platform)
    {
        if (_vm == null || _loading) return;
        _loading = true;
        try
        {
            var state = await _vm.LoadAsync(platform);
            _vm.ApplyState(platform, state);
            PopulateSettings(platform, state);
        }
        catch (Exception ex) { ShowError(ex, $"加载 {PlatformName(platform)} 状态失败"); }
        finally { _loading = false; }
    }

    private async Task RefreshPlatformAsync(DropsPlatform platform)
    {
        if (_vm == null || _loading) return;
        _loading = true;
        try
        {
            var state = await _vm.RequestAsync(platform, "refresh");
            _vm.ApplyState(platform, state);
            PopulateSettings(platform, state);
        }
        catch (Exception ex) { ShowError(ex, $"刷新 {PlatformName(platform)} 失败"); }
        finally { _loading = false; }
    }

    private void PopulateSettings(DropsPlatform platform, JsonElement state)
    {
        var settingsName = platform == DropsPlatform.YouTube ? "config" : "settings";
        if (!state.TryGetProperty(settingsName, out var settings) || settings.ValueKind != JsonValueKind.Object) return;

        if (platform == DropsPlatform.Soop)
        {
            SelectTag(SoopChannelMode, Text(settings, "channel_mode", "smart"));
            SoopManualChannel.Text = Text(settings, "manual_input");
            SoopPreferredChannel.Text = Text(settings, "preferred_bjid", "owesports");
            PopulateSoopMissions(state, Text(settings, "priority_mission_id", "auto"));
            SoopAutoClaim.IsChecked = Bool(settings, "auto_claim_enabled");
            SoopLowBandwidth.IsChecked = Bool(settings, "low_bandwidth_mode", true);
            SoopHangWithoutMissions.IsChecked = Bool(settings, "hang_without_missions", true);
            SoopMissionInterval.Text = Int(settings, "mission_poll_interval", 90).ToString();
            SoopInventoryInterval.Text = Int(settings, "inventory_poll_interval", 300).ToString();
            SoopChannelInterval.Text = Int(settings, "channel_refresh_interval", 300).ToString();
            UpdateSoopChannelFields();
            return;
        }

        if (platform == DropsPlatform.YouTube)
        {
            SelectTag(YouTubeBrowser, Text(settings, "browser", "chrome"));
            var configuredPath = Text(settings, "browser_path");
            YouTubeBrowserPath.Text = string.IsNullOrWhiteSpace(configuredPath)
                ? Text(state, "detectedBrowserPath")
                : configuredPath;
            YouTubeHeadless.IsChecked = Bool(settings, "headless");
            YouTubeMute.IsChecked = Bool(settings, "mute", true);
            SelectTag(YouTubeMode, Text(settings, "mode", "auto"));
            YouTubeManualUrl.Text = Text(settings, "manual_url");
            YouTubeInterval.Text = Int(settings, "check_interval", 300).ToString();
            UpdateYouTubeModeFields();
            return;
        }

        if (platform == DropsPlatform.Bilibili)
        {
            _vm?.BilibiliDetails.ApplyState(state);
            if (_main is not null && _vm is not null)
            {
                _vm.BilibiliDetails.AutoRestore = _main.Settings.AutoStartBilibili;
                _vm.BilibiliDetails.AutoResume = _main.Settings.AutoResumeBilibiliDrops;
            }
            return;
        }

        PopulateTwitchLanguages(state, Text(settings, "language", "简体中文"));
        SelectTag(TwitchPriorityMode, Text(settings, "priority_mode", "PRIORITY_ONLY"));
        SelectTag(TwitchCampaignScopePicker, _vm?.TwitchCampaignScopeKey ?? "available");
        SelectTag(TwitchConnectionQuality, Int(settings, "connection_quality", 1).ToString());
        TwitchTrayNotifications.IsChecked = Bool(settings, "tray_notifications", true);
        TwitchBadgesEmotes.IsChecked = Bool(settings, "enable_badges_emotes");
        TwitchAvailableDrops.IsChecked = Bool(settings, "available_drops_check");
        TwitchAutoClaim.IsChecked = Bool(settings, "auto_claim_drops", true);
        PopulateTwitchGames(state, settings);
    }

    private void PopulateSoopMissions(JsonElement state, string selectedMission)
    {
        SoopPriorityMission.Items.Clear();
        if (!state.TryGetProperty("tasks", out var tasks) || tasks.ValueKind != JsonValueKind.Array || tasks.GetArrayLength() == 0)
        {
            SoopPriorityMission.Items.Add(new ComboBoxItem { Content = "暂无可选任务", Tag = "auto", IsEnabled = false });
            SoopPriorityMission.SelectedIndex = 0;
            SoopPriorityMission.IsEnabled = false;
            return;
        }

        SoopPriorityMission.IsEnabled = true;
        SoopPriorityMission.Items.Add(new ComboBoxItem { Content = "自动选择", Tag = "auto" });
        foreach (var task in tasks.EnumerateArray())
        {
            var id = Text(task, "id");
            if (!string.IsNullOrWhiteSpace(id))
                SoopPriorityMission.Items.Add(new ComboBoxItem { Content = Text(task, "title", id), Tag = id });
        }
        SelectTag(SoopPriorityMission, selectedMission);
    }

    private void PopulateTwitchLanguages(JsonElement state, string selectedLanguage)
    {
        TwitchLanguage.Items.Clear();
        if (state.TryGetProperty("languages", out var languages) && languages.ValueKind == JsonValueKind.Array)
        {
            foreach (var language in languages.EnumerateArray())
            {
                var value = Text(language, "value");
                if (!string.IsNullOrWhiteSpace(value))
                    TwitchLanguage.Items.Add(new ComboBoxItem { Content = Text(language, "display", value), Tag = value });
            }
        }
        if (TwitchLanguage.Items.Count == 0)
            TwitchLanguage.Items.Add(new ComboBoxItem { Content = "简体中文", Tag = "简体中文" });
        SelectTag(TwitchLanguage, selectedLanguage);
    }

    private void PopulateTwitchGames(JsonElement state, JsonElement settings)
    {
        if (_vm == null) return;
        ReplaceStrings(_vm.TwitchPriorityGames, ArrayStrings(settings, "priority"));
        ReplaceStrings(_vm.TwitchExcludedGames, ArrayStrings(settings, "exclude"));
        var available = ArrayStrings(state, "availableGames").Concat(ArrayStrings(state, "games"))
            .Concat(_vm.TwitchPriorityGames).Concat(_vm.TwitchExcludedGames);
        ReplaceStrings(_vm.TwitchAvailableGames, available.OrderBy(value => value, StringComparer.CurrentCultureIgnoreCase));
        RefreshTwitchGameChoices();
    }

    private async void OnPlatformChanged(object sender, RoutedEventArgs e)
    {
        if (!_initialized || sender is not RadioButton { Tag: string tag } || !Enum.TryParse(tag, out DropsPlatform platform)) return;
        _platform = platform;
        _vm?.SelectPlatform(platform);
        await EnsureLogTailStartedAsync();
        if (_logTail is not null) await _logTail.RefreshAsync(platform);
        RenderLogBuffer();
        await LoadPlatformAsync(platform);
    }

    private async void OnOpenPlatform(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: string tag } || !Enum.TryParse(tag, out DropsPlatform platform)) return;
        if (platform == DropsPlatform.Soop) SoopTab.IsChecked = true;
        else if (platform == DropsPlatform.YouTube) YouTubeTab.IsChecked = true;
        else if (platform == DropsPlatform.Twitch) TwitchTab.IsChecked = true;
        else BilibiliTab.IsChecked = true;
        if (_platform == platform) await LoadPlatformAsync(platform);
    }

    private async void OnStart(object sender, RoutedEventArgs e)
    {
        if (_vm == null || sender is not Button { Tag: string tag } || !Enum.TryParse(tag, out DropsPlatform platform)) return;
        await StartPlatformAsync(platform);
    }

    private async Task StartPlatformAsync(DropsPlatform platform, bool automatic = false)
    {
        if (_vm == null) return;
        if (platform == DropsPlatform.Twitch) _vm.BeginTwitchStart(automatic: false);
        try
        {
            if (platform == DropsPlatform.Bilibili)
            {
                // An explicit Start action is an opt-in for this provider,
                // so keep the persisted Enabled flag consistent with the UI.
                BilibiliEnabledCheck.IsChecked = true;
                _vm.BilibiliDetails.Enabled = true;
                if (!await SaveBilibiliSettingsAsync(showError: true)) return;
                if (!await EnsureBilibiliCredentialsAsync(showError: true)) return;
                _vm.BeginBilibiliStart(automatic);
            }
            var state = await _vm.StartAsync(platform);
            _vm.ApplyState(platform, state);
            if (platform == _platform) PopulateSettings(platform, state); else await LoadPlatformAsync(_platform);
        }
        catch (Exception ex)
        {
            if (platform == DropsPlatform.Twitch) _vm.SetTwitchTemporaryNetworkFailure(ex.Message);
            else ShowError(ex, $"启动 {PlatformName(platform)} 失败");
        }
    }

    private async void OnStop(object sender, RoutedEventArgs e)
    {
        if (_vm == null || sender is not Button { Tag: string tag } || !Enum.TryParse(tag, out DropsPlatform platform)) return;
        await StopPlatformAsync(platform);
    }

    private async Task StopPlatformAsync(DropsPlatform platform)
    {
        if (_vm == null) return;
        if (platform == DropsPlatform.Soop) _vm.StopSoopByUser();
        if (platform == DropsPlatform.Twitch) _vm.StopTwitchByUser();
        if (platform == DropsPlatform.Bilibili) _vm.StopBilibiliByUser();
        try
        {
            var state = await _vm.StopAsync(platform);
            _vm.ApplyState(platform, state);
            if (platform == _platform) PopulateSettings(platform, state); else await LoadPlatformAsync(_platform);
        }
        catch (Exception ex) { ShowError(ex, $"停止 {PlatformName(platform)} 失败"); }
    }

    private async void OnRefresh(object sender, RoutedEventArgs e)
    {
        if (_platform == DropsPlatform.Soop) await RefreshSoopAsync();
        else if (_platform == DropsPlatform.Twitch) await RefreshTwitchAsync();
        else if (_platform == DropsPlatform.Bilibili) await RefreshBilibiliAsync();
        else await RefreshPlatformAsync(_platform);
    }

    private async void OnSoopRefresh(object sender, RoutedEventArgs e) => await RefreshSoopAsync();

    private async Task RefreshSoopAsync(bool showError = true)
    {
        if (_vm == null || _vm.IsSoopRefreshing || _loading) return;
        _vm.BeginSoopRefresh();
        _loading = true;
        try
        {
            var state = await _vm.RequestAsync(DropsPlatform.Soop, "refresh");
            if (!Bool(state, "refreshCompleted"))
                throw new InvalidOperationException("SOOP 刷新未返回完成状态。");
            _vm.ApplyState(DropsPlatform.Soop, state);
            PopulateSettings(DropsPlatform.Soop, state);
        }
        catch (Exception ex)
        {
            _vm.FailSoopRefresh();
            _vm.SetSoopFailure(ex.Message);
            if (showError) ShowError(ex, "刷新 SOOP 掉宝信息失败");
        }
        finally { _loading = false; }
    }

    private void OnOpenProxySettings(object sender, RoutedEventArgs e)
    {
        if (Window.GetWindow(this) is MainWindow mainWindow)
            mainWindow.OpenProxySettings();
    }

    private void OnToggleHelp(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: string tag }) return;
        var target = tag switch
        {
            "YouTube" => YouTubeHelp,
            "Twitch" => TwitchHelp,
            _ => SoopHelp,
        };
        target.Visibility = target.Visibility == Visibility.Visible ? Visibility.Collapsed : Visibility.Visible;
    }

    private async void OnQuickStartAction(object sender, RoutedEventArgs e)
    {
        if (_vm == null || sender is not Button { Tag: string action }) return;
        switch (action)
        {
            case "soop_add_account":
                SoopAccountsCard.BringIntoView();
                SoopUserId.Focus();
                break;
            case "soop_settings":
                SoopSettingsCard.BringIntoView();
                SoopChannelMode.Focus();
                break;
            case "soop_refresh":
                await RefreshSoopAsync();
                break;
            case "soop_start":
                if (SoopAccountsList.SelectedItem == null && _vm.Accounts.Count > 0)
                    SoopAccountsList.SelectedIndex = 0;
                await RunSoopAccountCommandAsync("start_account", "启动");
                break;
            case "youtube_browser":
                YouTubeBrowserCard.BringIntoView();
                YouTubeBrowser.Focus();
                break;
            case "youtube_account":
                YouTubeAccountsCard.BringIntoView();
                if (_vm.Accounts.Count == 0)
                    YouTubeProfileName.Focus();
                else
                {
                    if (YouTubeProfilesList.SelectedItem == null) YouTubeProfilesList.SelectedIndex = 0;
                    await OpenYouTubeLoginAsync();
                }
                break;
            case "youtube_channel":
                YouTubeChannelsCard.BringIntoView();
                YouTubeChannelName.Focus();
                break;
            case "youtube_start":
                await StartPlatformAsync(DropsPlatform.YouTube);
                break;
            case "twitch_login":
                await LoginTwitchAsync();
                break;
            case "twitch_settings":
                TwitchSettingsCard.BringIntoView();
                TwitchPriorityMode.Focus();
                break;
            case "twitch_start":
                await StartPlatformAsync(DropsPlatform.Twitch);
                break;
        }
    }

    private void OnSoopChannelModeChanged(object sender, SelectionChangedEventArgs e) => UpdateSoopChannelFields();

    private void UpdateSoopChannelFields()
    {
        if (SoopManualChannelPanel == null || SoopPreferredChannelPanel == null) return;
        var mode = SelectedTag(SoopChannelMode, "smart");
        SoopManualChannelPanel.Visibility = mode == "manual" ? Visibility.Visible : Visibility.Collapsed;
        SoopPreferredChannelPanel.Visibility = mode == "owesports" ? Visibility.Visible : Visibility.Collapsed;
    }

    private void OnSoopPasswordChanged(object sender, RoutedEventArgs e) =>
        SoopPasswordHint.Visibility = string.IsNullOrEmpty(SoopPassword.Password) ? Visibility.Visible : Visibility.Collapsed;

    private async void OnSaveSoop(object sender, RoutedEventArgs e)
    {
        if (_vm == null) return;
        try
        {
            await _vm.RequestAsync(DropsPlatform.Soop, "save_settings", new { settings = new Dictionary<string, object?>
            {
                ["channel_mode"] = SelectedTag(SoopChannelMode, "smart"),
                ["manual_input"] = SoopManualChannel.Text.Trim(),
                ["priority_mission_id"] = SelectedTag(SoopPriorityMission, "auto"),
                ["preferred_bjid"] = string.IsNullOrWhiteSpace(SoopPreferredChannel.Text) ? "owesports" : SoopPreferredChannel.Text.Trim(),
                ["auto_claim_enabled"] = SoopAutoClaim.IsChecked == true,
                ["low_bandwidth_mode"] = SoopLowBandwidth.IsChecked == true,
                ["hang_without_missions"] = SoopHangWithoutMissions.IsChecked == true,
                ["mission_poll_interval"] = ParseInt(SoopMissionInterval, 90),
                ["inventory_poll_interval"] = ParseInt(SoopInventoryInterval, 300),
                ["channel_refresh_interval"] = ParseInt(SoopChannelInterval, 300),
            }});
            await LoadPlatformAsync(DropsPlatform.Soop);
        }
        catch (Exception ex) { ShowError(ex, "保存 SOOP 设置失败"); }
    }

    private async void OnSoopAddAccount(object sender, RoutedEventArgs e)
    {
        if (_vm == null) return;
        try
        {
            await _vm.RequestAsync(DropsPlatform.Soop, "add_account", new { userid = SoopUserId.Text.Trim(), password = SoopPassword.Password });
            SoopPassword.Clear();
            await LoadPlatformAsync(DropsPlatform.Soop);
        }
        catch (Exception ex) { SoopPassword.Clear(); ShowError(ex, "添加 SOOP 账号失败"); }
    }

    private async void OnSoopStartAccount(object sender, RoutedEventArgs e) => await RunSoopAccountCommandAsync("start_account", "启动");
    private async void OnSoopStopAccount(object sender, RoutedEventArgs e) => await RunSoopAccountCommandAsync("stop_account", "停止");

    private async void OnSoopSetPrimaryAccount(object sender, RoutedEventArgs e)
    {
        if (_vm == null) return;
        if (SoopAccountsList.SelectedItem is not DropsRow row)
        {
            ShowInfo("请先选择一个 SOOP 账号。", "设置主账号");
            return;
        }
        try
        {
            await _vm.RequestAsync(DropsPlatform.Soop, "set_primary_account", new { userid = row.Id });
            await LoadPlatformAsync(DropsPlatform.Soop);
        }
        catch (Exception ex) { ShowError(ex, "设置 SOOP 主账号失败"); }
    }

    private async Task RunSoopAccountCommandAsync(string command, string action)
    {
        if (_vm == null) return;
        if (SoopAccountsList.SelectedItem is not DropsRow row) { ShowInfo("请先选择一个 SOOP 账号。", $"{action}账号"); return; }
        if (command == "start_account") _vm.BeginSoopStart(row.Id, automatic: false);
        if (command == "stop_account") _vm.StopSoopByUser();
        try
        {
            await _vm.RequestAsync(DropsPlatform.Soop, command, new { userid = row.Id });
            await LoadPlatformAsync(DropsPlatform.Soop);
        }
        catch (Exception ex)
        {
            if (command == "start_account") _vm.SetSoopFailure(ex.Message);
            ShowError(ex, $"{action} SOOP 账号失败");
        }
    }

    private async void OnSoopDeleteAccount(object sender, RoutedEventArgs e)
    {
        if (_vm == null) return;
        if (SoopAccountsList.SelectedItem is not DropsRow row) { ShowInfo("请先选择一个 SOOP 账号。", "删除账号"); return; }
        if (MessageBox.Show($"删除 SOOP 账号「{row.Primary}」的本地登录信息？", "删除账号", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes) return;
        _vm.LogoutSoopByUser(row.Id);
        try { await _vm.RequestAsync(DropsPlatform.Soop, "delete_account", new { userid = row.Id }); await LoadPlatformAsync(DropsPlatform.Soop); }
        catch (Exception ex) { ShowError(ex, "删除 SOOP 账号失败"); }
    }

    private async void OnClaimSoopReward(object sender, RoutedEventArgs e)
    {
        if (_vm == null) return;
        if (SoopInventoryList.SelectedItem is not DropsRow row)
        {
            ShowInfo("请先选择一项要领取的奖励。", "领取奖励");
            return;
        }
        if (Bool(row.Payload, "claimed"))
        {
            ShowInfo("该奖励已经领取。", "领取奖励");
            return;
        }
        var uid = Text(row.Payload, "uid");
        if (string.IsNullOrWhiteSpace(uid))
        {
            ShowInfo("无法确定该奖励所属的 SOOP 账号，请刷新背包后重试。", "领取奖励");
            return;
        }
        if (MessageBox.Show($"确认领取奖励「{row.Primary}」？", "领取奖励",
                MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes)
            return;

        var button = sender as Button;
        if (button != null) button.IsEnabled = false;
        try
        {
            var result = await _vm.RequestAsync(
                DropsPlatform.Soop, "claim_reward", new { userid = uid, id = row.Id });
            var message = Text(result, "status") switch
            {
                "claimed" => "领取已确认。现在可以复制兑换码。",
                "already_claimed" => "该奖励已经领取。",
                "not_claimable" => "该奖励尚未达到领取条件。",
                "unconfirmed" => "领取结果无法确认，请前往 SOOP 官方背包检查。",
                _ => "领取失败，请稍后重试或前往 SOOP 官方背包检查。",
            };
            ShowInfo(message, "领取奖励");
            await LoadPlatformAsync(DropsPlatform.Soop);
        }
        catch (Exception ex)
        {
            ShowError(ex, "领取 SOOP 奖励失败");
        }
        finally
        {
            if (button != null) button.IsEnabled = true;
        }
    }

    private async void OnCopySoopCode(object sender, RoutedEventArgs e)
    {
        if (SoopInventoryList.SelectedItem is not DropsRow row)
        {
            ShowInfo("请先选择一项包含兑换码的奖励。", "复制兑换码");
            return;
        }

        // 与“复制日志”使用同一套剪贴板写入方式。
        // 兑换码已经包含在当前奖励行的 Payload 中，不再额外请求 Worker 执行复制相关命令。
        var code = Text(row.Payload, "redeemCode");
        if (string.IsNullOrWhiteSpace(code))
        {
            ShowInfo(Bool(row.Payload, "claimed")
                ? "该奖励没有可复制的兑换码。"
                : "请先领取该奖励，再复制兑换码。", "复制兑换码");
            return;
        }

        try
        {
            if (!await ClipboardService.CopyTextAsync(code))
                ShowInfo("剪贴板暂时被其它程序占用，请稍后重试。", "复制兑换码");
        }
        catch
        {
            ShowInfo("复制失败，请稍后重试。", "复制兑换码");
        }
    }

    private void OnOpenSoopInventory(object sender, RoutedEventArgs e)
    {
        try
        {
            Process.Start(new ProcessStartInfo { FileName = SoopInventoryUrl, UseShellExecute = true });
        }
        catch
        {
            ShowInfo("无法打开 SOOP 奖励背包，请稍后重试。", "打开背包");
        }
    }

    private void OnYouTubeModeChanged(object sender, SelectionChangedEventArgs e) => UpdateYouTubeModeFields();

    private void UpdateYouTubeModeFields()
    {
        if (YouTubeManualUrlPanel == null) return;
        YouTubeManualUrlPanel.Visibility = SelectedTag(YouTubeMode, "auto") == "manual" ? Visibility.Visible : Visibility.Collapsed;
    }

    private async void OnYouTubeBrowserChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_initialized || _vm == null || YouTubeBrowserPath == null) return;
        try
        {
            var result = await _vm.RequestAsync(DropsPlatform.YouTube, "detect_browser", new { browser = SelectedTag(YouTubeBrowser, "chrome") });
            YouTubeBrowserPath.Text = Text(result, "path");
        }
        catch { YouTubeBrowserPath.Clear(); }
    }

    private void OnBrowseYouTubeBrowser(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog { Title = "选择浏览器程序", Filter = "浏览器程序 (*.exe)|*.exe|所有文件 (*.*)|*.*", CheckFileExists = true };
        if (dialog.ShowDialog() == true) YouTubeBrowserPath.Text = dialog.FileName;
    }

    private async void OnSaveYouTube(object sender, RoutedEventArgs e)
    {
        if (_vm == null) return;
        try
        {
            await _vm.RequestAsync(DropsPlatform.YouTube, "save_settings", new { settings = new Dictionary<string, object?>
            {
                ["browser"] = SelectedTag(YouTubeBrowser, "chrome"), ["browser_path"] = YouTubeBrowserPath.Text.Trim(),
                ["headless"] = YouTubeHeadless.IsChecked == true, ["mute"] = YouTubeMute.IsChecked == true,
                ["mode"] = SelectedTag(YouTubeMode, "auto"), ["manual_url"] = YouTubeManualUrl.Text.Trim(),
                ["check_interval"] = ParseInt(YouTubeInterval, 300),
            }});
            await LoadPlatformAsync(DropsPlatform.YouTube);
        }
        catch (Exception ex) { ShowError(ex, "保存 YouTube 设置失败"); }
    }

    private async void OnYouTubeAddProfile(object sender, RoutedEventArgs e)
    {
        if (_vm == null) return;
        try { await _vm.RequestAsync(DropsPlatform.YouTube, "add_profile", new { name = YouTubeProfileName.Text.Trim() }); YouTubeProfileName.Clear(); await LoadPlatformAsync(DropsPlatform.YouTube); }
        catch (Exception ex) { ShowError(ex, "添加观看账号失败"); }
    }

    private async void OnYouTubeLogin(object sender, RoutedEventArgs e)
        => await OpenYouTubeLoginAsync();

    private async Task OpenYouTubeLoginAsync()
    {
        if (_vm == null) return;
        var profile = (YouTubeProfilesList.SelectedItem as DropsRow)?.Id ?? YouTubeProfileName.Text.Trim();
        if (string.IsNullOrWhiteSpace(profile)) { ShowInfo("请先选择观看账号，或填写账号名称。", "打开登录窗口"); return; }
        try { await _vm.RequestAsync(DropsPlatform.YouTube, "open_login", new { profile }); await LoadPlatformAsync(DropsPlatform.YouTube); }
        catch (Exception ex) { ShowError(ex, "打开登录窗口失败"); }
    }

    private async void OnYouTubeDeleteProfile(object sender, RoutedEventArgs e)
    {
        if (_vm == null) return;
        if (YouTubeProfilesList.SelectedItem is not DropsRow row) { ShowInfo("请先选择一个观看账号。", "删除账号"); return; }
        var result = MessageBox.Show("是否同时删除该观看账号的浏览器登录数据？\n\n选择“否”只从列表移除，浏览器资料目录仍保留。", "删除观看账号", MessageBoxButton.YesNoCancel, MessageBoxImage.Warning);
        if (result == MessageBoxResult.Cancel) return;
        try { await _vm.RequestAsync(DropsPlatform.YouTube, "delete_profile", new { name = row.Id, deleteData = result == MessageBoxResult.Yes }); await LoadPlatformAsync(DropsPlatform.YouTube); }
        catch (Exception ex) { ShowError(ex, "删除观看账号失败"); }
    }

    private async void OnYouTubeAddChannel(object sender, RoutedEventArgs e)
    {
        if (_vm == null) return;
        try
        {
            await _vm.RequestAsync(DropsPlatform.YouTube, "add_channel", new { channel = new
            {
                name = YouTubeChannelName.Text.Trim(), id = YouTubeChannelId.Text.Trim(),
                url = YouTubeChannelUrl.Text.Trim(), enabled = true,
            }});
            YouTubeChannelName.Clear(); YouTubeChannelId.Clear(); YouTubeChannelUrl.Clear();
            await LoadPlatformAsync(DropsPlatform.YouTube);
        }
        catch (Exception ex) { ShowError(ex, "添加频道失败"); }
    }

    private async void OnYouTubeDeleteChannel(object sender, RoutedEventArgs e)
    {
        if (_vm == null) return;
        if (YouTubeChannelsList.SelectedIndex < 0) { ShowInfo("请先选择一个频道。", "删除频道"); return; }
        try { await _vm.RequestAsync(DropsPlatform.YouTube, "delete_channel", new { index = YouTubeChannelsList.SelectedIndex }); await LoadPlatformAsync(DropsPlatform.YouTube); }
        catch (Exception ex) { ShowError(ex, "删除频道失败"); }
    }

    private async void OnYouTubeToggleChannel(object sender, RoutedEventArgs e)
    {
        if (_vm == null) return;
        if (YouTubeChannelsList.SelectedItem is not DropsRow row || YouTubeChannelsList.SelectedIndex < 0) { ShowInfo("请先选择一个频道。", "启用或停用频道"); return; }
        try
        {
            var channel = row.Payload;
            await _vm.RequestAsync(DropsPlatform.YouTube, "update_channel", new
            {
                index = YouTubeChannelsList.SelectedIndex,
                channel = new { name = Text(channel, "name"), id = Text(channel, "id"), url = Text(channel, "url"), enabled = !Bool(channel, "enabled", true) },
            });
            await LoadPlatformAsync(DropsPlatform.YouTube);
        }
        catch (Exception ex) { ShowError(ex, "更新频道状态失败"); }
    }

    private async void OnTwitchLogin(object sender, RoutedEventArgs e)
        => await LoginTwitchAsync();

    private async Task LoginTwitchAsync()
    {
        if (_vm == null || !_vm.CanTwitchLogin) return;
        if (!string.IsNullOrWhiteSpace(_vm.TwitchAuthorizationUrl))
        {
            OpenTwitchAuthorizationUrl(_vm.TwitchAuthorizationUrl);
            return;
        }
        _vm.BeginTwitchLogin();
        try { await _vm.RequestAsync(DropsPlatform.Twitch, "login"); await LoadPlatformAsync(DropsPlatform.Twitch); }
        catch (Exception ex) { _vm.SetTwitchTemporaryNetworkFailure(ex.Message); }
    }

    private void OnOpenTwitchAuthorization(object sender, RoutedEventArgs e)
    {
        if (_vm == null || string.IsNullOrWhiteSpace(_vm.TwitchAuthorizationUrl)) return;
        OpenTwitchAuthorizationUrl(_vm.TwitchAuthorizationUrl);
    }

    private void OpenTwitchAuthorizationUrl(string url)
    {
        try { Process.Start(new ProcessStartInfo { FileName = url, UseShellExecute = true }); }
        catch { _ = CopyLoginUrlAsync(url); }
    }

    private async void OnCopyTwitchAuthorizationCode(object sender, RoutedEventArgs e)
    {
        if (_vm == null || string.IsNullOrWhiteSpace(_vm.TwitchAuthorizationCode)) return;
        if (!await ClipboardService.CopyTextAsync(_vm.TwitchAuthorizationCode))
            ShowInfo("复制失败，请稍后重试。", "Twitch 授权");
    }

    private void OnAutoStartChanged(object sender, RoutedEventArgs e)
    {
        if (!_initialized || _main == null) return;
        _main.Settings.AutoStartSoop = SoopAutoStart.IsChecked == true;
        _main.Settings.AutoStartTwitch = TwitchAutoStart.IsChecked == true;
        _main.Settings.AutoStartBilibili = BilibiliAutoStart.IsChecked == true;
        _main.Settings.AutoResumeBilibiliDrops = BilibiliAutoResume.IsChecked == true;
        if (_vm is not null)
        {
            _vm.BilibiliDetails.AutoRestore = _main.Settings.AutoStartBilibili;
            _vm.BilibiliDetails.AutoResume = _main.Settings.AutoResumeBilibiliDrops;
        }
        _vm?.SetSoopAutoStartEnabled(_main.Settings.AutoStartSoop);
        _vm?.SetTwitchAutoStartEnabled(_main.Settings.AutoStartTwitch);
        _main.Settings.Save();
    }

    private async void OnTwitchLogout(object sender, RoutedEventArgs e)
    {
        if (_vm == null || !_vm.CanClearTwitchLogin ||
            MessageBox.Show("清除 Twitch 本地登录信息并停止当前登录/连接尝试？\n\n优先游戏、排除游戏、自动启动、代理和其它界面设置都会保留。",
                "清除 Twitch 登录信息", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes) return;
        _vm.BeginClearTwitchLogin();
        try
        {
            var state = await _vm.ClearTwitchAuthenticationAsync();
            _vm.CompleteClearTwitchLogin(state);
            PopulateSettings(DropsPlatform.Twitch, state);
        }
        catch (Exception ex)
        {
            _vm.FailClearTwitchLogin(ex.Message);
            ShowError(ex, "清除 Twitch 登录信息失败");
        }
    }

    private async void OnTwitchRetry(object sender, RoutedEventArgs e)
    {
        if (_vm == null || _vm.IsTwitchLoginInProgress || _vm.IsClearingTwitchLogin) return;
        try
        {
            await _vm.RequestAsync(DropsPlatform.Twitch, "ssl_check");
            if (_vm.IsTwitchLoggedIn)
            {
                _vm.BeginTwitchStart(automatic: false);
                var state = await _vm.RequestAsync(DropsPlatform.Twitch, "start", new { automatic = false });
                _vm.ApplyState(DropsPlatform.Twitch, state);
            }
            else
            {
                await LoginTwitchAsync();
            }
        }
        catch (Exception ex) { _vm.SetTwitchFailure(ex.Message); }
    }

    private void OnSoopRetryNow(object sender, RoutedEventArgs e) => _vm?.RetrySoopNow();

    private void OnTwitchRetryNow(object sender, RoutedEventArgs e) => _vm?.RetryTwitchNow();

    private async void OnRestartWorker(object sender, RoutedEventArgs e)
    {
        if (_vm == null || sender is not Button { Tag: DropsPlatform platform }) return;
        try
        {
            await _vm.RestartWorkerAsync(platform);
            await LoadPlatformAsync(platform);
        }
        catch (Exception ex)
        {
            ShowError(ex, $"重启 {PlatformName(platform)} Worker 失败");
        }
    }

    private async void OnTwitchReload(object sender, RoutedEventArgs e)
        => await RefreshTwitchAsync();

    private async Task RefreshTwitchAsync()
    {
        if (_vm == null || _vm.IsTwitchRefreshing) return;
        _vm.BeginTwitchRefresh();
        try
        {
            var state = await _vm.RequestAsync(DropsPlatform.Twitch, "refresh");
            _vm.ApplyState(DropsPlatform.Twitch, state);
            PopulateSettings(DropsPlatform.Twitch, state);
            _vm.CompleteTwitchRefresh(DateTimeOffset.Now);
        }
        catch (Exception ex)
        {
            _vm.FailTwitchRefresh();
            ShowError(ex, "刷新 Twitch 掉宝失败");
        }
        finally
        {
            if (_vm.IsTwitchRefreshing) _vm.FailTwitchRefresh();
        }
    }

    private async Task RefreshBilibiliAsync(bool showError = true)
    {
        if (_vm == null || _loading) return;
        _loading = true;
        try
        {
            var state = await _vm.RequestAsync(DropsPlatform.Bilibili, "refresh", new { discover = true });
            _vm.ApplyState(DropsPlatform.Bilibili, state);
            PopulateSettings(DropsPlatform.Bilibili, state);
            SyncBilibiliSettingsToApp();
        }
        catch (Exception ex)
        {
            if (showError) ShowError(ex, "刷新哔哩哔哩掉宝失败");
        }
        finally { _loading = false; }
    }

    private async Task DiscoverBilibiliAsync()
    {
        if (_vm == null || _loading) return;
        _loading = true;
        try
        {
            var result = await _vm.RequestAsync(DropsPlatform.Bilibili, "discover");
            ApplyBilibiliResult(result);
            SyncBilibiliSettingsToApp();
            if (!string.IsNullOrWhiteSpace(Text(result, "message")))
                ShowInfo(Text(result, "message"), "哔哩哔哩活动发现");
        }
        catch (Exception ex) { ShowError(ex, "发现哔哩哔哩活动失败"); }
        finally { _loading = false; }
    }

    private async Task StartBilibiliQrLoginAsync()
    {
        if (_vm == null || _bilibiliQrPollingTask is { IsCompleted: false }) return;
        try
        {
            var result = await _vm.RequestAsync(DropsPlatform.Bilibili, "qr_generate");
            ApplyBilibiliResult(result);
            _bilibiliQrPollingCts?.Cancel();
            _bilibiliQrPollingCts?.Dispose();
            var cts = new CancellationTokenSource();
            _bilibiliQrPollingCts = cts;
            _bilibiliQrPollingTask = PollBilibiliQrAsync(cts);
            await _bilibiliQrPollingTask;
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            _vm.BilibiliDetails.SetQrError("登录失败：" + SensitiveDataRedactor.Redact(ex.Message));
            ShowError(ex, "哔哩哔哩扫码登录失败");
        }
        finally
        {
            if (_bilibiliQrPollingCts?.IsCancellationRequested == true)
            {
                _bilibiliQrPollingCts.Dispose();
                _bilibiliQrPollingCts = null;
            }
            _bilibiliQrPollingTask = null;
        }
    }

    private async Task PollBilibiliQrAsync(CancellationTokenSource owner)
    {
        if (_vm == null) return;
        var token = owner.Token;
        while (!token.IsCancellationRequested)
        {
            await Task.Delay(TimeSpan.FromSeconds(2), token);
            var result = await _vm.RequestAsync(DropsPlatform.Bilibili, "qr_poll", token: token);
            ApplyBilibiliResult(result);
            var state = Text(result, "state");
            if (state is "success" or "expired" or "cancelled")
            {
                if (state == "success")
                {
                    ShowInfo("哔哩哔哩扫码登录成功，账号凭据已安全保存。", "哔哩哔哩登录");
                    await LoadPlatformAsync(DropsPlatform.Bilibili);
                }
                return;
            }
        }
    }

    private async Task CancelBilibiliQrAsync()
    {
        _bilibiliQrPollingCts?.Cancel();
        if (_vm == null) return;
        try
        {
            var result = await _vm.RequestAsync(DropsPlatform.Bilibili, "qr_cancel");
            ApplyBilibiliResult(result);
        }
        catch (Exception ex) { ShowError(ex, "取消哔哩哔哩扫码失败"); }
    }

    private async Task ImportBilibiliCookieAsync()
    {
        if (_vm == null) return;
        var cookie = BilibiliCookieBox.Password.Trim();
        if (string.IsNullOrWhiteSpace(cookie))
        {
            ShowInfo("请粘贴包含 SESSDATA、DedeUserID 与 bili_jct 的 Cookie。", "导入 Cookie");
            return;
        }
        try
        {
            var result = await _vm.RequestAsync(DropsPlatform.Bilibili, "set_credentials", new { cookie });
            ApplyBilibiliResult(result);
            SyncBilibiliSettingsToApp();
            ShowInfo("Cookie 已验证并使用 Windows CurrentUser DPAPI 加密保存。", "哔哩哔哩登录");
        }
        catch (Exception ex) { ShowError(ex, "导入哔哩哔哩 Cookie 失败"); }
        finally
        {
            cookie = "";
            BilibiliCookieBox.Clear();
        }
    }

    private async Task LogoutBilibiliAsync()
    {
        if (_vm == null || MessageBox.Show("退出哔哩哔哩登录并删除本机加密凭据？\n\n直播间和其它设置会保留。", "退出哔哩哔哩登录",
                MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes) return;
        _bilibiliQrPollingCts?.Cancel();
        _vm.StopBilibiliByUser();
        try
        {
            var state = await _vm.RequestAsync(DropsPlatform.Bilibili, "logout");
            ApplyBilibiliResult(state);
            if (_main is not null)
            {
                _main.Settings.BilibiliCredentialBlob = "";
                _main.Settings.BilibiliUserName = "";
                _main.Settings.BilibiliUid = 0;
                _main.Settings.Save();
            }
        }
        catch (Exception ex) { ShowError(ex, "退出哔哩哔哩登录失败"); }
    }

    private async Task AddBilibiliRoomAsync()
    {
        if (_vm == null) return;
        var details = _vm.BilibiliDetails;
        var reference = details.RoomReference.Trim();
        if (string.IsNullOrWhiteSpace(reference)) { ShowInfo("请输入直播间 URL 或 Room ID。", "添加直播间"); return; }
        try
        {
            var result = await _vm.RequestAsync(DropsPlatform.Bilibili, "add_room", new
            {
                roomId = reference, name = details.RoomName.Trim(), enabled = true,
            });
            ApplyBilibiliResult(result);
            SyncBilibiliSettingsToApp();
            details.RoomReference = "";
            details.RoomName = "";
        }
        catch (Exception ex) { ShowError(ex, "添加哔哩哔哩直播间失败"); }
    }

    private async Task RemoveBilibiliRoomAsync(object? parameter)
    {
        if (_vm == null || parameter is not BilibiliRoomViewModel room) return;
        try
        {
            var result = await _vm.RequestAsync(DropsPlatform.Bilibili, "remove_room", new { roomId = room.Id });
            ApplyBilibiliResult(result);
            SyncBilibiliSettingsToApp();
        }
        catch (Exception ex) { ShowError(ex, "删除哔哩哔哩直播间失败"); }
    }

    private async Task SetBilibiliRoomEnabledAsync(object? parameter)
    {
        if (_vm == null || parameter is not BilibiliRoomViewModel room) return;
        try
        {
            var result = await _vm.RequestAsync(DropsPlatform.Bilibili, "set_room_enabled", new { roomId = room.Id, enabled = room.Enabled });
            ApplyBilibiliResult(result);
            SyncBilibiliSettingsToApp();
        }
        catch (Exception ex) { ShowError(ex, "更新哔哩哔哩直播间状态失败"); }
    }

    private async Task ClearBilibiliNotifierAsync()
    {
        if (_vm == null || MessageBox.Show("删除已保存的 Gotify / Server 酱通知配置？", "清除第三方通知",
                MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes) return;
        try
        {
            var result = await _vm.RequestAsync(DropsPlatform.Bilibili, "save_settings", new
            {
                settings = new Dictionary<string, object?> { ["notifyUrls"] = Array.Empty<string>() },
            });
            ApplyBilibiliResult(result);
            BilibiliNotifierUrlBox.Clear();
            ShowInfo("第三方通知配置已清除。", "哔哩哔哩通知");
        }
        catch (Exception ex) { ShowError(ex, "清除哔哩哔哩通知配置失败"); }
    }

    private async Task RefreshBilibiliSessionsAsync()
    {
        if (_vm == null) return;
        try
        {
            var result = await _vm.RequestAsync(DropsPlatform.Bilibili, "get_session_details");
            _vm.BilibiliDetails.HandleEvent("session", result);
        }
        catch (Exception ex) { ShowError(ex, "读取哔哩哔哩 Session 详情失败"); }
    }

    private async Task ClaimBilibiliRewardAsync(object? parameter)
    {
        if (_vm == null || parameter is not BilibiliTaskViewModel task || !task.CanClaim) return;
        task.IsClaiming = true;
        try
        {
            var result = await _vm.RequestAsync(DropsPlatform.Bilibili, "claim_reward", new { taskId = task.Id });
            ApplyBilibiliResult(result);
            var success = result.TryGetProperty("results", out var results) && results.ValueKind == JsonValueKind.Array &&
                          results.EnumerateArray().Any(item => Bool(item, "success"));
            ShowInfo(success ? "奖励领取成功。" : "奖励领取未成功，仍可稍后重新领取。", "哔哩哔哩奖励");
        }
        catch (Exception ex) { ShowError(ex, "领取哔哩哔哩奖励失败"); }
        finally { task.IsClaiming = false; }
    }

    private async Task<bool> EnsureBilibiliCredentialsAsync(bool showError)
    {
        if (_vm == null || _main == null) return false;
        var cookie = DpapiCredentialStore.Unprotect(_main.Settings.BilibiliCredentialBlob);
        if (string.IsNullOrWhiteSpace(cookie))
        {
            try
            {
                var state = await _vm.LoadAsync(DropsPlatform.Bilibili);
                ApplyBilibiliResult(state);
                if (Bool(state, "credentialAvailable")) return true;
            }
            catch { }
            if (!string.IsNullOrWhiteSpace(_main.Settings.BilibiliCredentialBlob))
            {
                _main.Settings.BilibiliCredentialBlob = "";
                _main.Settings.Save();
            }
            if (showError) ShowInfo("请先扫码登录哔哩哔哩，或在高级区域导入 Cookie。", "哔哩哔哩登录");
            return false;
        }
        try
        {
            var result = await _vm.RequestAsync(DropsPlatform.Bilibili, "set_credentials", new { cookie });
            ApplyBilibiliResult(result);
            return true;
        }
        catch (Exception ex)
        {
            if (showError) ShowError(ex, "恢复哔哩哔哩登录失败");
            return false;
        }
        finally { cookie = ""; }
    }

    private async Task<bool> SaveBilibiliSettingsAsync(bool showError)
    {
        if (_vm == null || _main == null) return false;
        var details = _vm.BilibiliDetails;
        var mode = details.WatchMode;
        if (!int.TryParse(details.SessionsPerRoomText.Trim(), out var sessions) || sessions <= 0 || sessions > 128)
        {
            if (showError) ShowInfo("每房间并发 Session 必须是 1 到 128 的正整数。", "保存哔哩哔哩设置");
            return false;
        }
        if (mode == "standard") sessions = 1;
        if (!int.TryParse(details.ReconnectDelayText.Trim(), out var reconnect) || reconnect <= 0)
        {
            if (showError) ShowInfo("重连延迟必须是正整数秒。", "保存哔哩哔哩设置");
            return false;
        }
        if (!int.TryParse(details.TaskIntervalText.Trim(), out var interval) || interval < 10)
        {
            if (showError) ShowInfo("官方任务查询间隔不能小于 10 秒。", "保存哔哩哔哩设置");
            return false;
        }
        details.SessionsPerRoom = sessions;
        details.ReconnectDelayText = reconnect.ToString();
        details.TaskIntervalText = interval.ToString();
        var taskIds = ParseTaskIds(details.TaskIdsText);
        var settings = new Dictionary<string, object?>
        {
            ["enabled"] = details.Enabled,
            ["autoRestore"] = details.AutoRestore,
            ["autoResumeDrops"] = details.AutoResume,
            ["watchMode"] = mode,
            ["sessionsPerRoom"] = sessions,
            ["reconnectDelay"] = reconnect,
            ["taskInterval"] = interval,
            ["autoTaskProgress"] = details.AutoTaskProgress,
            ["reconnectEnabled"] = details.ReconnectEnabled,
            ["autoDiscover"] = details.AutoDiscover,
            ["autoClaim"] = details.AutoClaim,
            ["taskNotifications"] = details.TaskNotifications,
            ["taskIds"] = taskIds,
        };
        var notifier = BilibiliNotifierUrlBox.Password.Trim();
        if (!string.IsNullOrWhiteSpace(notifier))
            settings["notifyUrls"] = notifier.Split(['\r', '\n', ',', ';', ' '], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        try
        {
            var result = await _vm.RequestAsync(DropsPlatform.Bilibili, "save_settings", new { settings });
            ApplyBilibiliResult(result);
            SyncBilibiliSettingsToApp(mode, sessions, reconnect, interval, taskIds);
            BilibiliNotifierUrlBox.Clear();
            return true;
        }
        catch (Exception ex)
        {
            if (showError) ShowError(ex, "保存哔哩哔哩设置失败");
            return false;
        }
    }

    private void ApplyBilibiliResult(JsonElement result)
    {
        if (_vm == null || result.ValueKind != JsonValueKind.Object) return;
        _vm.ApplyState(DropsPlatform.Bilibili, result);
        if (result.TryGetProperty("state", out var nested) && nested.ValueKind == JsonValueKind.Object)
            _vm.ApplyState(DropsPlatform.Bilibili, nested);
        if (result.TryGetProperty("account", out var account) && account.ValueKind == JsonValueKind.Object)
            _vm.BilibiliDetails.HandleEvent("account", account);
        if (result.TryGetProperty("results", out var rewards) && rewards.ValueKind == JsonValueKind.Array)
            foreach (var reward in rewards.EnumerateArray()) _vm.BilibiliDetails.HandleEvent("reward", reward);
        if (result.TryGetProperty("state", out var qrState) && qrState.ValueKind == JsonValueKind.String)
            _vm.BilibiliDetails.HandleEvent("qr_login", result);
        if (_main is not null && result.TryGetProperty("credentialBlob", out var blob) && blob.ValueKind == JsonValueKind.String)
        {
            var encrypted = blob.GetString() ?? "";
            if (!string.IsNullOrWhiteSpace(encrypted))
            {
                _main.Settings.BilibiliCredentialBlob = encrypted;
                if (result.TryGetProperty("account", out var savedAccount) && savedAccount.ValueKind == JsonValueKind.Object)
                {
                    _main.Settings.BilibiliUserName = Text(savedAccount, "userName");
                    _main.Settings.BilibiliUid = Long(savedAccount, "uid");
                }
                _main.Settings.Save();
            }
        }
    }

    private void SyncBilibiliSettingsToApp(string? mode = null, int? sessions = null,
        int? reconnect = null, int? interval = null, IReadOnlyList<string>? taskIds = null)
    {
        if (_main == null || _vm == null) return;
        var details = _vm.BilibiliDetails;
        _main.Settings.BilibiliEnabled = details.Enabled;
        _main.Settings.AutoStartBilibili = details.AutoRestore;
        _main.Settings.AutoResumeBilibiliDrops = details.AutoResume;
        _main.Settings.BilibiliRoomIds = details.Rooms.Select(room => room.Id).Distinct().ToList();
        _main.Settings.BilibiliTaskIds = (taskIds ?? ParseTaskIds(details.TaskIdsText)).ToList();
        _main.Settings.BilibiliWatchMode = mode ?? details.WatchMode;
        _main.Settings.BilibiliSessionsPerRoom = sessions ?? Math.Max(1, details.SessionsPerRoom);
        _main.Settings.BilibiliReconnectDelaySeconds = reconnect ?? Math.Max(1, details.ReconnectDelay);
        _main.Settings.BilibiliTaskIntervalSeconds = interval ?? Math.Max(10, details.TaskInterval);
        _main.Settings.BilibiliAutoClaim = details.AutoClaim;
        _main.Settings.BilibiliTaskNotifications = details.TaskNotifications;
        _main.Settings.Save();
    }

    private static IReadOnlyList<string> ParseTaskIds(string value) => value
        .Split(['\r', '\n', ',', ';', ' ', '\t'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
        .Where(item => !string.IsNullOrWhiteSpace(item)).Distinct(StringComparer.Ordinal).ToArray();

    private void OnTwitchCampaignScopeChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_initialized || _vm == null || TwitchCampaignScopePicker == null) return;
        _vm.SetTwitchCampaignScope(SelectedTag(TwitchCampaignScopePicker, "available"));
    }

    private void OnAddPriorityGame(object sender, RoutedEventArgs e) => AddTwitchGame(TwitchPriorityPicker, priority: true);
    private void OnAddExcludedGame(object sender, RoutedEventArgs e) => AddTwitchGame(TwitchExcludePicker, priority: false);

    private void AddTwitchGame(ComboBox picker, bool priority)
    {
        if (_vm == null) return;
        var text = picker.SelectedItem?.ToString() ?? picker.Text;
        var source = priority ? _vm.TwitchPriorityChoices : _vm.TwitchExcludeChoices;
        var game = source.FirstOrDefault(item => string.Equals(item, text, StringComparison.OrdinalIgnoreCase));
        if (game == null) { ShowInfo("请从当前掉宝活动发现的游戏中选择。", priority ? "添加优先游戏" : "添加排除游戏"); return; }
        var target = priority ? _vm.TwitchPriorityGames : _vm.TwitchExcludedGames;
        if (!target.Contains(game)) target.Add(game);
        picker.SelectedIndex = -1;
        picker.Text = "";
        RefreshTwitchGameChoices();
    }

    private void OnPriorityUp(object sender, RoutedEventArgs e) => MovePriority(sender, -1);
    private void OnPriorityDown(object sender, RoutedEventArgs e) => MovePriority(sender, 1);

    private void MovePriority(object sender, int offset)
    {
        if (_vm == null || sender is not Button { Tag: string game }) return;
        var index = _vm.TwitchPriorityGames.IndexOf(game);
        var next = index + offset;
        if (index >= 0 && next >= 0 && next < _vm.TwitchPriorityGames.Count)
            _vm.TwitchPriorityGames.Move(index, next);
    }

    private void OnRemovePriorityGame(object sender, RoutedEventArgs e)
    {
        if (_vm != null && sender is Button { Tag: string game })
        {
            _vm.TwitchPriorityGames.Remove(game);
            RefreshTwitchGameChoices();
        }
    }

    private void OnRemoveExcludedGame(object sender, RoutedEventArgs e)
    {
        if (_vm != null && sender is Button { Tag: string game })
        {
            _vm.TwitchExcludedGames.Remove(game);
            RefreshTwitchGameChoices();
        }
    }

    private async void OnSaveTwitch(object sender, RoutedEventArgs e)
    {
        if (_vm == null) return;
        try
        {
            await _vm.RequestAsync(DropsPlatform.Twitch, "save_settings", new { settings = new Dictionary<string, object?>
            {
                ["priority"] = _vm.TwitchPriorityGames.ToArray(), ["exclude"] = _vm.TwitchExcludedGames.ToArray(),
                ["priority_mode"] = SelectedTag(TwitchPriorityMode, "PRIORITY_ONLY"),
                ["connection_quality"] = int.Parse(SelectedTag(TwitchConnectionQuality, "1")),
                ["tray_notifications"] = TwitchTrayNotifications.IsChecked == true,
                ["enable_badges_emotes"] = TwitchBadgesEmotes.IsChecked == true,
                ["available_drops_check"] = TwitchAvailableDrops.IsChecked == true,
                ["auto_claim_drops"] = TwitchAutoClaim.IsChecked == true,
                ["language"] = SelectedTag(TwitchLanguage, "简体中文"),
            }});
            await LoadPlatformAsync(DropsPlatform.Twitch);
        }
        catch (Exception ex) { ShowError(ex, "保存 Twitch 设置失败"); }
    }

    private async void OnTwitchSelectChannel(object sender, RoutedEventArgs e)
    {
        if (_vm == null) return;
        if (TwitchChannelsList.SelectedItem is not DropsRow row) { ShowInfo("请先选择一个在线频道。", "切换频道"); return; }
        try { await _vm.RequestAsync(DropsPlatform.Twitch, "select_channel", new { id = row.Id }); await LoadPlatformAsync(DropsPlatform.Twitch); }
        catch (Exception ex) { ShowError(ex, "切换 Twitch 频道失败"); }
    }

    private void OnOpenTwitchInventory(object sender, RoutedEventArgs e)
    {
        try
        {
            Process.Start(new ProcessStartInfo { FileName = TwitchInventoryUrl, UseShellExecute = true });
        }
        catch
        {
            ShowInfo("无法打开 Twitch 奖励背包，请稍后重试。", "打开背包");
        }
    }

    private async void OnClaimTwitchDrop(object sender, RoutedEventArgs e)
    {
        if (_vm == null || sender is not Button { DataContext: DropsRow row }) return;
        if (!_twitchClaimsInProgress.Add(row.Id)) return;
        if (!row.TryBeginTwitchClaim())
        {
            _twitchClaimsInProgress.Remove(row.Id);
            return;
        }

        try
        {
            var result = await _vm.RequestAsync(
                DropsPlatform.Twitch, "claim_drop", new { id = row.Id });
            if (result.TryGetProperty("state", out var state) && state.ValueKind == JsonValueKind.Object)
            {
                _vm.ApplyState(DropsPlatform.Twitch, state);
                PopulateSettings(DropsPlatform.Twitch, state);
            }

            var status = Text(result, "status");
            if (status is not ("claimed" or "already_claimed"))
                ShowInfo("Twitch 奖励领取失败，请稍后重试。", "领取奖励");
        }
        catch
        {
            ShowInfo("Twitch 奖励领取失败，请稍后重试。", "领取奖励");
        }
        finally
        {
            row.EndTwitchClaim();
            _twitchClaimsInProgress.Remove(row.Id);
        }
    }

    private void RefreshTwitchGameChoices()
    {
        if (_vm == null) return;
        ReplaceStrings(_vm.TwitchPriorityChoices, _vm.TwitchAvailableGames
            .Where(game => !_vm.TwitchPriorityGames.Contains(game, StringComparer.OrdinalIgnoreCase)));
        ReplaceStrings(_vm.TwitchExcludeChoices, _vm.TwitchAvailableGames
            .Where(game => !_vm.TwitchExcludedGames.Contains(game, StringComparer.OrdinalIgnoreCase)));
        var hasGames = _vm.TwitchAvailableGames.Count > 0;
        TwitchPriorityPicker.IsEnabled = hasGames;
        TwitchExcludePicker.IsEnabled = hasGames;
        TwitchGamesEmptyHint.Visibility = hasGames ? Visibility.Collapsed : Visibility.Visible;
        const string hint = "登录 Twitch 并刷新掉宝活动后即可选择游戏。";
        if (hasGames)
        {
            if (string.Equals(TwitchPriorityPicker.Text, hint, StringComparison.Ordinal)) TwitchPriorityPicker.Text = "";
            if (string.Equals(TwitchExcludePicker.Text, hint, StringComparison.Ordinal)) TwitchExcludePicker.Text = "";
        }
        else
        {
            TwitchPriorityPicker.Text = hint;
            TwitchExcludePicker.Text = hint;
        }
    }

    private async Task EnsureLogTailStartedAsync()
    {
        if (_logTailStarted || _logTail is null) return;
        await _logStartGate.WaitAsync();
        try
        {
            if (_logTailStarted || _logTail is null) return;
            await _logTail.StartAsync();
            _logTailStarted = true;
        }
        finally { _logStartGate.Release(); }
    }

    private void OnLogTailChanged(object? sender, PlatformLogChunk chunk) => Dispatcher.BeginInvoke(new Action(() =>
    {
        if (chunk.Revision < _logVisibleRevisions[chunk.Platform]) return;
        _logVisibleRevisions[chunk.Platform] = chunk.Revision;
        var buffer = _logBuffers[chunk.Platform];
        if (chunk.Reset) buffer.Clear();
        buffer.Append(chunk.Text);
        if (chunk.Platform == _platform) AppendLogText(chunk.Text, chunk.Reset);
    }));

    private void RenderLogBuffer()
    {
        LogTextBox.Text = _logBuffers[_platform].ToString();
        LogTextBox.ScrollToEnd();
    }

    private void AppendLogText(string text, bool reset)
    {
        var scrollableHeight = Math.Max(0, LogTextBox.ExtentHeight - LogTextBox.ViewportHeight);
        var wasAtBottom = scrollableHeight <= 0 || LogTextBox.VerticalOffset >= scrollableHeight - 0.5;
        var previousVerticalOffset = LogTextBox.VerticalOffset;
        var hadSelection = LogTextBox.SelectionLength > 0;
        var selectionStart = LogTextBox.SelectionStart;
        var selectionLength = LogTextBox.SelectionLength;
        var hadFocus = LogTextBox.IsKeyboardFocusWithin;

        if (reset) LogTextBox.Text = text;
        else if (text.Length > 0) LogTextBox.AppendText(text);

        if (hadSelection)
            LogTextBox.Select(Math.Min(selectionStart, LogTextBox.Text.Length),
                Math.Min(selectionLength, Math.Max(0, LogTextBox.Text.Length - selectionStart)));
        if (wasAtBottom) LogTextBox.ScrollToEnd();
        else LogTextBox.ScrollToVerticalOffset(previousVerticalOffset);
        if (!hadFocus && LogTextBox.IsKeyboardFocusWithin) Keyboard.ClearFocus();
    }

    private async void OnCopyAllLogs(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrEmpty(LogTextBox.Text)) return;
        try
        {
            if (!await ClipboardService.CopyTextAsync(LogTextBox.Text))
                ShowInfo("剪贴板暂时被其它程序占用，请稍后重试。", "复制日志");
        }
        catch { ShowInfo("复制失败，请稍后重试。", "复制日志"); }
    }

    private async void OnClearLogDisplay(object sender, RoutedEventArgs e)
    {
        var platform = _platform;
        if (_logTail is not null)
        {
            var revision = await _logTail.ClearDisplayAsync(platform);
            if (revision >= 0) _logVisibleRevisions[platform] = revision;
        }
        _logBuffers[platform].Clear();
        if (_platform == platform) LogTextBox.Clear();
    }

    private void OnLogTextChanged(object sender, TextChangedEventArgs e)
    {
        if (LogEmptyHint is not null)
            LogEmptyHint.Visibility = string.IsNullOrEmpty(LogTextBox.Text) ? Visibility.Visible : Visibility.Collapsed;
    }

    private async void OnImport(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: string tag } || !Enum.TryParse(tag, out DropsPlatform platform)) return;
        using var dialog = new System.Windows.Forms.FolderBrowserDialog { Description = "选择旧版程序目录", UseDescriptionForTitle = true };
        if (dialog.ShowDialog() != System.Windows.Forms.DialogResult.OK) return;
        var destination = platform switch { DropsPlatform.Soop => AppPaths.Current.SoopDropsDir, DropsPlatform.YouTube => AppPaths.Current.YouTubeDropsDir, DropsPlatform.Bilibili => AppPaths.Current.BilibiliDropsDir, _ => AppPaths.Current.TwitchDropsDir };
        var importer = new DropsDataImporter();
        var detected = importer.Detect(platform, dialog.SelectedPath);
        if (detected.Count == 0) { ShowInfo("所选目录未识别到该平台的旧版数据。", "导入旧版数据"); return; }
        var result = await importer.ImportAsync(platform, dialog.SelectedPath, destination, relative =>
        {
            var choice = MessageBox.Show($"目标已有「{relative}」。\n\n是：覆盖\n否：跳过\n取消：终止导入", "同名数据", MessageBoxButton.YesNoCancel, MessageBoxImage.Warning);
            return choice switch { MessageBoxResult.Yes => ImportConflictAction.Overwrite, MessageBoxResult.No => ImportConflictAction.Skip, _ => ImportConflictAction.Cancel };
        });
        if (result.Cancelled) return;
        var message = result.Success ? $"导入完成：{string.Join("、", result.Copied)}\n原目录已保留。" : $"导入未完全成功：{string.Join("；", result.Failed)}";
        MessageBox.Show(message, "导入旧版数据", MessageBoxButton.OK, result.Success ? MessageBoxImage.Information : MessageBoxImage.Warning);
        await LoadPlatformAsync(platform);
    }

    private void OnWorkerEvent(object? sender, WorkerEvent message)
    {
        if (message.Platform == DropsPlatform.Bilibili)
        {
            if (message.Name == "error" && string.Equals(Text(message.Payload, "code"), "login_expired", StringComparison.OrdinalIgnoreCase))
            {
                Dispatcher.BeginInvoke(new Action(() => ShowInfo("哔哩哔哩登录已失效，请重新扫码登录。", "哔哩哔哩登录")));
            }
            return;
        }
        if (message.Platform != DropsPlatform.Twitch) return;
        if (message.Name == "log" && Bool(message.Payload, "userFacing"))
        {
            var text = Text(message.Payload, "message");
            if (string.IsNullOrWhiteSpace(text)) return;
            Dispatcher.BeginInvoke(new Action(() =>
            {
                var level = Text(message.Payload, "level", "info").ToUpperInvariant();
                var line = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {level} cloudlight.drops: {text}{Environment.NewLine}";
                _logBuffers[DropsPlatform.Twitch].Append(line);
                if (_platform == DropsPlatform.Twitch) AppendLogText(line, reset: false);
            }));
            return;
        }
        if (message.Name == "games" && message.Payload.TryGetProperty("items", out var items) &&
            items.ValueKind == JsonValueKind.Array)
        {
            Dispatcher.BeginInvoke(new Action(() =>
            {
                if (_vm == null) return;
                ReplaceStrings(_vm.TwitchAvailableGames, items.EnumerateArray()
                    .Select(item => item.GetString() ?? "")
                    .Concat(_vm.TwitchPriorityGames).Concat(_vm.TwitchExcludedGames));
                RefreshTwitchGameChoices();
            }));
            RefreshTwitchStateFromEvent();
            return;
        }
        if (message.Name == "login_status" && message.Payload.TryGetProperty("userId", out var userId) &&
            userId.ValueKind is not JsonValueKind.Null and not JsonValueKind.Undefined)
        {
            RefreshTwitchStateFromEvent();
            return;
        }
        if (message.Name != "auth_required") return;
        Dispatcher.BeginInvoke(new Action(() =>
        {
            var url = Text(message.Payload, "url");
            var code = Text(message.Payload, "code");
            var automatic = Bool(message.Payload, "automatic");
            _vm?.SetTwitchAuthorization(url, code, automatic);
            if (automatic || string.IsNullOrWhiteSpace(url)) return;
            try { Process.Start(new ProcessStartInfo { FileName = url, UseShellExecute = true }); }
            catch { _ = CopyLoginUrlAsync(url); }
        }));
    }

    private async Task CopyLoginUrlAsync(string url)
    {
        try
        {
            if (!await ClipboardService.CopyTextAsync(url))
                ShowInfo("无法打开浏览器，且剪贴板暂时被占用。请稍后重试。", "Twitch 登录");
        }
        catch { ShowInfo("无法打开浏览器，请稍后重试。", "Twitch 登录"); }
    }

    private void RefreshTwitchStateFromEvent()
    {
        if (_platform != DropsPlatform.Twitch) return;
        _ = Dispatcher.InvokeAsync(async () =>
        {
            if (_platform == DropsPlatform.Twitch && !_loading)
                await LoadPlatformAsync(DropsPlatform.Twitch);
        });
    }

    public async ValueTask DisposeAsync()
    {
        _retryDisplayTimer.Stop();
        _bilibiliQrPollingCts?.Cancel();
        if (_bilibiliQrPollingTask is not null)
        {
            try { await _bilibiliQrPollingTask.ConfigureAwait(false); } catch { }
        }
        _bilibiliQrPollingCts?.Dispose();
        _bilibiliQrPollingCts = null;
        if (_main is not null) _main.DropsHost.EventReceived -= OnWorkerEvent;
        _main?.SetDropsDiagnosticSnapshotProvider(null);
        _vm?.Dispose();
        if (_logTail is not null)
        {
            _logTail.Changed -= OnLogTailChanged;
            await _logTail.DisposeAsync().ConfigureAwait(false);
        }
        _logStartGate.Dispose();
    }

    private static void ReplaceStrings(System.Collections.ObjectModel.ObservableCollection<string> target, IEnumerable<string> values)
    {
        target.Clear();
        foreach (var value in values.Where(value => !string.IsNullOrWhiteSpace(value)).Distinct(StringComparer.OrdinalIgnoreCase)) target.Add(value);
    }

    private static IEnumerable<string> ArrayStrings(JsonElement owner, string property) =>
        owner.TryGetProperty(property, out var array) && array.ValueKind == JsonValueKind.Array
            ? array.EnumerateArray().Select(item => item.GetString() ?? "")
            : [];

    private static int ParseInt(TextBox box, int fallback) => int.TryParse(box.Text, out var value) ? value : fallback;
    private static string SelectedTag(ComboBox box, string fallback) => (box.SelectedItem as ComboBoxItem)?.Tag?.ToString() ?? fallback;
    private static void SelectTag(ComboBox box, string tag)
    {
        if (box.Items.Count == 0) return;
        box.SelectedItem = box.Items.OfType<ComboBoxItem>().FirstOrDefault(item => string.Equals(item.Tag?.ToString(), tag, StringComparison.OrdinalIgnoreCase)) ?? box.Items[0];
    }
    private static string Text(JsonElement owner, string property, string fallback = "") => owner.TryGetProperty(property, out var value) && value.ValueKind != JsonValueKind.Null ? value.ToString() : fallback;
    private static bool Bool(JsonElement owner, string property, bool fallback = false) => owner.TryGetProperty(property, out var value) && value.ValueKind is JsonValueKind.True or JsonValueKind.False ? value.GetBoolean() : fallback;
    private static int Int(JsonElement owner, string property, int fallback) => owner.TryGetProperty(property, out var value) && value.TryGetInt32(out var number) ? number : fallback;
    private static long Long(JsonElement owner, string property) => owner.TryGetProperty(property, out var value) && value.TryGetInt64(out var number) ? number : 0;
    private static string PlatformName(DropsPlatform platform) => platform switch { DropsPlatform.Soop => "SOOP", DropsPlatform.YouTube => "YouTube", DropsPlatform.Bilibili => "哔哩哔哩", _ => "Twitch" };
    private void ShowError(Exception ex, string title)
    {
        var message = title.Contains("Twitch", StringComparison.OrdinalIgnoreCase)
            ? "Twitch 掉宝服务运行失败，请查看运行日志。"
            : title.Contains("YouTube", StringComparison.OrdinalIgnoreCase)
                ? "YouTube 观看服务启动失败，请查看运行日志。"
                : title.Contains("SOOP", StringComparison.OrdinalIgnoreCase)
                    ? "SOOP 掉宝服务运行失败，请查看运行日志。"
                    : title.Contains("哔哩哔哩", StringComparison.OrdinalIgnoreCase) || title.Contains("Bilibili", StringComparison.OrdinalIgnoreCase)
                        ? "哔哩哔哩掉宝服务运行失败，请检查直连网络、登录状态和运行日志。"
                    : SafeUiError(ex, title);
        var dialog = new PlatformErrorWindow(title, message) { Owner = Window.GetWindow(this) };
        if (dialog.ShowDialog() == true)
        {
            LogTextBox.BringIntoView();
            LogTextBox.ScrollToEnd();
        }
    }

    private static string SafeUiError(Exception ex, string title)
    {
        var message = SensitiveDataRedactor.Redact(ex.Message);
        var technical = new[] { "HRESULT", "Traceback", "TypeError", "OSError", "OpenClipboard", "CLIPBRD_" };
        return technical.Any(token => message.Contains(token, StringComparison.OrdinalIgnoreCase))
            ? $"{title}，请查看运行日志。"
            : message;
    }
    private static void ShowInfo(string message, string title) => MessageBox.Show(message, title, MessageBoxButton.OK, MessageBoxImage.Information);
}
