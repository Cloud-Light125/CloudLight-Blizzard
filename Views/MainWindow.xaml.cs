using System.ComponentModel;
using System.Drawing;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using CloudLightBlizzard.Services;
using CloudLightBlizzard.ViewModels;
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
            if (StatsNav.IsChecked == true) _statsPage.SelectAccount(_vm.CurrentAccount ?? _vm.SavedAccounts.FirstOrDefault(), false);
            if (RegionNav.IsChecked == true) await _regionPage.RefreshAsync();
            _watchTimer.Tick += async (_, _) => await _vm.PollAccountsAsync();
            _watchTimer.Start();
            if (ShouldStartHidden()) HideToTray();
        };
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
        if (section == "stats") await _statsPage.LoadSelectedAsync();
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
        else _statsPage.LoadChinaRoleId(accountId);
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
        if (WindowState != WindowState.Maximized) { _vm.Settings.WindowWidth = ActualWidth; _vm.Settings.WindowHeight = ActualHeight; }
        _vm.Settings.WindowMaximized = WindowState == WindowState.Maximized; _vm.Settings.Save();
        if (_tray != null) { _tray.Visible = false; _tray.Dispose(); }
        _showRegistration.Unregister(null);
        _showEvent.Dispose();
        base.OnClosing(e);
    }
}
