using System.ComponentModel;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using CloudLightBlizzard.Models;
using CloudLightBlizzard.Services;
using CloudLightBlizzard.Services.Drops;
using CloudLightBlizzard.Services.Notifications;
using CloudLightBlizzard.Services.OverwatchRegion;
using CloudLightBlizzard.ViewModels;
using CloudLightBlizzard.Views;
using CloudLightBlizzard.Views.Pages;

namespace CloudLightBlizzard;

public partial class MainWindow : Window
{
    internal static readonly TimeSpan AnnouncementRefreshInterval = TimeSpan.FromMinutes(30);
    private readonly MainViewModel _vm;
    private readonly AccountsPage _accountsPage = new();
    private readonly RegionFilesPage _regionPage = new();
    private readonly StatsPage _statsPage = new();
    private readonly DropsPage _dropsPage = new();
    private readonly SnapshotsPage _snapshotsPage = new();
    private readonly DiagnosticsPage _diagnosticsPage = new();
    private readonly SettingsPage _settingsPage = new();
    private readonly AboutPage _aboutPage = new();
    private readonly System.Windows.Threading.DispatcherTimer _watchTimer = new() { Interval = TimeSpan.FromSeconds(2) };
    private readonly EventWaitHandle _showEvent;
    private readonly RegisteredWaitHandle _showRegistration;
    private System.Windows.Forms.NotifyIcon? _tray;
    private System.Windows.Forms.ContextMenuStrip? _trayMenu;
    private volatile bool _isClosing;
    private volatile bool _isExiting;
    private bool _exitRequested;
    private bool _showSignalDisposed;
    private bool _exitCleanupStarted;
    private Task? _exitCleanupTask;
    private Task _accountDrainTask = Task.CompletedTask;
    private readonly ExitLifecycleCoordinator _exitLifecycleCoordinator =
        new(TimeSpan.FromSeconds(90));
    private bool _pagesReady;
    private bool _initialized;
    private readonly CancellationTokenSource _updateCancellation = new();
    private bool _updateCancellationDisposed;
    private readonly UpdateInstallerLaunchCoordinator _installerLaunchCoordinator;
    private bool _installerStarted;
    internal Func<ExitCleanupStage, Task>? CleanupFailureForSelfTest { get; set; }
    internal Action<string>? UpdateFailureForSelfTest { get; set; }
    internal ExitLifecycleCoordinator ExitResult => _exitLifecycleCoordinator;
    private readonly AnnouncementService _announcementService;
    private readonly INotificationService _notificationService;
    private readonly DropsNotificationGate _dropsNotificationGate;
    private readonly object _dropsNotificationSync = new();
    private readonly HashSet<string> _dropsCompletionNotifications = [];
    private IReadOnlyList<Announcement> _announcements = Array.Empty<Announcement>();
    private Task? _announcementRefreshTask;
    public AnnouncementService AnnouncementState => _announcementService;

