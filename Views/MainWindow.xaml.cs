using System.ComponentModel;
using System.Diagnostics;
using System.Drawing;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using CloudLightBlizzard.Models;
using CloudLightBlizzard.Services;
using CloudLightBlizzard.ViewModels;
using CloudLightBlizzard.Views;
using CloudLightBlizzard.Views.Pages;

namespace CloudLightBlizzard;

public partial class MainWindow : Window
{
    private readonly MainViewModel _vm = new();
    private readonly AccountsPage _accountsPage = new();
    private readonly RegionFilesPage _regionPage = new();
    private readonly StatsPage _statsPage = new();
    private readonly SettingsPage _settingsPage = new();
    private readonly System.Windows.Threading.DispatcherTimer _watchTimer = new() { Interval = TimeSpan.FromSeconds(2) };
    private readonly EventWaitHandle _showEvent;
    private readonly RegisteredWaitHandle _showRegistration;
    private System.Windows.Forms.NotifyIcon? _tray;
    private bool _reallyExit;
    private bool _pagesReady;
    private readonly CancellationTokenSource _updateCancellation = new();

    public MainWindow()
    {
        InitializeComponent();
        ThemeManager.Attach(this);
        _showEvent = new EventWaitHandle(false, EventResetMode.AutoReset, App.ShowEventName);
        _showRegistration = ThreadPool.RegisterWaitForSingleObject(
            _showEvent,
            (_, _) => Dispatcher.BeginInvoke(new Action(ShowFromTray)),
            null,
            Timeout.Infinite,
            false);
        DataContext = _vm;
        Width = _vm.Settings.WindowWidth >= MinWidth ? _vm.Settings.WindowWidth : Width;
        Height = _vm.Settings.WindowHeight >= MinHeight ? _vm.Settings.WindowHeight : Height;
        if (_vm.Settings.WindowMaximized) WindowState = WindowState.Maximized;

        _accountsPage.Initialize(_vm);
        _regionPage.Initialize(_vm);
        _statsPage.Initialize(_vm);
        _settingsPage.Initialize(_vm);
        _accountsPage.OpenStatsRequested += row => { StatsNav.IsChecked = true; _statsPage.SelectAccount(row); };
        _vm.MainSectionRequested += section => Dispatcher.Invoke(() =>
        {
            if (section == "region") RegionNav.IsChecked = true;
            else if (section == "stats") StatsNav.IsChecked = true;
            else if (section == "settings") SettingsNav.IsChecked = true;
            else AccountsNav.IsChecked = true;
        });
        _pagesReady = true;
        SelectSavedSection();

        SetupTray();
        Loaded += async (_, _) =>
        {
            // 先让窗口完成首帧，再开始账号与区服的磁盘读取。
            await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
            await _vm.RefreshAsync();
            var args = Environment.GetCommandLineArgs();
            var demoIndex = Array.FindIndex(args, value => string.Equals(value, "--accountlayoutdemo", StringComparison.OrdinalIgnoreCase));
            if (demoIndex >= 0 && demoIndex + 1 < args.Length && int.TryParse(args[demoIndex + 1], out var demoCount))
            {
                _vm.ApplyAccountLayoutDemo(demoCount);
                AccountsNav.IsChecked = true;
            }
            if (StatsNav.IsChecked == true) _statsPage.SelectAccount(_vm.CurrentAccount ?? _vm.SavedAccounts.FirstOrDefault());
            if (RegionNav.IsChecked == true) await _regionPage.RefreshAsync();
            _watchTimer.Tick += async (_, _) => await _vm.PollAccountsAsync();
            _watchTimer.Start();
            if (ShouldStartHidden()) HideToTray();
            _ = RunAutomaticUpdateCheckAsync();
        };
    }

    private async Task RunAutomaticUpdateCheckAsync()
    {
        try
        {
            var outcome = await _vm.UpdateChecks.CheckAfterDelayAsync(
                TimeSpan.FromSeconds(3), _updateCancellation.Token);
            PresentUpdateOutcome(outcome, automatic: true);
        }
        catch (OperationCanceledException) { }
    }

    internal async Task CheckForUpdatesManuallyAsync()
    {
        try
        {
            var outcome = await _vm.UpdateChecks.CheckAsync(UpdateCheckMode.Manual, _updateCancellation.Token);
            PresentUpdateOutcome(outcome, automatic: false);
        }
        catch (OperationCanceledException) { }
    }

