using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using CloudLightBlizzard.Models;
using CloudLightBlizzard.Services;
using CloudLightBlizzard.Services.Overwatch;
using CloudLightBlizzard.Services.Drops;
using CloudLightBlizzard.ViewModels;

namespace CloudLightBlizzard.Views.Pages;

public partial class SettingsPage : UserControl
{
    public event EventHandler? AnnouncementBadgeSettingChanged;
    private MainViewModel? _vm;
    private bool _loading = true;
    private NetworkDiagnosticReport? _lastNetworkDiagnostic;
    private CancellationTokenSource? _networkDiagnosticCancellation;
    private UpdateCheckResult? _availableUpdate;
    private bool _isDownloadingUpdate;
    private CancellationTokenSource? _updateCts;
    private bool _isUpdateRunning;
    private bool _installerStarted;

    public SettingsPage()
    {
        InitializeComponent();
        Unloaded += OnUnloaded;
    }

    private void OnUnloaded(object sender, RoutedEventArgs e) => CancelUpdateDownload();

    internal void CancelUpdateDownload()
    {
        if (_isUpdateRunning && !_installerStarted)
            _updateCts?.Cancel();
    }
    public void Initialize(MainViewModel vm)
    {
        _vm = vm; CloseToTrayBox.IsChecked = vm.Settings.CloseToTray; StartMinimizedBox.IsChecked = vm.Settings.StartMinimized;
        StartupBox.IsChecked = StartupService.IsEnabled(); DarkModeBox.IsChecked = vm.Settings.DarkMode;
        EnableProxyBox.IsChecked = vm.Settings.EnableProxy;
        ProxyUrlBox.Text = vm.Settings.ProxyUrl;
        FallbackDirectBox.IsChecked = vm.Settings.FallbackDirect;
        AnnouncementBadgeBox.IsChecked = vm.Settings.ShowAnnouncementBadge;
        InitializeNotificationSettings(vm);
        FeedbackPanel.Initialize(vm.Settings, vm.FeedbackService);
        ProxyNoticeText.Text = "公告、反馈和更新会在下一次请求时读取当前代理；Chrome / Brave 正在观看时需重新启动观看窗口后生效。";
        DataPathText.Text = AppPaths.Current.Root;
        CacheText.Text = "打开设置页后统计";
        IsVisibleChanged += async (_, _) =>
        {
            if (!IsVisible) return;
            await Task.Yield();
            await RefreshCacheSizeAsync();
        };
        vm.UpdateChecks.CheckingChanged += OnUpdateCheckingChanged;
        RefreshUpdateInfo();
        Refresh(); _loading = false;
    }

    public void RefreshUpdateInfo()
    {
        if (_vm == null) return;
        CurrentVersionText.Text = _vm.UpdateChecks.CurrentVersion;
        SelectTag(UpdateChannelPicker, _vm.Settings.UpdateChannel.ToString());
        var skipped = UpdateService.NormalizeReleaseVersion(_vm.Settings.SkippedUpdateVersion);
        SkippedUpdatePanel.Visibility = string.IsNullOrWhiteSpace(skipped)
            ? Visibility.Collapsed : Visibility.Visible;
        SkippedVersionText.Text = skipped ?? "";
        var lastCheck = _vm.Settings.LastUpdateCheckAt is { } check
            ? check.ToLocalTime().ToString("yyyy-MM-dd HH:mm") : "暂无";
        var lastUpdate = _vm.Settings.LastSuccessfulUpdateAt is { } updated &&
                         !string.IsNullOrWhiteSpace(_vm.Settings.LastSuccessfulUpdateFrom) &&
                         !string.IsNullOrWhiteSpace(_vm.Settings.LastSuccessfulUpdateTo)
            ? $"最近一次更新：{_vm.Settings.LastSuccessfulUpdateFrom} → {_vm.Settings.LastSuccessfulUpdateTo} · 成功 · {updated.ToLocalTime():yyyy-MM-dd HH:mm}"
            : "最近一次更新：暂无更新记录";
        UpdateHistoryText.Text = $"{lastUpdate}\n最近一次检查：{lastCheck}";
        UpdateFailureText.Text = string.IsNullOrWhiteSpace(_vm.Settings.LastUpdateFailure)
            ? "" : $"最近一次失败：{_vm.Settings.LastUpdateFailure}";
        UpdateCheckingState();
    }

