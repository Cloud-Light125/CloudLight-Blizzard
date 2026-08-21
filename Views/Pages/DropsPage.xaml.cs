using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Microsoft.Win32;
using CloudLightBlizzard.Services;
using CloudLightBlizzard.Services.Drops;
using CloudLightBlizzard.ViewModels;

namespace CloudLightBlizzard.Views.Pages;

public partial class DropsPage : UserControl
{
    private MainViewModel? _main;
    private DropsViewModel? _vm;
    private DropsPlatform _platform = DropsPlatform.Soop;
    private bool _initialized;
    private bool _loading;
    private readonly PlatformLogTailService _logTail = new(AppPaths.Current.LogsDir);
    private readonly Dictionary<DropsPlatform, StringBuilder> _logBuffers = Enum.GetValues<DropsPlatform>()
        .ToDictionary(platform => platform, _ => new StringBuilder());
    private readonly SemaphoreSlim _logStartGate = new(1, 1);
    private bool _logTailStarted;

    public DropsPage()
    {
        InitializeComponent();
        _logTail.Changed += OnLogTailChanged;
        IsVisibleChanged += async (_, _) =>
        {
            if (!IsVisible || !_initialized) return;
            await EnsureLogTailStartedAsync();
            await _logTail.RefreshAsync(_platform);
            RenderLogBuffer();
            await RefreshAsync();
        };
    }

    public void Initialize(MainViewModel main)
    {
        _main = main;
        _vm = new DropsViewModel(main.DropsHost);
        DataContext = _vm;
        _vm.UpdateProxySettings(main.Settings.EnableProxy, main.Settings.ProxyUrl, main.Settings.FallbackDirect);
        main.DropsHost.EventReceived += OnWorkerEvent;
        SoopAutoStart.IsChecked = main.Settings.AutoStartSoop;
        TwitchAutoStart.IsChecked = main.Settings.AutoStartTwitch;
        SoopTab.IsChecked = true;
        SoopPanel.Visibility = Visibility.Visible;
        _initialized = true;
    }

    public void StartAutomaticPlatforms()
    {
        if (_main?.Settings.AutoStartSoop == true) _ = AutoStartSoopAsync();
        if (_main?.Settings.AutoStartTwitch == true) _ = AutoStartTwitchAsync();
        // YouTube intentionally remains manual-only.
    }

    private async Task AutoStartSoopAsync()
    {
        if (_vm == null) return;
        try
        {
            _vm.Soop.Status = "正在恢复 SOOP 账号…";
            _vm.Soop.Summary = "正在读取已保存的主账号";
            var state = await _vm.RequestAsync(DropsPlatform.Soop, "auto_start");
            if (Bool(state, "missingPrimary"))
            {
                _vm.Soop.Running = false;
                _vm.Soop.Status = "需要设置主账号";
                _vm.Soop.Summary = "SOOP 自动启动已开启，但尚未设置主账号。";
                return;
            }
            if (_platform == DropsPlatform.Soop)
            {
                _vm.ApplyState(DropsPlatform.Soop, state);
                PopulateSettings(DropsPlatform.Soop, state);
            }
            _vm.Soop.Status = "SOOP 正在运行";
        }
        catch
        {
            _vm.Soop.Running = false;
            _vm.Soop.Status = "启动失败";
            _vm.Soop.Summary = "SOOP 自动登录失败";
        }
    }

