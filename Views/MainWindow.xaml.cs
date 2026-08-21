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
    private readonly DropsPage _dropsPage = new();
    private readonly SettingsPage _settingsPage = new();
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
    private bool _pagesReady;
    private bool _initialized;
    private readonly CancellationTokenSource _updateCancellation = new();

    public MainWindow(bool startHidden = false)
    {
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
        if (_vm.Settings.WindowMaximized) WindowState = WindowState.Maximized;

        _accountsPage.Initialize(_vm);
        _regionPage.Initialize(_vm);
        _statsPage.Initialize(_vm);
        _dropsPage.Initialize(_vm);
        _settingsPage.Initialize(_vm);
        _accountsPage.OpenStatsRequested += row => { StatsNav.IsChecked = true; _statsPage.SelectAccount(row); };
        _vm.MainSectionRequested += section => Dispatcher.Invoke(() =>
        {
            if (section == "region") RegionNav.IsChecked = true;
            else if (section == "stats") StatsNav.IsChecked = true;
            else if (section == "drops") DropsNav.IsChecked = true;
            else if (section == "settings") SettingsNav.IsChecked = true;
            else AccountsNav.IsChecked = true;
        });
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
        if (_initialized) return;
        _initialized = true;
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
        _dropsPage.StartAutomaticPlatforms();
        _ = RunAutomaticUpdateCheckAsync();
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
        if (automatic && !IsVisible) return;
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
            case "drops": DropsNav.IsChecked = true; break;
            case "settings": SettingsNav.IsChecked = true; break;
            default: AccountsNav.IsChecked = true; break;
        }
    }

    private async void OnNavigate(object sender, RoutedEventArgs e)
    {
        if (!_pagesReady || sender is not RadioButton { Tag: string section }) return;
        PageHost.Content = section switch { "region" => _regionPage, "stats" => _statsPage, "drops" => _dropsPage, "settings" => _settingsPage, _ => _accountsPage };
        AccountFooter.Visibility = section == "accounts" ? Visibility.Visible : Visibility.Collapsed;
        _vm.Settings.LastMainSection = section;
        _vm.Settings.Save();
        await Dispatcher.Yield(DispatcherPriority.Render);
        if (section == "region") await _regionPage.RefreshAsync();
        if (section == "stats") _statsPage.OnPageOpened();
        if (section == "drops") await _dropsPage.RefreshAsync();
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
            CompleteExitCleanup();
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
            CompleteExitCleanup();
            base.OnClosing(e);
            return;
        }

        BeginExit();
        CompleteExitCleanup();
        base.OnClosing(e);
    }

    private void CancelClosing(CancelEventArgs e)
    {
        e.Cancel = true;
        _exitRequested = false;
        _isClosing = false;
    }

    private void BeginExit()
    {
        _isExiting = true;
        _watchTimer.Stop();
        _updateCancellation.Cancel();
        DisposeTray();
        DisposeShowSignal();
    }

    private void CompleteExitCleanup()
    {
        if (_exitCleanupStarted) return;
        _exitCleanupStarted = true;
        try { _dropsPage.DisposeAsync().AsTask().GetAwaiter().GetResult(); } catch { }
        try { _vm.DropsHost.DisposeAsync().AsTask().GetAwaiter().GetResult(); } catch { }
        _updateCancellation.Dispose();
        if (WindowState != WindowState.Maximized) { _vm.Settings.WindowWidth = ActualWidth; _vm.Settings.WindowHeight = ActualHeight; }
        _vm.Settings.WindowMaximized = WindowState == WindowState.Maximized; _vm.Settings.Save();
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