    internal void InitializeNotificationSettings(MainViewModel vm)
    {
        _vm = vm;
        _loading = true;
        EnableWindowsNotificationsBox.IsChecked = vm.Settings.EnableWindowsNotifications;
        NotifyRegionSwitchBox.IsChecked = vm.Settings.NotifyRegionSwitch;
        NotifyDropsBox.IsChecked = vm.Settings.NotifyDrops;
        NotifyUpdatesBox.IsChecked = vm.Settings.NotifyUpdates;
        NotifyAnnouncementsBox.IsChecked = vm.Settings.NotifyAnnouncements;
        _loading = false;
    }

    internal void FocusUpdateSection()
    {
        Dispatcher.BeginInvoke(new Action(() =>
        {
            UpdateSettingsCard.BringIntoView();
            CheckUpdateButton.Focus();
        }), System.Windows.Threading.DispatcherPriority.Loaded);
    }
    public void FocusProxySection()
    {
        Dispatcher.BeginInvoke(new Action(() =>
        {
            NetworkProxyCard.BringIntoView();
            EnableProxyBox.Focus();
        }), System.Windows.Threading.DispatcherPriority.Loaded);
    }
    private void Refresh()
    {
        if (_vm == null) return; ExePathText.Text = _vm.Settings.ClientExe ?? "自动检测（未手动指定）";
    }
    private async Task RefreshCacheSizeAsync()
    {
        var bytes = await Task.Run(OwImageCache.CacheSizeBytes);
        CacheText.Text = bytes >= 1024 * 1024 ? $"当前缓存约 {bytes / 1024d / 1024:0.0} MB" : $"当前缓存约 {bytes / 1024d:0.0} KB";
    }
    private void OnSettingChanged(object sender, RoutedEventArgs e) => Save();
    private void OnAnnouncementBadgeChanged(object sender, RoutedEventArgs e)
    {
        if (_loading || _vm == null) return;
        _vm.Settings.ShowAnnouncementBadge = AnnouncementBadgeBox.IsChecked == true;
        _vm.Settings.Save();
        AnnouncementBadgeSettingChanged?.Invoke(this, EventArgs.Empty);
    }
    private void OnNotificationSettingChanged(object sender, RoutedEventArgs e)
    {
        if (_loading || _vm == null) return;
        _vm.Settings.EnableWindowsNotifications = EnableWindowsNotificationsBox.IsChecked == true;
        _vm.Settings.NotifyRegionSwitch = NotifyRegionSwitchBox.IsChecked == true;
        _vm.Settings.NotifyDrops = NotifyDropsBox.IsChecked == true;
        _vm.Settings.NotifyUpdates = NotifyUpdatesBox.IsChecked == true;
        _vm.Settings.NotifyAnnouncements = NotifyAnnouncementsBox.IsChecked == true;
        _vm.Settings.Save();
    }