    private void PresentUpdateOutcome(UpdateCheckOutcome outcome, bool automatic)
    {
        if (outcome.Kind == UpdateCheckOutcomeKind.UpdateAvailable && outcome.Result is { } result)
        {
            var dialog = new UpdateDialog(result) { Owner = this };
            dialog.ShowDialog();
            if (dialog.SkipVersion) _vm.UpdateChecks.SkipVersion(result.LatestVersion);
            _settingsPage.RefreshUpdateInfo();
            if (dialog.Action == UpdateDialogAction.OpenRelease && !string.IsNullOrWhiteSpace(result.ReleaseUrl))
            {
                try
                {
                    Process.Start(new ProcessStartInfo { FileName = result.ReleaseUrl, UseShellExecute = true });
                }
                catch
                {
                    MessageBox.Show("无法打开系统浏览器，请稍后重试。", "前往更新",
                        MessageBoxButton.OK, MessageBoxImage.Information);
                }
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
            MessageBox.Show("请检查网络连接后重试。", "暂时无法检查更新",
                MessageBoxButton.OK, MessageBoxImage.Information);
    }

    private void SelectSavedSection()
    {
        switch (_vm.Settings.LastMainSection)
        {
            case "region": RegionNav.IsChecked = true; break;
            case "stats": StatsNav.IsChecked = true; break;
            case "settings": SettingsNav.IsChecked = true; break;
            default: AccountsNav.IsChecked = true; break;
        }
    }

    private async void OnNavigate(object sender, RoutedEventArgs e)
    {
        if (!_pagesReady || sender is not RadioButton { Tag: string section }) return;
        PageHost.Content = section switch { "region" => _regionPage, "stats" => _statsPage, "settings" => _settingsPage, _ => _accountsPage };
        _vm.Settings.LastMainSection = section;
        _vm.Settings.Save();
        await Dispatcher.Yield(DispatcherPriority.Render);
        if (section == "region") await _regionPage.RefreshAsync();
        if (section == "stats") _statsPage.OnPageOpened();
    }

    private bool ShouldStartHidden()
    {
        var args = Environment.GetCommandLineArgs();
        if (args.Any(a => string.Equals(a, "--visible", StringComparison.OrdinalIgnoreCase))) return false;
        return args.Any(a => string.Equals(a, "--tray", StringComparison.OrdinalIgnoreCase)) || _vm.Settings.StartMinimized;
    }
    private async void OnRefresh(object sender, RoutedEventArgs e) => await _vm.RefreshAsync();

    public void OpenStatsAccount(long accountId)
    {
        StatsNav.IsChecked = true;
        var saved = _vm.SavedAccounts.FirstOrDefault(account => account.AccountId == accountId);
        if (saved != null) _statsPage.SelectAccount(saved);
        else _statsPage.SelectAccount(null);
    }

    private void SetupTray()
    {
        _tray = new System.Windows.Forms.NotifyIcon { Text = "CloudLight Blizzard", Visible = true, Icon = LoadTrayIcon() };
        _tray.DoubleClick += (_, _) => ShowFromTray();
        _tray.ContextMenuStrip = TrayMenuFactory.Create(("打开 CloudLight Blizzard", (_, _) => ShowFromTray()), ("-", null), ("退出", (_, _) => ExitApp()));
    }
    private static Icon? LoadTrayIcon()
    {
        try { var info = Application.GetResourceStream(new Uri("pack://application:,,,/Assets/app.ico")); return info?.Stream is { } stream ? new Icon(stream) : null; }
        catch { return null; }
    }
    private void ShowFromTray() { Show(); ShowInTaskbar = true; WindowState = WindowState.Normal; Activate(); }
    private void HideToTray() { Hide(); ShowInTaskbar = false; }
    private void ExitApp() { _reallyExit = true; Close(); }

    protected override void OnClosing(CancelEventArgs e)
    {
        if (!_reallyExit && _vm.Settings.CloseToTray) { e.Cancel = true; HideToTray(); return; }
        _watchTimer.Stop();
        _updateCancellation.Cancel();
        _updateCancellation.Dispose();
        if (WindowState != WindowState.Maximized) { _vm.Settings.WindowWidth = ActualWidth; _vm.Settings.WindowHeight = ActualHeight; }
        _vm.Settings.WindowMaximized = WindowState == WindowState.Maximized; _vm.Settings.Save();
        if (_tray != null) { _tray.Visible = false; _tray.Dispose(); }
        _showRegistration.Unregister(null);
        _showEvent.Dispose();
        base.OnClosing(e);
    }
}
