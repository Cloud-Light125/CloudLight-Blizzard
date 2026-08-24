using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
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
    public SettingsPage() => InitializeComponent();
    public void Initialize(MainViewModel vm)
    {
        _vm = vm; CloseToTrayBox.IsChecked = vm.Settings.CloseToTray; StartMinimizedBox.IsChecked = vm.Settings.StartMinimized;
        StartupBox.IsChecked = StartupService.IsEnabled(); DarkModeBox.IsChecked = vm.Settings.DarkMode;
        EnableProxyBox.IsChecked = vm.Settings.EnableProxy;
        ProxyUrlBox.Text = vm.Settings.ProxyUrl;
        FallbackDirectBox.IsChecked = vm.Settings.FallbackDirect;
        AnnouncementBadgeBox.IsChecked = vm.Settings.ShowAnnouncementBadge;
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
        var skipped = UpdateService.NormalizeVersion(_vm.Settings.SkippedUpdateVersion);
        SkippedUpdatePanel.Visibility = string.IsNullOrWhiteSpace(skipped)
            ? Visibility.Collapsed : Visibility.Visible;
        SkippedVersionText.Text = skipped ?? "";
        UpdateCheckingState();
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
        _networkDiagnosticCancellation?.Dispose();
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
        if (_vm == null || _vm.UpdateChecks.IsChecking) return;
        if (Window.GetWindow(this) is MainWindow mainWindow)
            await mainWindow.CheckForUpdatesManuallyAsync();
        RefreshUpdateInfo();
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
        CheckUpdateButton.IsEnabled = !_vm.UpdateChecks.IsChecking;
        CheckUpdateButton.Content = _vm.UpdateChecks.IsChecking ? "正在检查…" : "检查更新";
    }
}