    private void OnUpdateChannelChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_loading || _vm == null || UpdateChannelPicker.SelectedItem is not ComboBoxItem item) return;
        if (!Enum.TryParse<UpdateChannel>(item.Tag?.ToString(), out var channel)) return;
        _vm.Settings.UpdateChannel = channel;
        _vm.Settings.Save();
        RefreshUpdateInfo();
    }
    private void OnThemeChanged(object sender, RoutedEventArgs e) { if (_loading || _vm == null) return; _vm.Settings.DarkMode = DarkModeBox.IsChecked == true; _vm.Settings.Save(); ThemeManager.Apply(_vm.Settings.DarkMode); }
    private void Save()
    {
        if (_loading || _vm == null) return; _vm.Settings.CloseToTray = CloseToTrayBox.IsChecked == true; _vm.Settings.StartMinimized = StartMinimizedBox.IsChecked == true; _vm.Settings.Save(); StartupService.SetEnabled(StartupBox.IsChecked == true);
    }
    private void OnChooseExe(object sender, RoutedEventArgs e) { _vm?.SetExePath(); Refresh(); }
    private void OnAutoDetect(object sender, RoutedEventArgs e) { if (_vm == null) return; _vm.Settings.ClientExe = null; _vm.Settings.Save(); Refresh(); }
    private void OnOpenData(object sender, RoutedEventArgs e) { Directory.CreateDirectory(AppPaths.Current.Root); Process.Start(new ProcessStartInfo { FileName = AppPaths.Current.Root, UseShellExecute = true }); }
    private void OnClearCache(object sender, RoutedEventArgs e) { OwImageCache.ClearCache(); Refresh(); }

    private void OnProxyInputChanged(object sender, RoutedEventArgs e)
    {
        if (_loading) return;
        ProxyNoticeText.Text = EnableProxyBox.IsChecked == true &&
                               !ProxyValidator.TryNormalize(ProxyUrlBox.Text, out _, out var error)
            ? error
            : "代理设置用于公告、反馈、软件更新和掉宝网络请求；本机 Chrome DevTools 始终直连。";
    }

    private async void OnSaveProxy(object sender, RoutedEventArgs e)
    {
        if (_vm == null) return;
        var enabled = EnableProxyBox.IsChecked == true;
        var url = ProxyUrlBox.Text.Trim();
        if (enabled && !ProxyValidator.TryNormalize(url, out url, out var error))
        {
            ProxyNoticeText.Text = error;
            MessageBox.Show(error, "网络代理", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        _vm.Settings.EnableProxy = enabled;
        _vm.Settings.ProxyUrl = url;
        _vm.Settings.FallbackDirect = FallbackDirectBox.IsChecked == true;
        _vm.Settings.Save();
        var settings = new DropsProxySettings(enabled, url, _vm.Settings.FallbackDirect);
        _vm.DropsHost.ConfigureProxy(settings);
        await _vm.DropsHost.ApplyProxyAsync(settings);
        ProxyNoticeText.Text = enabled
            ? "代理已应用；公告、反馈和更新将在下一次请求时使用。正在运行的 YouTube 浏览器需重新启动观看窗口后生效。"
            : "网络代理已关闭，下一次云服务请求将使用直连。";
        _lastNetworkDiagnostic = null;
        CopyDiagnosticButton.IsEnabled = false;
        NetworkDiagnosticPanel.Visibility = Visibility.Collapsed;
    }

    private async void OnTestNetwork(object sender, RoutedEventArgs e)
    {
        if (_vm == null) return;
        _networkDiagnosticCancellation?.Cancel();
        var cancellation = new CancellationTokenSource();
        _networkDiagnosticCancellation = cancellation;
        NetworkTestButton.IsEnabled = false;
        NetworkTestButton.Content = "正在测试…";
        CopyDiagnosticButton.IsEnabled = false;
        NetworkDiagnosticPanel.Visibility = Visibility.Visible;
        NetworkDiagnosticText.Text = "正在使用已保存的代理设置测试正式网络路径……";
        try
        {
            var diagnostics = new NetworkDiagnosticService(_vm.Settings, _vm.CloudHttpClients);
            _lastNetworkDiagnostic = await diagnostics.RunAsync(cancellation.Token);
            NetworkDiagnosticText.Text = _lastNetworkDiagnostic.ToDisplayText();
            CopyDiagnosticButton.IsEnabled = true;
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
        {
            NetworkDiagnosticText.Text = "网络诊断已取消。";
        }
        finally
        {
            if (ReferenceEquals(_networkDiagnosticCancellation, cancellation))
                _networkDiagnosticCancellation = null;
            cancellation.Dispose();
            NetworkTestButton.IsEnabled = true;
            NetworkTestButton.Content = "测试连接";
        }
    }

    private void OnCopyDiagnostic(object sender, RoutedEventArgs e)
    {
        if (_vm == null) return;
        var diagnostics = new NetworkDiagnosticService(_vm.Settings, _vm.CloudHttpClients);
        var context = new RuntimeDiagnosticContext(
            _vm.UpdateChecks.CurrentVersion,
            _vm.BattleNetPathValid,
            _vm.OverwatchPathValid,
            _vm.RegionGuide.CurrentFileText,
            _vm.RegionGuide.BackupModeText,
            _vm.RegionGuide.BackupStateText,
            _vm.GetDropsDiagnosticSnapshot());
        try
        {
            Clipboard.SetText(diagnostics.BuildCopyText(context, _lastNetworkDiagnostic));
            ProxyNoticeText.Text = "诊断摘要已复制；凭据、用户目录和代理认证信息已自动脱敏。";
        }
        catch
        {
            ProxyNoticeText.Text = "无法写入剪贴板，请稍后重试。";
        }
    }

    private async void OnCheckUpdate(object sender, RoutedEventArgs e)
    {
        if (_vm == null || _vm.UpdateChecks.IsChecking || _isDownloadingUpdate) return;
        try
        {
            var outcome = await _vm.UpdateChecks.CheckAsync(UpdateCheckMode.Manual);
            if (outcome.Kind == UpdateCheckOutcomeKind.UpdateAvailable && outcome.Result is { } result)
            {
                _availableUpdate = result;
                UpdateAvailablePanel.Visibility = Visibility.Visible;
                UpdateAvailableText.Text = $"发现新版本 {result.LatestVersion}";
                UpdateDownloadPanel.Visibility = Visibility.Collapsed;
                OpenUpdateLinkButton.IsEnabled = !string.IsNullOrWhiteSpace(result.ReleaseUrl);
                var canDownload = CanDownloadInstaller(result);
                OnlineUpdateButton.IsEnabled = canDownload;
                InstallerValidationText.Visibility = !string.IsNullOrWhiteSpace(result.InstallerDownloadUrl) && !canDownload
                    ? Visibility.Visible : Visibility.Collapsed;
                InstallerValidationText.Text = "在线安装已禁用：更新服务未提供有效 SHA-256 摘要，请打开 Release 页面手动核对。";
            }
            else
            {
                ClearAvailableUpdate();
                if (outcome.Kind == UpdateCheckOutcomeKind.UpToDate)
                    MessageBox.Show($"当前版本：{_vm.UpdateChecks.CurrentVersion}", "已是最新版本",
                        MessageBoxButton.OK, MessageBoxImage.Information);
                else if (outcome.Kind == UpdateCheckOutcomeKind.NoRelease)
                    MessageBox.Show("当前没有可用的正式版本更新。", "软件更新",
                        MessageBoxButton.OK, MessageBoxImage.Information);
                else if (outcome.Kind == UpdateCheckOutcomeKind.Failed)
                    MessageBox.Show(outcome.Result?.ErrorMessage ?? "暂时无法连接更新服务器。", "暂时无法检查更新",
                        MessageBoxButton.OK, MessageBoxImage.Information);
            }
        }
        catch (OperationCanceledException) { }
        finally
        {
            RefreshUpdateInfo();
        }
    }

    private void OnOpenUpdateLink(object sender, RoutedEventArgs e)
    {
        if (_isDownloadingUpdate || _availableUpdate is null) return;
        if (Window.GetWindow(this) is MainWindow mainWindow)
            mainWindow.OpenUpdateRelease(_availableUpdate.ReleaseUrl);
    }

    private async void OnOnlineUpdate(object sender, RoutedEventArgs e)
    {
        if (_vm == null || _availableUpdate is null || _isDownloadingUpdate ||
            !CanDownloadInstaller(_availableUpdate))
            return;

        var update = _availableUpdate;
        var cts = new CancellationTokenSource();
        _updateCts = cts;
        var token = cts.Token;
        _isUpdateRunning = true;
        _installerStarted = false;
        _isDownloadingUpdate = true;
        UpdateDownloadPanel.Visibility = Visibility.Visible;
        UpdateDownloadProgressBar.Value = 0;
        UpdateDownloadProgressBar.IsIndeterminate = update.InstallerSize <= 0;
        UpdateDownloadText.Text = "正在下载安装包…";
        UpdateCheckingState();

        try
        {
            var progress = new Progress<UpdateDownloadProgress>(RenderUpdateDownloadProgress);
            var path = await _vm.UpdateDownloader.DownloadInstallerAsync(
                update, progress, token);
            token.ThrowIfCancellationRequested();
            UpdateDownloadText.Text = "正在启动安装程序…";
            _isUpdateRunning = false;
            if (Window.GetWindow(this) is MainWindow mainWindow &&
                mainWindow.InstallDownloadedUpdate(path, () =>
                {
                    _installerStarted = true;
                    UpdateDownloadText.Text = "安装程序已启动，正在退出 CloudLight Blizzard…";
                }))
            {
                _vm.RecordSuccessfulUpdate(update);
                return;
            }

            UpdateDownloadText.Text = "更新安装程序启动失败。";
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
            UpdateDownloadText.Text = "下载已取消。";
        }
        catch (Exception ex)
        {
            UpdateDownloadText.Text = "在线更新失败。";
            MessageBox.Show(ex.Message, "在线更新失败", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        finally
        {
            _isUpdateRunning = false;
            if (ReferenceEquals(_updateCts, cts))
                _updateCts = null;
            cts.Dispose();
            if (!_installerStarted && !Dispatcher.HasShutdownStarted)
            {
                _isDownloadingUpdate = false;
                UpdateCheckingState();
            }
        }
    }

    private void RenderUpdateDownloadProgress(UpdateDownloadProgress value)
    {
        if (value.Phase == UpdateDownloadPhase.WaitingRetry)
        {
            UpdateDownloadProgressBar.IsIndeterminate = true;
            var delay = value.RetryDelay is { } retry ? $"{Math.Max(1, (int)Math.Ceiling(retry.TotalSeconds))} 秒后" : "稍后";
            UpdateDownloadText.Text = $"下载中断，{delay}重试（{value.RetryAttempt}/{value.MaxRetries}）";
            return;
        }
        if (value.Phase == UpdateDownloadPhase.Verifying)
        {
            UpdateDownloadProgressBar.IsIndeterminate = false;
            UpdateDownloadProgressBar.Value = value.Percentage ?? 100;
            UpdateDownloadText.Text = "正在校验安装包…";
            return;
        }

        if (value.Percentage is { } percentage)
        {
            UpdateDownloadProgressBar.IsIndeterminate = false;
            UpdateDownloadProgressBar.Value = percentage;
            var total = value.TotalBytes is > 0
                ? $" / {UpdateDownloadService.FormatBytes(value.TotalBytes.Value)}"
                : "";
            UpdateDownloadText.Text =
                $"正在下载：{percentage}% · {UpdateDownloadService.FormatBytes(value.BytesReceived)}{total}";
        }
        else
        {
            UpdateDownloadProgressBar.IsIndeterminate = true;
            UpdateDownloadText.Text =
                $"正在下载：{UpdateDownloadService.FormatBytes(value.BytesReceived)}";
        }
    }

    private void ClearAvailableUpdate()
    {
        _availableUpdate = null;
        UpdateAvailablePanel.Visibility = Visibility.Collapsed;
        UpdateDownloadPanel.Visibility = Visibility.Collapsed;
        InstallerValidationText.Visibility = Visibility.Collapsed;
    }

    private void OnRestoreSkippedUpdate(object sender, RoutedEventArgs e)
    {
        _vm?.UpdateChecks.RestoreSkippedVersion();
        RefreshUpdateInfo();
    }

    private void OnUpdateCheckingChanged()
    {
        if (Dispatcher.HasShutdownStarted) return;
        Dispatcher.BeginInvoke(UpdateCheckingState);
    }

    private void UpdateCheckingState()
    {
        if (_vm == null) return;
        CheckUpdateButton.IsEnabled = !_vm.UpdateChecks.IsChecking && !_isDownloadingUpdate;
        CheckUpdateButton.Content = _vm.UpdateChecks.IsChecking ? "正在检查…" : "检查更新";
        if (_availableUpdate is not null)
        {
            OpenUpdateLinkButton.IsEnabled = !_isDownloadingUpdate &&
                !string.IsNullOrWhiteSpace(_availableUpdate.ReleaseUrl);
            OnlineUpdateButton.IsEnabled = !_isDownloadingUpdate &&
                CanDownloadInstaller(_availableUpdate);
            OnlineUpdateButton.Content = _isDownloadingUpdate ? "正在下载…" : "在线更新";
        }
    }

    private static void SelectTag(ComboBox box, string tag)
    {
        if (box.Items.Count == 0) return;
        box.SelectedItem = box.Items.OfType<ComboBoxItem>()
            .FirstOrDefault(item => string.Equals(item.Tag?.ToString(), tag, StringComparison.OrdinalIgnoreCase))
            ?? box.Items[0];
    }

    private static bool CanDownloadInstaller(UpdateCheckResult result) =>
        !string.IsNullOrWhiteSpace(result.InstallerDownloadUrl) &&
        UpdateService.IsValidSha256Digest(result.InstallerDigest);
}