    public MainWindow(bool startHidden = false, Services.Drops.PlatformLogSession? logSession = null,
        IInstallerLauncher? installerLauncher = null, AppSettings? settings = null, BattleNetPaths? paths = null,
        TimeSpan? exitBudgetForSelfTest = null)
    {
        _vm = new MainViewModel(logSession, settings, paths);
        if (exitBudgetForSelfTest is not null) _exitLifecycleCoordinator = new(exitBudgetForSelfTest);
        _installerLaunchCoordinator = new UpdateInstallerLaunchCoordinator(
            installerLauncher ?? new ProcessInstallerLauncher());
        _announcementService = new AnnouncementService(_vm.Settings, httpClients: _vm.CloudHttpClients);
        _notificationService = new WindowsToastNotificationService(_vm.Settings);
        _dropsNotificationGate = new DropsNotificationGate(Notify);
        InitializeComponent();
        ThemeManager.Attach(this);
        _showEvent = new EventWaitHandle(false, EventResetMode.AutoReset, App.ShowEventName);
        _showRegistration = ThreadPool.RegisterWaitForSingleObject(
            _showEvent,
            OnShowSignal,
            null,
            Timeout.Infinite,
            false);
        DataContext = _vm;
        Width = _vm.Settings.WindowWidth >= MinWidth ? _vm.Settings.WindowWidth : Width;
        Height = _vm.Settings.WindowHeight >= MinHeight ? _vm.Settings.WindowHeight : Height;
        // WorkArea is in DIPs, so a 1366x768 display at 150% must not force a
        // 1000x660 DIP window outside the available desktop.
        var available = SystemParameters.WorkArea;
        MinWidth = Math.Min(MinWidth, available.Width);
        MinHeight = Math.Min(MinHeight, available.Height);
        Width = Math.Min(Width, available.Width);
        Height = Math.Min(Height, available.Height);
        if (_vm.Settings.WindowMaximized) WindowState = WindowState.Maximized;

        _accountsPage.Initialize(_vm);
        _regionPage.Initialize(_vm);
        _statsPage.Initialize(_vm);
        _dropsPage.Initialize(_vm);
        _snapshotsPage.Initialize(_vm);
        _diagnosticsPage.Initialize(_vm);
        _settingsPage.Initialize(_vm);
        _aboutPage.Initialize(_vm);
        _settingsPage.AnnouncementBadgeSettingChanged += (_, _) =>
            _announcementService.NotifyBadgeSettingChanged();
        _settingsPage.InitializeNotificationSettings(_vm);
        _vm.RegionSwitchCompleted += OnRegionSwitchCompleted;
        _vm.DropsHost.EventReceived += OnDropsEventForNotification;
        _notificationService.Initialize(action => Dispatcher.BeginInvoke(() => HandleNotificationAction(action)));
        _accountsPage.OpenStatsRequested += row => { StatsNav.IsChecked = true; _statsPage.SelectAccount(row); };
        _pagesReady = true;
        SelectSavedSection();

        SetupTray();
        if (startHidden)
        {
            ShowInTaskbar = false;
            Dispatcher.BeginInvoke(new Action(() => _ = InitializeAsync()), DispatcherPriority.ApplicationIdle);
        }
        else
        {
            Loaded += async (_, _) => await InitializeAsync();
        }
    }

    private async Task InitializeAsync()
    {
        if (_isExiting || _initialized) return;
        _initialized = true;
        await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
        if (_isExiting) return;
        await _vm.RefreshAsync();
        if (_isExiting) return;
        var args = Environment.GetCommandLineArgs();
        var demoIndex = Array.FindIndex(args, value => string.Equals(value, "--accountlayoutdemo", StringComparison.OrdinalIgnoreCase));
        if (demoIndex >= 0 && demoIndex + 1 < args.Length && int.TryParse(args[demoIndex + 1], out var demoCount))
        {
            _vm.ApplyAccountLayoutDemo(demoCount);
            AccountsNav.IsChecked = true;
        }
        if (StatsNav.IsChecked == true) _statsPage.SelectAccount(_vm.CurrentAccount ?? _vm.SavedAccounts.FirstOrDefault());
        if (RegionNav.IsChecked == true) await _regionPage.RefreshAsync();
        if (SnapshotsNav.IsChecked == true) await _snapshotsPage.RefreshAsync();
        if (_isExiting) return;
        _watchTimer.Tick += async (_, _) => await _vm.PollAccountsAsync();
        _watchTimer.Start();
        _dropsPage.StartAutomaticPlatforms();
        _ = RunAutomaticUpdateCheckAsync();
        _announcementRefreshTask ??= RunAnnouncementRefreshLoopAsync();
    }

    private async Task RunAnnouncementRefreshLoopAsync()
    {
        try
        {
            await AnnouncementService.RunPeriodicRefreshAsync(
                RefreshAnnouncementsAsync, AnnouncementRefreshInterval, _updateCancellation.Token);
        }
        catch (OperationCanceledException) when (_updateCancellation.IsCancellationRequested) { }
    }

    private async Task RefreshAnnouncementsAsync(CancellationToken cancellationToken)
    {
        var cached = _announcementService.CachedAnnouncements;
        if (!Dispatcher.HasShutdownStarted)
            await Dispatcher.InvokeAsync(() => ApplyAnnouncements(cached));
        try
        {
            var refreshed = await _announcementService.RefreshAsync(cancellationToken);
            if (!Dispatcher.HasShutdownStarted)
                await Dispatcher.InvokeAsync(() => ApplyAnnouncements(refreshed));
        }
        catch (OperationCanceledException) { }
    }

    private void ApplyAnnouncements(IReadOnlyList<Announcement> announcements)
    {
        var newAnnouncement = _announcements.Count > 0
            ? announcements.FirstOrDefault(item => !_announcements.Any(old =>
                string.Equals(old.Id, item.Id, StringComparison.Ordinal) && old.Revision >= item.Revision))
            : null;
        _announcements = announcements;
        if (newAnnouncement is not null)
            Notify(new NotificationRequest("CloudLight Blizzard 公告", newAnnouncement.Title,
                NotificationCategory.Announcements, "accounts", $"announcement:{newAnnouncement.Id}:{newAnnouncement.Revision}"));
    }

