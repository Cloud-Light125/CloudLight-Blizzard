using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using CloudLightBlizzard.Services.Diagnostics;
using CloudLightBlizzard.Services.Drops;
using CloudLightBlizzard.ViewModels;
using CloudLightBlizzard.Views.Pages;

namespace CloudLightBlizzard.Services;

/// <summary>Opt-in, isolated presentation acceptance. Never loads user accounts or starts a worker.</summary>
internal sealed class UiRedesignReview
{
    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern int SetCurrentProcessExplicitAppUserModelID(string id);
    private readonly string _output;
    private readonly MainWindow _window;
    private readonly MainViewModel _main;
    private readonly RegionPreparationGuide _defaultRegionGuide;
    private readonly DropsPage _page;
    private readonly DropsViewModel _drops;
    private readonly Dictionary<DropsPlatform, JsonElement> _states = new();
    private readonly List<string> _checks = [];
    private readonly BindingListener _bindings = new();
    private readonly ComboBox _scenarios = new() { MinWidth = 240, Margin = new Thickness(16, 0, 8, 0) };
    private readonly bool _automated;
    internal static readonly string[] Scenarios = [
        "01 总览", "02 Battle.net 账号管理", "03 区服切换", "04 战绩", "05 区服快照",
        "06 Twitch 未登录", "07 Twitch 设备授权", "08 Twitch 已登录", "09 Twitch 正在观看", "10 Twitch 完整设置",
        "11 SOOP 多账号", "12 SOOP 设置", "13 Bilibili 登录", "14 Bilibili 设置", "15 YouTube", "16 诊断中心", "17 全局设置",
        "18 Twitch 配置损坏", "19 Twitch 网络失败", "20 SOOP 无账号", "21 SOOP 单账号停止", "22 Bilibili 二维码过期",
        "23 Bilibili 已登录", "24 YouTube 浏览器不可用", "25 Worker 崩溃", "26 代理不可用", "27 账号切换失败", "28 区服切换失败",
        "29 Twitch 活动与奖励", "30 Twitch 账号维护", "31 Bilibili 活动任务", "32 Twitch 网络与维护", "33 运行日志", "34 Twitch 游戏优先级", "35 Twitch 自动化",
        "36 SOOP 刷新与恢复", "37 Bilibili 自动化与通知", "38 YouTube 浏览器配置", "39 全局网络代理", "40 账号备份与存储",
    ];