    private async Task AutoStartTwitchAsync()
    {
        if (_vm == null) return;
        try
        {
            _vm.BeginTwitchLogin();
            var state = await _vm.RequestAsync(DropsPlatform.Twitch, "auto_start");
            if (Bool(state, "requiresLogin"))
            {
                if (state.TryGetProperty("authRequired", out var auth) && auth.ValueKind == JsonValueKind.Object)
                    _vm.SetTwitchAuthorization(Text(auth, "url"), Text(auth, "code"), automatic: true);
                else
                    _vm.SetTwitchAuthorization("", "", automatic: true);
                return;
            }
            if (_platform == DropsPlatform.Twitch)
            {
                _vm.ApplyState(DropsPlatform.Twitch, state);
                PopulateSettings(DropsPlatform.Twitch, state);
            }
        }
        catch
        {
            _vm.SetTwitchFailure("Twitch 自动启动失败，请检查代理设置或运行日志。");
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
        SoopPanel.Visibility = platform == DropsPlatform.Soop ? Visibility.Visible : Visibility.Collapsed;
        YouTubePanel.Visibility = platform == DropsPlatform.YouTube ? Visibility.Visible : Visibility.Collapsed;
        TwitchPanel.Visibility = platform == DropsPlatform.Twitch ? Visibility.Visible : Visibility.Collapsed;
        await EnsureLogTailStartedAsync();
        await _logTail.RefreshAsync(platform);
        RenderLogBuffer();
        await LoadPlatformAsync(platform);
    }

    private async void OnOpenPlatform(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: string tag } || !Enum.TryParse(tag, out DropsPlatform platform)) return;
        if (platform == DropsPlatform.Soop) SoopTab.IsChecked = true;
        else if (platform == DropsPlatform.YouTube) YouTubeTab.IsChecked = true;
        else TwitchTab.IsChecked = true;
        if (_platform == platform) await LoadPlatformAsync(platform);
    }

    private async void OnStart(object sender, RoutedEventArgs e)
    {
        if (_vm == null || sender is not Button { Tag: string tag } || !Enum.TryParse(tag, out DropsPlatform platform)) return;
        await StartPlatformAsync(platform);
    }

    private async Task StartPlatformAsync(DropsPlatform platform)
    {
        if (_vm == null) return;
        try
        {
            var state = await _vm.StartAsync(platform);
            _vm.ApplyState(platform, state);
            if (platform == _platform) PopulateSettings(platform, state); else await LoadPlatformAsync(_platform);
        }
        catch (Exception ex) { ShowError(ex, $"启动 {PlatformName(platform)} 失败"); }
    }

    private async void OnStop(object sender, RoutedEventArgs e)
    {
        if (_vm == null || sender is not Button { Tag: string tag } || !Enum.TryParse(tag, out DropsPlatform platform)) return;
        await StopPlatformAsync(platform);
    }

    private async Task StopPlatformAsync(DropsPlatform platform)
    {
        if (_vm == null) return;
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
        else await RefreshPlatformAsync(_platform);
    }

    private async void OnSoopRefresh(object sender, RoutedEventArgs e) => await RefreshSoopAsync();

    private async Task RefreshSoopAsync()
    {
        if (_vm == null || _vm.IsSoopRefreshing || _loading) return;
        _vm.BeginSoopRefresh();
        _loading = true;
        try
        {
            var state = await _vm.RequestAsync(DropsPlatform.Soop, "refresh");
            _vm.ApplyState(DropsPlatform.Soop, state);
            PopulateSettings(DropsPlatform.Soop, state);
            _vm.CompleteSoopRefresh();
        }
        catch (Exception ex)
        {
            _vm.FailSoopRefresh();
            ShowError(ex, "刷新 SOOP 掉宝信息失败");
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
        try
        {
            await _vm.RequestAsync(DropsPlatform.Soop, command, new { userid = row.Id });
            await LoadPlatformAsync(DropsPlatform.Soop);
        }
        catch (Exception ex) { ShowError(ex, $"{action} SOOP 账号失败"); }
    }

    private async void OnSoopDeleteAccount(object sender, RoutedEventArgs e)
    {
        if (_vm == null) return;
        if (SoopAccountsList.SelectedItem is not DropsRow row) { ShowInfo("请先选择一个 SOOP 账号。", "删除账号"); return; }
        if (MessageBox.Show($"删除 SOOP 账号「{row.Primary}」的本地登录信息？", "删除账号", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes) return;
        try { await _vm.RequestAsync(DropsPlatform.Soop, "delete_account", new { userid = row.Id }); await LoadPlatformAsync(DropsPlatform.Soop); }
        catch (Exception ex) { ShowError(ex, "删除 SOOP 账号失败"); }
    }

    private async void OnCopySoopCode(object sender, RoutedEventArgs e)
    {
        if (_vm == null) return;
        if (SoopInventoryList.SelectedItem is not DropsRow row) { ShowInfo("请先选择一项包含兑换码的奖励。", "复制兑换码"); return; }
        try
        {
            var result = await _vm.RequestAsync(DropsPlatform.Soop, "copy_redeem_code", new { id = row.Id });
            var code = Text(result, "redeemCode");
            if (string.IsNullOrWhiteSpace(code)) throw new InvalidOperationException("该奖励没有可复制的兑换码。");
            if (!await ClipboardService.CopyTextAsync(code))
                ShowInfo("剪贴板暂时被其它程序占用，请稍后重试。", "复制兑换码");
        }
        catch (Exception ex)
        {
            Trace.TraceError("SOOP redeem code copy failed: {0}", SensitiveDataRedactor.Redact(ex.Message));
            ShowInfo("复制失败，请稍后重试。", "复制兑换码");
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
        if (_vm == null) return;
        _vm.BeginTwitchLogin();
        try { await _vm.RequestAsync(DropsPlatform.Twitch, "login"); await LoadPlatformAsync(DropsPlatform.Twitch); }
        catch (Exception ex) { _vm.SetTwitchFailure("Twitch 登录启动失败，请检查代理设置或运行日志。"); ShowError(ex, "启动 Twitch 登录失败"); }
    }

    private void OnOpenTwitchAuthorization(object sender, RoutedEventArgs e)
    {
        if (_vm == null || string.IsNullOrWhiteSpace(_vm.TwitchAuthorizationUrl)) return;
        try { Process.Start(new ProcessStartInfo { FileName = _vm.TwitchAuthorizationUrl, UseShellExecute = true }); }
        catch { _ = CopyLoginUrlAsync(_vm.TwitchAuthorizationUrl); }
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
        _main.Settings.Save();
    }

    private async void OnTwitchLogout(object sender, RoutedEventArgs e)
    {
        if (_vm == null || MessageBox.Show("退出 Twitch 并删除本地登录信息？", "退出登录", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes) return;
        try { await _vm.RequestAsync(DropsPlatform.Twitch, "logout"); await LoadPlatformAsync(DropsPlatform.Twitch); }
        catch (Exception ex) { ShowError(ex, "Twitch 退出登录失败"); }
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
        if (_logTailStarted) return;
        await _logStartGate.WaitAsync();
        try
        {
            if (_logTailStarted) return;
            await _logTail.StartAsync();
            _logTailStarted = true;
        }
        finally { _logStartGate.Release(); }
    }

    private void OnLogTailChanged(object? sender, PlatformLogChunk chunk) => Dispatcher.BeginInvoke(new Action(() =>
    {
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

    private void OnClearLogDisplay(object sender, RoutedEventArgs e)
    {
        _logBuffers[_platform].Clear();
        LogTextBox.Clear();
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
        var destination = platform switch { DropsPlatform.Soop => AppPaths.Current.SoopDropsDir, DropsPlatform.YouTube => AppPaths.Current.YouTubeDropsDir, _ => AppPaths.Current.TwitchDropsDir };
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
        if (message.Platform != DropsPlatform.Twitch) return;
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
        if (_main is not null) _main.DropsHost.EventReceived -= OnWorkerEvent;
        _vm?.Dispose();
        _logTail.Changed -= OnLogTailChanged;
        await _logTail.DisposeAsync().ConfigureAwait(false);
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
    private static string PlatformName(DropsPlatform platform) => platform switch { DropsPlatform.Soop => "SOOP", DropsPlatform.YouTube => "YouTube", _ => "Twitch" };
    private void ShowError(Exception ex, string title)
    {
        var message = title.Contains("Twitch", StringComparison.OrdinalIgnoreCase)
            ? "Twitch 掉宝服务运行失败，请查看运行日志。"
            : title.Contains("YouTube", StringComparison.OrdinalIgnoreCase)
                ? "YouTube 观看服务启动失败，请查看运行日志。"
                : title.Contains("SOOP", StringComparison.OrdinalIgnoreCase)
                    ? "SOOP 掉宝服务运行失败，请查看运行日志。"
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