    internal async Task OpenAnnouncementsAsync()
    {
        if (_announcements.Count == 0) await RefreshAnnouncementsAsync(_updateCancellation.Token);
        var dialog = new AnnouncementWindow(_announcements, _announcementService) { Owner = this };
        dialog.ShowDialog();
    }

    private async Task RunAutomaticUpdateCheckAsync()
    {
        try
        {
            var outcome = await _vm.UpdateChecks.CheckAfterDelayAsync(
                TimeSpan.FromSeconds(3), _updateCancellation.Token);
            await PresentUpdateOutcome(outcome, automatic: true);
        }
        catch (OperationCanceledException) { }
    }

    internal async Task CheckForUpdatesManuallyAsync()
    {
        try
        {
            var outcome = await _vm.UpdateChecks.CheckAsync(UpdateCheckMode.Manual, _updateCancellation.Token);
            await PresentUpdateOutcome(outcome, automatic: false);
        }
        catch (OperationCanceledException) { }
    }

    private async Task PresentUpdateOutcome(UpdateCheckOutcome outcome, bool automatic)
    {
        if (_isExiting) return;
        if (outcome.Kind == UpdateCheckOutcomeKind.UpdateAvailable && outcome.Result is { } result)
        {
            Notify(new NotificationRequest("CloudLight Blizzard 有新版本", $"版本 {result.LatestVersion} 已发布。",
                NotificationCategory.Updates, "updates", $"update:{result.Tag}:{result.Channel}"));
            if (automatic && !IsVisible) return;
            var dialog = new UpdateDialog(result, _vm.UpdateDownloader) { Owner = this };
            dialog.ShowDialog();
            if (dialog.SkipVersion) _vm.UpdateChecks.SkipVersion(result.LatestVersion);
            else if (dialog.Action == UpdateDialogAction.Later) _vm.UpdateChecks.RemindLater();
            _settingsPage.RefreshUpdateInfo();
            if (dialog.Action == UpdateDialogAction.OpenRelease)
                OpenUpdateRelease(result.ReleaseUrl);
            else if (dialog.Action == UpdateDialogAction.InstallDownloaded &&
                      !string.IsNullOrWhiteSpace(dialog.DownloadedInstallerPath))
            {
                if (await InstallDownloadedUpdate(dialog.DownloadedInstallerPath, dialog.MarkInstallerStarted))
                    _vm.RecordSuccessfulUpdate(result);
            }
            return;
        }

        if (automatic || outcome.Kind is UpdateCheckOutcomeKind.Suppressed or UpdateCheckOutcomeKind.AlreadyChecked)
            return;
        if (outcome.Kind == UpdateCheckOutcomeKind.UpToDate)
            MessageBox.Show($"当前版本：{_vm.UpdateChecks.CurrentVersion}", "已是最新版本",
                MessageBoxButton.OK, MessageBoxImage.Information);
        else if (outcome.Kind == UpdateCheckOutcomeKind.NoRelease)
            MessageBox.Show("当前没有可用的正式更新。", "软件更新",
                MessageBoxButton.OK, MessageBoxImage.Information);
        else if (outcome.Kind == UpdateCheckOutcomeKind.Failed)
            MessageBox.Show(outcome.Result?.ErrorMessage ?? "暂时无法连接更新服务器。", "暂时无法检查更新",
                MessageBoxButton.OK, MessageBoxImage.Information);
    }

    internal void OpenUpdateRelease(string? releaseUrl)
    {
        if (string.IsNullOrWhiteSpace(releaseUrl)) return;
        try
        {
            Process.Start(new ProcessStartInfo { FileName = releaseUrl, UseShellExecute = true });
        }
        catch
        {
            MessageBox.Show("无法打开系统浏览器，请稍后重试。", "打开更新链接",
                MessageBoxButton.OK, MessageBoxImage.Information);
        }
    }

    internal void OpenUpdateSettings()
    {
        SettingsNav.IsChecked = true;
        _settingsPage.FocusUpdateSection();
    }