    private static T Field<T>(object target, string name) => (T)target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(target)!;
    private static JsonElement Json(object value) => JsonSerializer.SerializeToElement(value);
    private static JsonElement Raw(string value) => JsonDocument.Parse(value).RootElement.Clone();
    internal static void Open(string output, bool automated, int initialScenario = 0)
    {
        // Prevent inheriting the launcher AUMID: screenshot/UI tooling must target this process.
        SetCurrentProcessExplicitAppUserModelID("CloudLightBlizzard.IsolatedUiReview");
        var review = new UiRedesignReview(output, automated, initialScenario);
        review._window.Show();
    }
    private UiRedesignReview(string output, bool automated, int initialScenario)
    {
        _output = Path.GetFullPath(output);
        var userRoot = AppPaths.Current.Root.TrimEnd(Path.DirectorySeparatorChar);
        if (_output.Equals(userRoot, StringComparison.OrdinalIgnoreCase) ||
            _output.StartsWith(userRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) ||
            userRoot.StartsWith(_output + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("UI 验收必须使用与用户数据分离的目录。");
        Directory.CreateDirectory(_output);
        CreateSimulatedQr(Path.Combine(_output, "simulated-qr.png"));
        AppPaths.UseSelfTestRoot(Path.Combine(_output, "isolated-data"));
        _automated = automated;
        var paths = new BattleNetPaths(Path.Combine(_output, "isolated-battle-local"), Path.Combine(_output, "isolated-battle-roaming")) { ClientExe = null };
        _window = new MainWindow(startHidden: true, settings: new AppSettings { LastMainSection = "overview", CloseToTray = false }, paths: paths, isolatedReview: true);
        Application.Current.MainWindow = _window;
        _window.Title = "CloudLight Blizzard · 隔离界面验收（模拟数据）";
        _main = Field<MainViewModel>(_window, "_vm");
        _defaultRegionGuide = _main.RegionGuide;
        _page = Field<DropsPage>(_window, "_dropsPage");
        _drops = (DropsViewModel)_page.DataContext;
        _main.DropsHost.ConfigureRequestOverrideForSelfTest((platform, command, payload, token) =>
        {
            _checks.Add($"mock-request {platform}/{command}");
            return Task.FromResult(_states.GetValueOrDefault(platform, Json(new { })));
        });
        _main.ApplyAccountLayoutDemo(3);
        _main.GameRegionTitle = "尚未识别游戏目录";
        _main.GameRegionSummary = "隔离环境未连接游戏目录，可通过区服页面检查设置。";
        _main.StatusText = "隔离验收：账号、授权和奖励均为模拟数据。";
        _drops.UpdateProxySettings(new DropsProxySettings(true, "http://127.0.0.1:7897", false));
        var root = (UIElement)_window.Content;
        _window.Content = null;
        var shell = new Grid();
        shell.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });shell.RowDefinitions.Add(new RowDefinition());
        var banner = new Border { Background = (Brush)_window.FindResource("AccSoft"), Padding = new Thickness(14, 8, 14, 8) };
        banner.SetResourceReference(Border.BackgroundProperty, "AccSoft");
        _scenarios.SetResourceReference(FrameworkElement.StyleProperty, "ThemeComboBox");
        var controls = new WrapPanel { VerticalAlignment = VerticalAlignment.Center };
        controls.Children.Add(new TextBlock { Text = "隔离验收 · 模拟数据", FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center });
        _scenarios.ItemsSource = Scenarios;
        controls.Children.Add(_scenarios);
        var next = new Button { Content = "下一场景", Style = (Style)_window.FindResource("SecondaryButton"), Padding = new Thickness(12, 0, 12, 0) };
        next.Click += (_, _) => _scenarios.SelectedIndex = (_scenarios.SelectedIndex + 1) % Scenarios.Length;
        controls.Children.Add(next);
        var theme = new Button { Content = "切换深浅色", Style = (Style)_window.FindResource("SecondaryButton"), Margin = new Thickness(8, 0, 0, 0), Padding = new Thickness(12, 0, 12, 0) };
        theme.Click += (_, _) => ThemeManager.Apply(!ThemeManager.IsDark);controls.Children.Add(theme);
        banner.Child = controls;shell.Children.Add(banner);Grid.SetRow(root, 1);shell.Children.Add(root);_window.Content = shell;
        PresentationTraceSources.DataBindingSource.Switch.Level = SourceLevels.Error;
        PresentationTraceSources.DataBindingSource.Listeners.Add(_bindings);
        _scenarios.SelectionChanged += (_, _) => Apply(_scenarios.SelectedIndex);
        _window.Loaded += async (_, _) =>
        {
            _scenarios.SelectedIndex = Math.Clamp(initialScenario, 0, Scenarios.Length-1);
            if (_automated) await RunMatrixAsync();
        };
        _window.Closed += (_, _) =>
        {
            PresentationTraceSources.DataBindingSource.Listeners.Remove(_bindings);
            File.WriteAllLines(Path.Combine(_output, "binding-errors.txt"), _bindings.Errors);
            File.WriteAllLines(Path.Combine(_output, "review-requests.txt"), _checks);
        };
    }