    private void HandleNotificationAction(string action)
    {
        if (!Dispatcher.CheckAccess())
        {
            _ = Dispatcher.BeginInvoke(() => HandleNotificationAction(action));
            return;
        }
        if (Dispatcher.HasShutdownStarted || Dispatcher.HasShutdownFinished) return;
        switch (action)
        {
            case "accounts": AccountsNav.IsChecked = true; break;
            case "updates": OpenUpdateSettings(); break;
            case "drops": OpenDrops(); break;
            case "region": OpenRegion(); break;
            default: AccountsNav.IsChecked = true; break;
        }
        ShowFromTray();
    }

    private void OnRegionSwitchCompleted(bool success, OverwatchRegion target, string detail)
    {
        Notify(new NotificationRequest(
            success ? "区服切换成功" : "区服切换失败",
            success ? $"已切换到{MainViewModel.RegionDisplayName(target)}。" : "区服切换未完成，请打开区服页面查看详情。",
            NotificationCategory.RegionSwitch, "region"));
    }

    private void OnDropsEventForNotification(object? sender, WorkerEvent message)
    {
        if (TryGetCompletedDrop(message, out var dropId, out var dropName))
        {
            var completionKey = $"{message.Platform}:{dropId}";
            var firstCompletion = false;
            lock (_dropsNotificationSync) firstCompletion = _dropsCompletionNotifications.Add(completionKey);
            if (firstCompletion)
            {
                Notify(new NotificationRequest("Drops 完成",
                    string.IsNullOrWhiteSpace(dropName) ? $"{PlatformName(message.Platform)} 有一个掉宝已完成。" : $"{PlatformName(message.Platform)}：{dropName} 已完成。",
                    NotificationCategory.Drops, "drops", $"drop-completed:{completionKey}"));
            }
        }
        if (message.Platform == DropsPlatform.Bilibili && message.Name is ("status" or "session"))
        {
            var state = Text(message.Payload, "connectionState");
            if (state is "Degraded" or "WaitingRetry" or "Failed")
                _dropsNotificationGate.ReportFailure(message.Platform,
                    $"{PlatformName(message.Platform)} 连接质量下降", "部分 Session 正在自动恢复。");
            else if (state == "Connected")
                _dropsNotificationGate.ReportRecovery(message.Platform,
                    $"{PlatformName(message.Platform)} 已恢复连接", "自动恢复流程已完成。");
            return;
        }
        if (message.Name is not ("connection_status" or "runtime_error" or "runtime_recovered")) return;
        var phase = message.Name == "connection_status"
            ? Text(message.Payload, "phase") : message.Name;
        var failure = phase is "realtime_disconnected" or "realtime_reconnecting" or "network_failed" or
            "proxy_failed" or "proxy_and_direct_failed" or "runtime_error";
        var recovery = phase is "realtime_connected" or "reconnected" or "connection_recovered" or
            "runtime_recovered";
        if (failure)
        {
            _dropsNotificationGate.ReportFailure(message.Platform,
                $"{PlatformName(message.Platform)} 连接中断", "正在自动重试，恢复后会再次通知。");
        }
        else if (recovery)
        {
            _dropsNotificationGate.ReportRecovery(message.Platform,
                $"{PlatformName(message.Platform)} 已恢复连接", "自动恢复流程已完成。");
        }
    }

    private void Notify(NotificationRequest request) =>
        NotificationSafety.TryNotifySafely(_notificationService, request);

    private static string PlatformName(DropsPlatform platform) => platform switch
    {
        DropsPlatform.Soop => "SOOP",
        DropsPlatform.YouTube => "YouTube",
        DropsPlatform.Bilibili => "哔哩哔哩",
        _ => "Twitch",
    };

    private static string Text(System.Text.Json.JsonElement owner, string name, string fallback = "")
    {
        if (!owner.TryGetProperty(name, out var value)) return fallback;
        if (value.ValueKind == System.Text.Json.JsonValueKind.String) return value.GetString() ?? fallback;
        if (value.ValueKind is System.Text.Json.JsonValueKind.Null or System.Text.Json.JsonValueKind.Undefined) return fallback;
        return value.ToString();
    }