    private JsonElement Twitch(bool loggedIn = false, bool running = false) => Json(new {
        coreAvailable = true, running, authState = loggedIn ? "logged_in" : "logged_out", status = running ? "正在观看" : loggedIn ? "已登录，未开始" : "尚未登录",
        accounts = loggedIn ? new[] { new { userId = "模拟账号 · review-user", loggedIn = true } } : [],
        settings = new { language = "简体中文", priority_mode = "PRIORITY_ONLY", connection_quality = 1, tray_notifications = true, auto_claim_drops = false, priority = new[] { "Overwatch 2" }, exclude = Array.Empty<string>() },
        campaigns = loggedIn ? new[] { new { id = "review-campaign", name = "模拟活动 · 守望先锋", game = "Overwatch 2", available = true, status = "ACTIVE", drops = new[] { new { id = "reward1", name = "模拟奖励", requiredMinutes = 60, currentMinutes = 18, progress = 0.3 } } } } : [],
        inventory = loggedIn ? new[] { new { id = "reward1", name = "模拟奖励 · 仅用于界面验收", campaign = "模拟活动 · 守望先锋", requiredMinutes = 60, currentMinutes = 18, progress = 0.3, completed = false, claimed = false, canClaim = false } } : [],
        channels = loggedIn ? new[] { new { id = "review-channel", name = "模拟频道 · Overwatch", game = "Overwatch 2", dropsEnabled = true, online = true } } : [],
        currentChannel = running ? new { id = "review-channel", name = "模拟频道 · Overwatch", game = "Overwatch 2", dropsEnabled = true, online = true, url = "https://www.twitch.tv/example" } : null,
        inventorySource = loggedIn ? "remote" : "none", lastInventorySyncAt = loggedIn ? DateTimeOffset.UtcNow.ToString("O") : "",
        watchRequests = new { lastResult = running ? "accepted" : "none", attempts = running ? 1 : 0 },
        currentDrop = running ? new { id = "reward1", campaign = "模拟活动", name = "模拟奖励", currentMinutes = 18, requiredMinutes = 60, progress = 0.3, estimatedExtraMinutes = 2 } : null,
    });
    private void Apply(int index)
    {
        if (index < 0) return;
        _main.StatusText = "隔离验收：账号、授权和奖励均为模拟数据。";
        if (index != 27) typeof(MainViewModel).GetProperty(nameof(MainViewModel.RegionGuide))!.SetValue(_main, _defaultRegionGuide);
        foreach (var p in Enum.GetValues<DropsPlatform>()) _drops.DismissMessage(p);
        _states[DropsPlatform.Twitch] = Twitch(index is 7 or 8 or 9 or 28 or 29 or 31 or 33 or 34, index == 8);
        _states[DropsPlatform.Soop] = Raw("""{"running":true,"settings":{"channel_mode":"smart","priority_mission_id":"auto","auto_claim_enabled":false,"low_bandwidth_mode":true,"hang_without_missions":true,"mission_poll_interval":90,"inventory_poll_interval":300,"channel_refresh_interval":300,"reauth_retry_interval":300},"accounts":[{"uid":"模拟主账号","primary":true,"loggedIn":true,"running":true,"channelName":"模拟赛事频道","missionTitle":"模拟任务"},{"uid":"模拟副账号","primary":false,"loggedIn":true,"running":false,"channelName":"尚未进入","missionTitle":"等待任务"}]}""");
        _states[DropsPlatform.Bilibili] = Raw("""{"running":false,"loggedIn":false,"status":"尚未登录","settings":{"enabled":true,"watch_mode":"standard","sessions_per_room":1,"auto_claim":false},"rooms":[],"activities":[],"tasks":[],"sessions":[]}""");
        _states[DropsPlatform.YouTube] = Raw("""{"running":false,"status":"尚未开始","profiles":["模拟观看账号"],"config":{"browser":"chrome","mode":"auto","headless":false,"mute":true,"check_interval":300},"channels":[],"history":{"rows":[]},"sessions":[]}""");
        if (index == 6) _states[DropsPlatform.Twitch] = Json(new { coreAvailable = true, running = false, authState = "authorization_required", accounts = Array.Empty<object>(), authRequired = new { code = "CLB-TEST", url = "https://www.twitch.tv/activate", expiresAt = DateTimeOffset.UtcNow.AddMinutes(15).ToString("O") } });
        if (index == 17) _states[DropsPlatform.Twitch] = Json(new { coreAvailable = false, coreErrorKind = "configuration", coreError = "模拟：配置无法解析。已保存的 Session 保留，可备份并恢复配置。", configuration = new { canReset = true }, accounts = Array.Empty<object>() });
        if (index == 18) _states[DropsPlatform.Twitch] = Json(new { coreAvailable = true, running = false, authState = "failed", loginErrorCode = "network_error", loginError = "模拟：连接超时，Session 尚未完成验证。", accounts = new[] { new { userId = "已保存 Session · 模拟", loggedIn = false } } });
        if (index == 19) _states[DropsPlatform.Soop] = Raw("""{"running":false,"accounts":[],"settings":{"channel_mode":"smart"}}""");
        var section = index switch { 0 => "overview", 1 or 26 or 39 => "accounts", 2 or 27 => "region", 3 => "stats", 4 => "snapshots", 15 => "diagnostics", 16 or 38 => "settings", _ => "drops" };
        _window.NavigateTo(section);
        if (section == "drops")
        {
            var platform = index switch { 10 or 11 or 19 or 20 or 35 => DropsPlatform.Soop, 12 or 13 or 21 or 22 or 30 or 36 => DropsPlatform.Bilibili, 14 or 23 or 37 => DropsPlatform.YouTube, _ => DropsPlatform.Twitch };
            _page.SelectPlatform(platform);
            _drops.SelectPlatform(platform);
            _drops.ApplyState(platform, _states[platform]);
            _page.ShowWorkspace(index switch { 9 or 11 or 13 or 37 => "settings", 10 or 20 or 29 => "accounts", 28 or 30 => "activities", 31 => "network", 32 => "logs", 33 => "games", 34 or 36 => "automation", 35 => "advanced", _ => "overview" });
            if (index is 12 or 21)
            {
                _drops.BilibiliDetails.HandleEvent("qr_login", Json(new { state = index == 21 ? "expired" : "waiting_scan", message = index == 21 ? "模拟：二维码已过期，请重新获取。" : "模拟二维码，仅用于布局验收，不可用于登录。", url = "https://example.invalid/isolated-review", imagePath = Path.Combine(_output, "simulated-qr.png"), expiresAt = DateTimeOffset.UtcNow.AddMinutes(3).ToString("O") }));
            }
            if (index == 22) _drops.BilibiliDetails.ApplyState(Raw("""{"loggedIn":true,"account":{"uid":123456,"userName":"模拟 Bilibili 账号"},"running":false}"""));
            if (index == 23) { _drops.YouTube.Status = "浏览器不可用"; _drops.YouTube.Summary = "模拟：未找到 Chrome / Brave，请在观看配置中指定浏览器路径。"; }
            if (index == 24) _drops.ApplyState(DropsPlatform.Twitch, Json(new { coreAvailable = false, coreErrorKind = "worker", coreError = "模拟：后台组件意外结束，请在网络与维护中重新检测。", accounts = Array.Empty<object>() }));
            if (index == 25) _drops.SetTwitchTemporaryNetworkFailure("模拟：代理服务器 127.0.0.1:7897 不可用，Session 已保留。");
        }
        if (index == 26) _main.StatusText = "模拟：账号切换失败，原账号数据保留。请先重新检测 Battle.net。";
        if (index == 27)
        {
            typeof(MainViewModel).GetProperty(nameof(MainViewModel.RegionGuide))!.SetValue(_main, new RegionPreparationGuide {
                State = RegionPreparationState.Error, ShowError = true, Title = "区服切换失败（模拟）",
                BackupStateText = "切换失败（模拟）", CurrentFileText = "待重新检查（模拟）", CanChangePaths = true,
                Description = "模拟：区服切换失败，恢复 journal 已保留。请检查目录与备份后通过已有恢复流程处理。隔离验收不执行游戏文件覆盖。",
            });
        }
        if (index == 38) Field<SettingsPage>(_window, "_settingsPage").ShowWorkspace("network");
        if (index == 39) Field<AccountsPage>(_window, "_accountsPage").ShowWorkspace("storage");
        if (index == 15)
        {
            var diag = (DiagnosticsViewModel)Field<DiagnosticsPage>(_window, "_diagnosticsPage").DataContext;
            if (diag.Checks.Count == 0) foreach (var c in new[] {
                new DiagnosticCheck { Category = "网络", Name = "Twitch 代理连接", Status = DiagnosticSeverity.Error, Summary = "模拟：代理连接失败，Session 尚未验证。", Details = "隔离状态，不执行网络请求。" },
                new DiagnosticCheck { Category = "Drops", Name = "Worker 状态", Status = DiagnosticSeverity.Warning, Summary = "模拟：Twitch Worker 已退出，可重新检测组件。" },
                new DiagnosticCheck { Category = "环境", Name = "配置存储", Status = DiagnosticSeverity.Healthy, Summary = "隔离验收目录可用。" } }) diag.Checks.Add(c);
            diag.ApplyReviewReport(new DiagnosticRunReport { Checks = diag.Checks.ToList(), StartedAt = DateTimeOffset.Now, CompletedAt = DateTimeOffset.Now });
        }
        _window.UpdateLayout();
    }