    private static bool TryGetCompletedDrop(WorkerEvent message, out string dropId, out string dropName)
    {
        dropId = "";
        dropName = "";
        var payload = message.Payload;
        if (payload.ValueKind != System.Text.Json.JsonValueKind.Object) return false;

        if (message.Name is "drop" or "reward")
        {
            dropId = Text(payload, "id", Text(payload, "dropId", Text(payload, "taskId")));
            dropName = Text(payload, "name", Text(payload, "reward"));
            if (message.Platform == DropsPlatform.Bilibili &&
                payload.TryGetProperty("success", out var success) && success.ValueKind == System.Text.Json.JsonValueKind.True)
                return !string.IsNullOrWhiteSpace(dropId);
            var completed = payload.TryGetProperty("completed", out var completedValue) &&
                            completedValue.ValueKind == System.Text.Json.JsonValueKind.True;
            var status = Text(payload, "status");
            if (!completed && !string.Equals(status, "completed", StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(status, "complete", StringComparison.OrdinalIgnoreCase))
            {
                completed = Number(payload, "currentMinutes") >= Number(payload, "requiredMinutes") &&
                            Number(payload, "requiredMinutes") > 0;
            }
            return completed && !string.IsNullOrWhiteSpace(dropId);
        }

        if (message.Platform == DropsPlatform.Bilibili && message.Name is ("task" or "progress") &&
            payload.TryGetProperty("tasks", out var tasks) && tasks.ValueKind == System.Text.Json.JsonValueKind.Array)
        {
            foreach (var item in tasks.EnumerateArray())
            {
                if (item.ValueKind != System.Text.Json.JsonValueKind.Object ||
                    !item.TryGetProperty("completed", out var completed) || completed.ValueKind != System.Text.Json.JsonValueKind.True)
                    continue;
                dropId = Text(item, "id");
                dropName = Text(item, "name");
                if (!string.IsNullOrWhiteSpace(dropId)) return true;
            }
        }

        if (message.Name == "account_status" && payload.TryGetProperty("currentProgress", out var progress) &&
            progress.ValueKind == System.Text.Json.JsonValueKind.Array)
        {
            foreach (var item in progress.EnumerateArray())
            {
                if (item.ValueKind != System.Text.Json.JsonValueKind.Object) continue;
                var completed = item.TryGetProperty("completed", out var completedValue) &&
                                completedValue.ValueKind == System.Text.Json.JsonValueKind.True;
                var percent = Number(item, "percent");
                if (!completed && percent < 100) continue;
                dropId = Text(item, "id");
                dropName = Text(item, "reward", Text(item, "campaign"));
                if (!string.IsNullOrWhiteSpace(dropId)) return true;
            }
        }

        return false;
    }

    private static double Number(System.Text.Json.JsonElement owner, string name)
    {
        if (!owner.TryGetProperty(name, out var value)) return 0;
        if (value.ValueKind == System.Text.Json.JsonValueKind.Number && value.TryGetDouble(out var number)) return number;
        return double.TryParse(value.ToString(), out number) ? number : 0;
    }

    internal async Task<bool> InstallDownloadedUpdate(string installerPath, Action? installerStarted = null)
    {
        if (_installerStarted) return true;
        if (_isExiting || _exitCleanupStarted) return false;
        if (!File.Exists(installerPath) || !PersistSettingsBeforeUpdate())
        {
            MessageBox.Show("安装包不存在或设置无法保存，已取消安装。", "在线更新");
            return false;
        }

        Notify(new NotificationRequest("在线更新下载完成", "安装包已验证，正在安全停止后台任务。",
            NotificationCategory.Updates, "updates"));
        // Stop/account-drain BEFORE launching an installer that may force-close
        // this process. The UI continues pumping throughout the drain.
        _exitRequested = true;
        BeginExit();
        _exitCleanupStarted = true;
        var cleanup = _exitLifecycleCoordinator.BeginAsync(CompleteExitCleanupAsync);
        _exitCleanupTask = cleanup;
        await cleanup;
        if (_exitLifecycleCoordinator.Error is Exception cleanupError) RecordExitError("exit coordinator", cleanupError);
        var started = false;
        try
        {
            if (!_exitLifecycleCoordinator.InstallerReady)
            {
                _vm.UpdateDownloader.MarkFailed();
                var stages = string.Join("、", _exitLifecycleCoordinator.Stages
                    .Where(stage => stage.Critical && stage.Error is not null).Select(stage => stage.Stage));
                ReportUpdateFailure($"关键退出清理未成功（{_exitLifecycleCoordinator.State}；{stages}），已阻止安装。\n" +
                    "程序将保持退出，不会重新启用已释放的服务；请查看退出诊断并重新启动后重试。");
                return false;
            }
            _vm.UpdateDownloader.MarkLaunchingInstaller();
            started = _installerLaunchCoordinator.TryLaunchAndRequestShutdown(installerPath,
                PersistSettingsBeforeUpdate,
                () => { _installerStarted = true; installerStarted?.Invoke(); },
                () => { }, out var error);
            if (!started)
            {
                _vm.UpdateDownloader.MarkFailed();
                ReportUpdateFailure($"无法启动更新安装程序：{error}\n后台已安全停止；请重新打开程序后重试。");
            }
        }
        finally
        {
            if (!Dispatcher.HasShutdownStarted) Close();
        }
        return started;
    }

    private void ReportUpdateFailure(string message)
    {
        if (UpdateFailureForSelfTest is { } report) report(message);
        else MessageBox.Show(message, "在线更新", MessageBoxButton.OK, MessageBoxImage.Warning);
    }

    private bool PersistSettingsBeforeUpdate()
    {
        if (WindowState != WindowState.Maximized)
        {
            if (ActualWidth >= MinWidth) _vm.Settings.WindowWidth = ActualWidth;
            if (ActualHeight >= MinHeight) _vm.Settings.WindowHeight = ActualHeight;
        }
        _vm.Settings.WindowMaximized = WindowState == WindowState.Maximized;
        return _vm.Settings.Save();
    }

    private void SelectSavedSection()
    {
        var section = AppSettings.NormalizeMainSection(_vm.Settings.LastMainSection);
        if (!string.Equals(_vm.Settings.LastMainSection, section, StringComparison.Ordinal))
        {
            _vm.Settings.LastMainSection = section;
            _vm.Settings.Save();
        }
        switch (section)
        {
            case "accounts": AccountsNav.IsChecked = true; break;
            case "region": RegionNav.IsChecked = true; break;
            case "stats": StatsNav.IsChecked = true; break;
            case "drops": DropsNav.IsChecked = true; break;
            case "snapshots": SnapshotsNav.IsChecked = true; break;
            case "diagnostics": DiagnosticsNav.IsChecked = true; break;
            case "settings": SettingsNav.IsChecked = true; break;
            case "about": AboutNav.IsChecked = true; break;
            default: AccountsNav.IsChecked = true; break;
        }
    }

    private async void OnNavigate(object sender, RoutedEventArgs e)
    {
        if (_isExiting || !_pagesReady || sender is not RadioButton { Tag: string section }) return;
        PageHost.Content = section switch
        {
            "region" => _regionPage,
            "stats" => _statsPage,
            "drops" => _dropsPage,
            "snapshots" => _snapshotsPage,
            "diagnostics" => _diagnosticsPage,
            "settings" => _settingsPage,
            "about" => _aboutPage,
            _ => _accountsPage,
        };
        AccountFooter.Visibility = section == "accounts" ? Visibility.Visible : Visibility.Collapsed;
        _vm.Settings.LastMainSection = section;
        _vm.Settings.Save();
        await Dispatcher.Yield(DispatcherPriority.Render);
        if (_isExiting) return;
        if (section == "region") await _regionPage.RefreshAsync();
        if (section == "stats") _statsPage.OnPageOpened();
        if (section == "drops") await _dropsPage.RefreshAsync();
        if (section == "snapshots") await _snapshotsPage.RefreshAsync();
    }

    internal static bool ShouldStartHidden(IEnumerable<string> args, bool startMinimized)
    {
        if (args.Any(a => string.Equals(a, "--visible", StringComparison.OrdinalIgnoreCase))) return false;
        return args.Any(a => string.Equals(a, "--tray", StringComparison.OrdinalIgnoreCase)) || startMinimized;
    }
    private async void OnRefresh(object sender, RoutedEventArgs e)
    {
        if (DropsNav.IsChecked == true) await _dropsPage.RefreshAsync(); else await _vm.RefreshAsync();
    }

    public void OpenStatsAccount(long accountId)
    {
        StatsNav.IsChecked = true;
        var saved = _vm.SavedAccounts.FirstOrDefault(account => account.AccountId == accountId);
        if (saved != null) _statsPage.SelectAccount(saved);
        else _statsPage.SelectAccount(null);
    }

    internal void OpenProxySettings()
    {
        SettingsNav.IsChecked = true;
        _settingsPage.FocusProxySection();
    }

    internal void OpenDiagnostics()
    {
        DiagnosticsNav.IsChecked = true;
    }

    internal void OpenDrops()
    {
        DropsNav.IsChecked = true;
    }

    internal void OpenRegion()
    {
        RegionNav.IsChecked = true;
    }

    internal void OpenSnapshots()
    {
        SnapshotsNav.IsChecked = true;
    }

    private void SetupTray()
    {
        _tray = new System.Windows.Forms.NotifyIcon { Text = "CloudLight Blizzard", Visible = true, Icon = LoadTrayIcon() };
        _tray.DoubleClick += OnTrayDoubleClick;
        _trayMenu = TrayMenuFactory.Create(
            ("打开 CloudLight Blizzard", OnTrayOpen),
            ("-", null),
            ("退出", OnTrayExit));
        _tray.ContextMenuStrip = _trayMenu;
    }
    private static Icon? LoadTrayIcon()
    {
        try { var info = Application.GetResourceStream(new Uri("pack://application:,,,/Assets/app.ico")); return info?.Stream is { } stream ? new Icon(stream) : null; }
        catch { return null; }
    }
    private void OnShowSignal(object? state, bool timedOut)
    {
        if (_isClosing || _isExiting || Dispatcher.HasShutdownStarted || Dispatcher.HasShutdownFinished) return;
        Dispatcher.BeginInvoke(new Action(ShowFromTray));
    }

    private void OnTrayDoubleClick(object? sender, EventArgs e) => ShowFromTray();
    private void OnTrayOpen(object? sender, EventArgs e) => ShowFromTray();
    private void OnTrayExit(object? sender, EventArgs e) => ExitApp();

    private void ShowFromTray()
    {
        if (_isClosing || _isExiting || Dispatcher.HasShutdownStarted || Dispatcher.HasShutdownFinished) return;
        if (!Dispatcher.CheckAccess())
        {
            Dispatcher.BeginInvoke(new Action(ShowFromTray));
            return;
        }
        if (_isClosing || _isExiting) return;
        if (!IsVisible) Show();
        ShowInTaskbar = true;
        if (WindowState == WindowState.Minimized) WindowState = WindowState.Normal;
        Activate();
    }

    private void HideToTray()
    {
        if (_isExiting) return;
        Hide();
        ShowInTaskbar = false;
    }

    private void ExitApp()
    {
        if (_isClosing || _isExiting) return;
        if (!Dispatcher.CheckAccess())
        {
            Dispatcher.BeginInvoke(new Action(ExitApp));
            return;
        }
        if (_isClosing || _isExiting) return;
        _exitRequested = true;
        Close();
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        if (_isExiting)
        {
            // The first close request starts async cleanup and is cancelled;
            // the coordinator asks WPF to close again only after cleanup.
            if (!_exitLifecycleCoordinator.IsCompleted)
            {
                e.Cancel = true;
                return;
            }
            base.OnClosing(e);
            return;
        }
        if (_isClosing)
        {
            e.Cancel = true;
            return;
        }

        _isClosing = true;

        // 用户点击主窗口关闭按钮，并且启用了“关闭主页面时最小化到托盘”时，
        // 直接隐藏到托盘；只有明确执行“退出”时才检查正在运行的掉宝任务。
        if (!_exitRequested && _vm.Settings.CloseToTray)
        {
            CancelClosing(e);
            HideToTray();
            return;
        }

        if (_vm.DropsHost.AnyRunning)
        {
            var dialog = new ExitConfirmationDialog { Owner = this };
            dialog.ShowDialog();
            if (dialog.Choice == ExitChoice.Cancel)
            {
                CancelClosing(e);
                return;
            }
            if (dialog.Choice == ExitChoice.MinimizeToTray)
            {
                CancelClosing(e);
                HideToTray();
                return;
            }
            BeginExit();
            e.Cancel = true;
            StartExitCleanup();
            return;
        }

        BeginExit();
        e.Cancel = true;
        StartExitCleanup();
    }

    private void CancelClosing(CancelEventArgs e)
    {
        e.Cancel = true;
        _exitRequested = false;
        _isClosing = false;
    }

    private void BeginExit()
    {
        if (_isExiting) return;
        _isExiting = true;
        IsEnabled = false;
        _isClosing = true;
        _watchTimer.Stop();
        _dropsPage.BeginShutdown();
        _vm.DropsHost.BeginShutdown();
        _accountDrainTask = _vm.BeginShutdownAsync();
        _settingsPage.CancelUpdateDownload();
        _vm.CancelSwitchPlan();
        if (!_updateCancellationDisposed)
            _updateCancellation.Cancel();
        DisposeTray();
        DisposeShowSignal();
    }

    private void StartExitCleanup()
    {
        if (_exitCleanupStarted) return;
        _exitCleanupStarted = true;
        _exitCleanupTask = AwaitExitCleanupAndCloseAsync();
    }

    private async Task AwaitExitCleanupAndCloseAsync()
    {
        await Task.Yield(); // Leave the first synchronous Closing event first.
        await _exitLifecycleCoordinator.BeginAsync(CompleteExitCleanupAsync);
        if (_exitLifecycleCoordinator.Error is Exception error) RecordExitError("exit coordinator", error);
        if (Dispatcher.HasShutdownStarted || Dispatcher.HasShutdownFinished) return;
        // The first Closing notification was cancelled.  Re-enter Closing
        // only after all cleanup has completed; this second notification is
        // deliberately the only one allowed to call base.OnClosing.
        Close();
    }

    private async Task CompleteExitCleanupAsync(CancellationToken cancellationToken)
    {
        // BeginShutdownAsync closes the account-operation gate and waits for
        // an already-written account transaction to finish or roll back.  Do
        // not dispose DropsHost before that await has completed.
        // This stage intentionally is not cut short by the coordinator budget:
        // abandoning an in-flight account restore can corrupt the user's
        // profile.  The page/worker stages below have their own finite waits.
        _ = cancellationToken;
        async Task Stage(ExitCleanupStage stage, Func<Task> action)
        {
            await _exitLifecycleCoordinator.RunStageAsync(stage, async () =>
            {
                await action();
                if (CleanupFailureForSelfTest is { } inject) await inject(stage);
            });
        }
        await Stage(ExitCleanupStage.AccountOperationDrain, () => _accountDrainTask);
        await Stage(ExitCleanupStage.DropsPageCleanup, () => _dropsPage.DisposeAsync().AsTask());
        await Stage(ExitCleanupStage.DropsHostShutdown, () => _vm.DropsHost.DisposeAsync().AsTask());
        async Task Warning(Action action)
        {
            await _exitLifecycleCoordinator.RunStageAsync(ExitCleanupStage.NonCriticalCleanup, () =>
            { action(); return Task.CompletedTask; }, critical: false);
        }
        await Warning(_diagnosticsPage.Dispose);
        await Warning(_announcementService.Dispose);
        _vm.RegionSwitchCompleted -= OnRegionSwitchCompleted;
        _vm.DropsHost.EventReceived -= OnDropsEventForNotification;
        await Warning(_dropsNotificationGate.Dispose);
        await Warning(_notificationService.Dispose);
        await Warning(_vm.CloudHttpClients.Dispose);
        if (!_updateCancellationDisposed)
        {
            _updateCancellation.Dispose();
            _updateCancellationDisposed = true;
        }
        if (CleanupFailureForSelfTest is { } injectWarning)
            await _exitLifecycleCoordinator.RunStageAsync(ExitCleanupStage.NonCriticalCleanup,
                () => injectWarning(ExitCleanupStage.NonCriticalCleanup), critical: false);
        await Stage(ExitCleanupStage.SettingsPersistence, () =>
        {
            if (!PersistSettingsBeforeUpdate()) throw new IOException("退出时设置无法保存。");
            return Task.CompletedTask;
        });
        foreach (var result in _exitLifecycleCoordinator.Stages.Where(stage => stage.Error is not null))
            RecordExitError(result.Stage.ToString(), result.Error!);
    }

    private static void RecordExitError(string stage, Exception error)
    {
        try
        {
            Directory.CreateDirectory(AppPaths.Current.LogsDir);
            File.AppendAllText(Path.Combine(AppPaths.Current.LogsDir, "exit-cleanup.log"),
                $"{DateTimeOffset.UtcNow:O} {stage}: {SensitiveDataRedactor.Redact(error.ToString())}\n");
        }
        catch { }
    }

    // Kept as a small compatibility shim for the UI self-test's reflection
    // hook.  It starts the asynchronous path; it must never block Dispatcher.
    private void CompleteExitCleanup()
    {
        BeginExit();
        StartExitCleanup();
    }

    private void DisposeTray()
    {
        var tray = _tray;
        var menu = _trayMenu;
        _tray = null;
        _trayMenu = null;
        if (tray is not null)
        {
            tray.DoubleClick -= OnTrayDoubleClick;
            tray.ContextMenuStrip = null;
            tray.Visible = false;
            tray.Dispose();
        }
        if (menu is not null)
        {
            foreach (var item in menu.Items.OfType<System.Windows.Forms.ToolStripMenuItem>())
            {
                item.Click -= OnTrayOpen;
                item.Click -= OnTrayExit;
            }
            menu.Dispose();
        }
    }

    private void DisposeShowSignal()
    {
        if (_showSignalDisposed) return;
        _showSignalDisposed = true;
        _showRegistration.Unregister(null);
        _showEvent.Dispose();
    }
}