    private async Task RunMatrixAsync()
    {
        try
        {
            var renders = Path.Combine(_output, "wpf-renders");Directory.CreateDirectory(renders);
            for (int i = 0; i < Scenarios.Length; i++)
            {
                _scenarios.SelectedIndex = i;
                await _window.Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
                foreach (var (width, height, scale) in new[] { (1366d,768d,1d), (1366d,768d,1.25), (1366d,768d,1.5), (1920d,1080d,1d), (1920d,1080d,1.25), (1920d,1080d,1.5), (2560d,1440d,1d), (2560d,1440d,1.25), (2560d,1440d,1.5) })
                {
                    // Explicit DIP layout testing; this does not claim to change the monitor DPI.
                    _window.Width = width / scale;_window.Height = height / scale;
                    _window.UpdateLayout();
                    await _window.Dispatcher.InvokeAsync(() => { }, DispatcherPriority.Render);
                    _checks.Add($"layout {i+1:00} {width}x{height} @{scale*100:0}% equivalent-DIP");
                    CheckVisibleBounds(i, width, height, scale);
                    if (width == 1366 && scale == 1.5 && i is 1 or 5 or 9 or 10 or 12 or 13 or 16)
                        Capture(Path.Combine(renders, $"{i+1:00}-1366-150.png"));
                }
                _window.Width = 1280;_window.Height = 820;_window.UpdateLayout();
                await _window.Dispatcher.InvokeAsync(() => { }, DispatcherPriority.Render);
                Capture(Path.Combine(renders, $"{i+1:00}.png"));
            }
            ThemeManager.Apply(true);_scenarios.SelectedIndex = 8;_window.UpdateLayout();Capture(Path.Combine(renders,"dark.png"));ThemeManager.Apply(false);
            await CheckDraftsAsync();
            await CheckAuthorizationCancellationAsync();
            var dpi = VisualTreeHelper.GetDpi(_window);
            File.WriteAllLines(Path.Combine(_output, "matrix-results.txt"), _checks.Append($"binding-errors={_bindings.Errors.Count}").Append($"physical-monitor-dpi={dpi.PixelsPerInchX:0}/{dpi.PixelsPerInchY:0}").Append("DPI method: equivalent DIP sizes, not physical monitor switching"));
            Environment.ExitCode = _bindings.Errors.Count > 0 || _checks.Any(c => c.StartsWith("FAIL")) ? 1 : 0;
        }
        catch (Exception e) { File.WriteAllText(Path.Combine(_output, "review-failure.txt"), e.ToString());Environment.ExitCode = 1; }
        finally { _window.Close(); }
    }
    private void CheckVisibleBounds(int scenario, double width, double height, double scale)
    {
        void Walk(DependencyObject parent)
        {
            for (var i=0;i<VisualTreeHelper.GetChildrenCount(parent);i++)
            {
                var child=VisualTreeHelper.GetChild(parent,i);
                if (child is FrameworkElement element && element.IsVisible && element.ActualWidth > 1 && element.TemplatedParent is null &&
                    element is ButtonBase or TextBox or ComboBox or PasswordBox)
                {
                    var bounds = element.TransformToAncestor(_window).TransformBounds(new Rect(0,0,element.ActualWidth,element.ActualHeight));
                    if (bounds.Bottom > 100 && bounds.Top < _window.ActualHeight && bounds.Right > _window.ActualWidth + 2)
                        _checks.Add($"FAIL horizontal overflow scenario={scenario+1:00} {width}x{height}@{scale} {element.GetType().Name}/{element.Name} right={bounds.Right:0} window={_window.ActualWidth:0}");
                }
                Walk(child);
            }
        }
        Walk(_window);
        if (scenario == 5 && width == 1366 && scale == 1.5 && _page.FindName("TwitchLoginAction") is Button login)
        {
            var bounds = login.TransformToAncestor(_window).TransformBounds(new Rect(0,0,login.ActualWidth,login.ActualHeight));
            var scroll = (ScrollViewer)_page.FindName("DropsScrollViewer");
            var viewport = scroll.TransformToAncestor(_window).TransformBounds(new Rect(0,0,scroll.ActualWidth,scroll.ActualHeight));
            _checks.Add(bounds.Bottom <= viewport.Bottom+1 ? "PASS narrow Twitch login primary action visible without scrolling" : "FAIL narrow Twitch login below first viewport");
        }
    }
    private async Task CheckDraftsAsync()
    {
        _scenarios.SelectedIndex = 9;
        var quality = (ComboBox)_page.FindName("TwitchConnectionQuality");quality.SelectedIndex = 4;
        _page.SelectPlatform(DropsPlatform.Soop);_page.SelectPlatform(DropsPlatform.Twitch);_page.ShowWorkspace("settings");
        await _page.RefreshAsync();
        _checks.Add(quality.SelectedIndex == 4 ? "PASS Twitch settings draft survives platform navigation/refresh" : "FAIL Twitch draft lost");
        var save=(Button)_page.FindName("TwitchSaveSettingsButton");save.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        await _window.Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
        _checks.Add(_checks.Any(c=>c=="mock-request Twitch/save_settings") ? "PASS real Twitch save Click reaches existing Worker command" : "FAIL Twitch save command missing");
        _page.SelectPlatform(DropsPlatform.Soop);_page.ShowWorkspace("settings");var interval=(TextBox)_page.FindName("SoopMissionInterval");interval.Text="137";
        _page.SelectPlatform(DropsPlatform.YouTube);_page.SelectPlatform(DropsPlatform.Soop);await _page.RefreshAsync();
        _checks.Add(interval.Text=="137" ? "PASS SOOP draft survives platform navigation/refresh" : "FAIL SOOP draft lost");
        _page.SelectPlatform(DropsPlatform.YouTube);_page.ShowWorkspace("settings");var yt=(TextBox)_page.FindName("YouTubeInterval");yt.Text="357";
        _page.SelectPlatform(DropsPlatform.Twitch);_page.SelectPlatform(DropsPlatform.YouTube);await _page.RefreshAsync();
        _checks.Add(yt.Text=="357" ? "PASS YouTube draft survives platform navigation/refresh" : "FAIL YouTube draft lost");
        var quickAction = typeof(DropsPage).GetMethod("OnQuickStartAction", BindingFlags.NonPublic | BindingFlags.Instance)!;
        quickAction.Invoke(_page, [new Button { Tag = "youtube_channel" }, new RoutedEventArgs()]);
        _checks.Add(_page.WorkspaceSection == "activities" ? "PASS YouTube channel quick-start opens activities section" : "FAIL YouTube quick-start section");
        _page.SelectPlatform(DropsPlatform.Soop);
        quickAction.Invoke(_page, [new Button { Tag = "soop_add_account" }, new RoutedEventArgs()]);
        _checks.Add(_page.WorkspaceSection == "accounts" && ((Expander)_page.FindName("SoopAddAccountExpander")).IsExpanded
            ? "PASS SOOP quick-start opens account form" : "FAIL SOOP quick-start form hidden");
    }
    private static void CreateSimulatedQr(string path)
    {
        // A deliberately non-authentic test pattern exercises Image loading without a login credential.
        const int side=29, unit=6;var pixels = Enumerable.Repeat((byte)255,side*side*unit*unit).ToArray();
        var random=new Random(17);
        bool Finder(int x,int y,int ox,int oy) => x>=ox && x<ox+7 && y>=oy && y<oy+7 &&
            (x==ox || y==oy || x==ox+6 || y==oy+6 || x>=ox+2 && x<=ox+4 && y>=oy+2 && y<=oy+4);
        for(int y=0;y<side;y++) for(int x=0;x<side;x++)
        {
            bool black=Finder(x,y,2,2)||Finder(x,y,20,2)||Finder(x,y,2,20)||x>10&&x<25&&y>10&&y<25&&random.Next(2)==0;
            if(black)for(int py=0;py<unit;py++)for(int px=0;px<unit;px++)pixels[(y*unit+py)*side*unit+x*unit+px]=0;
        }
        var bitmap=BitmapSource.Create(side*unit,side*unit,96,96,PixelFormats.Gray8,null,pixels,side*unit);
        var png=new PngBitmapEncoder();png.Frames.Add(BitmapFrame.Create(bitmap));using var file=File.Create(path);png.Save(file);
    }
    private async Task CheckAuthorizationCancellationAsync()
    {
        _page.SelectPlatform(DropsPlatform.Twitch);
        var stopped = new TaskCompletionSource<JsonElement>(TaskCreationOptions.RunContinuationsAsynchronously);
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var loginRequests = 0;
        _main.DropsHost.ConfigureRequestOverrideForSelfTest((platform, command, payload, token) =>
        {
            if (platform == DropsPlatform.Twitch && command == "stop") { entered.TrySetResult(); return stopped.Task; }
            if (platform == DropsPlatform.Twitch && command == "login") loginRequests++;
            return Task.FromResult(_states.GetValueOrDefault(platform, Json(new { })));
        });
        var renewing = (Task)typeof(DropsPage).GetMethod("RenewTwitchAuthorizationAsync", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(_page, null)!;
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        var cancel = (Task)typeof(DropsPage).GetMethod("StopPlatformAsync", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(_page, [DropsPlatform.Twitch, false])!;
        stopped.SetResult(Twitch());
        await Task.WhenAll(renewing, cancel).WaitAsync(TimeSpan.FromSeconds(5));
        _checks.Add(loginRequests == 0 ? "PASS cancel/stop prevents in-flight device-code renewal from restarting login" : "FAIL device authorization restarted after cancel");
    }
    private void Capture(string path)
    {
        var visual = (FrameworkElement)_window.Content;
        var bitmap = new RenderTargetBitmap((int)Math.Ceiling(visual.ActualWidth), (int)Math.Ceiling(visual.ActualHeight),96,96,PixelFormats.Pbgra32);
        bitmap.Render(visual);var png = new PngBitmapEncoder();png.Frames.Add(BitmapFrame.Create(bitmap));using var stream = File.Create(path);png.Save(stream);
    }
    private sealed class BindingListener : TraceListener
    {
        public List<string> Errors { get; } = [];
        public override void Write(string? message) { if (!string.IsNullOrWhiteSpace(message)) Errors.Add(message); }
        public override void WriteLine(string? message) => Write(message);
    }
}
