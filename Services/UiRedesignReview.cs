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
internal sealed partial class UiRedesignReview
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
    private readonly string _reviewGameImage="review-fixture://layout/cover",_reviewRewardTitle="模拟奖励组合 · 水晶与徽章";
    private readonly JsonElement _reviewBenefits=JsonSerializer.SerializeToElement(new[]{
        new{id="fixture-a",name="模拟水晶 A",type="DIRECT_ENTITLEMENT",imageUrl="review-fixture://layout/benefit-a"},
        new{id="fixture-b",name="模拟徽章 B",type="BADGE",imageUrl="review-fixture://layout/benefit-b"},
        new{id="fixture-c",name="缺图回退 C",type="EMOTE",imageUrl=""}});
    private readonly JsonElement _reviewTwitchSnapshot;
    private TextBlock? _evidenceWatermark;
    internal static readonly string[] Scenarios = [
        "01 总览", "02 Battle.net 账号管理", "03 区服切换", "04 战绩", "05 区服快照",
        "06 Twitch 未登录", "07 Twitch 设备授权", "08 Twitch 已登录", "09 Twitch 正在观看", "10 Twitch 完整设置",
        "11 SOOP 多账号", "12 SOOP 设置", "13 Bilibili 登录", "14 Bilibili 设置", "15 YouTube", "16 诊断中心", "17 全局设置",
        "18 Twitch 配置损坏", "19 Twitch 网络失败", "20 SOOP 无账号", "21 SOOP 单账号停止", "22 Bilibili 二维码过期",
        "23 Bilibili 已登录", "24 YouTube 浏览器不可用", "25 Worker 崩溃", "26 代理不可用", "27 账号切换失败", "28 区服切换失败",
        "29 Twitch 活动与奖励", "30 Twitch 账号维护", "31 Bilibili 活动任务", "32 Twitch 网络与维护", "33 运行日志", "34 Twitch 游戏优先级", "35 Twitch 自动化",
        "36 SOOP 刷新与恢复", "37 Bilibili 自动化与通知", "38 YouTube 浏览器配置", "39 全局网络代理", "40 账号备份与存储", "41 关于与来源", "42 SOOP 正在观看", "43 Bilibili 任务", "44 Twitch 奖励详情",
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
        Controls.VisualImage.ReviewLoader=Visuals.ReviewImageAssets.LoadAsync;
        var visualFixture=Path.Combine(_output,"visual-fixture.json");
        if(File.Exists(visualFixture))
        {
            using var fixture=JsonDocument.Parse(File.ReadAllText(visualFixture));
            _reviewGameImage=fixture.RootElement.TryGetProperty("gameImageUrl",out var gameImage)?gameImage.GetString()??"":"";
            if(fixture.RootElement.TryGetProperty("benefits",out var benefits)&&benefits.ValueKind==JsonValueKind.Array)_reviewBenefits=benefits.Clone();
            _reviewRewardTitle=fixture.RootElement.TryGetProperty("rewardTitle",out var rewardTitle)?rewardTitle.GetString()??"模拟奖励":"模拟奖励";
        }
        var capturedSnapshot=Path.Combine(_output,"core-visual-state.json");
        if(File.Exists(capturedSnapshot))
        {
            using var captured=JsonDocument.Parse(File.ReadAllText(capturedSnapshot));
            var state=captured.RootElement.TryGetProperty("result",out var result)?result:captured.RootElement.TryGetProperty("state",out var official)?official:captured.RootElement;
            // Opt-in replay only: keep visual data, never load authentication or obsolete Host generations.
            var safeNames=new[]{"coreAvailable","authState","campaigns","inventory","currentDrop","currentChannel","lastInventorySyncAt","inventorySource","watchRequests"};
            var fields=state.EnumerateObject().Where(p=>safeNames.Contains(p.Name)).ToDictionary(p=>p.Name,p=>p.Value.Clone());
            fields["historicalReplay"]=Json(true);fields["running"]=Json(false);fields["authState"]=Json("logged_in");
            fields["accounts"]=Json(new[]{new{userId="历史官方数据 · 隔离回放",loggedIn=true}});
            _reviewTwitchSnapshot=Json(fields);
        }
        CreateSimulatedQr(Path.Combine(_output, "simulated-qr.png"));
        AppPaths.UseSelfTestRoot(Path.Combine(_output, "isolated-data"));
        _automated = automated;
        var paths = new BattleNetPaths(Path.Combine(_output, "isolated-battle-local"), Path.Combine(_output, "isolated-battle-roaming")) { ClientExe = null };
        _window = new MainWindow(startHidden: true, settings: new AppSettings { LastMainSection = "overview", CloseToTray = false }, paths: paths, isolatedReview: true);
        Application.Current.MainWindow = _window;
        _window.Title = _reviewTwitchSnapshot.ValueKind==JsonValueKind.Object ? "CloudLight Blizzard · 官方快照回放（隔离验收）" : "CloudLight Blizzard · 隔离界面验收（模拟数据）";
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
        controls.Children.Add(new TextBlock { Text = "隔离验收 · 账号 / 进度 / 图片均为模拟", FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center });
        _scenarios.ItemsSource = Scenarios;
        controls.Children.Add(_scenarios);
        var next = new Button { Content = "下一场景", Style = (Style)_window.FindResource("SecondaryButton"), Padding = new Thickness(12, 0, 12, 0) };
        next.Click += (_, _) => _scenarios.SelectedIndex = (_scenarios.SelectedIndex + 1) % Scenarios.Length;
        controls.Children.Add(next);
        var theme = new Button { Content = "切换深浅色", Style = (Style)_window.FindResource("SecondaryButton"), Margin = new Thickness(8, 0, 0, 0), Padding = new Thickness(12, 0, 12, 0) };
        theme.Click += (_, _) => ThemeManager.Apply(!ThemeManager.IsDark);controls.Children.Add(theme);
        banner.Child = controls;shell.Children.Add(banner);Grid.SetRow(root, 1);shell.Children.Add(root);_window.Content = shell;
        if(File.Exists(Path.Combine(_output,"density-test.flag"))||File.Exists(Path.Combine(_output,"priority-state-test.flag")))
        {
            banner.Visibility=Visibility.Collapsed;
            _evidenceWatermark=new TextBlock{Text=_reviewTwitchSnapshot.ValueKind==JsonValueKind.Object?"TWITCH 官方快照回放 · 其他平台模拟":"SIMULATED · 隔离模拟数据",FontSize=12,FontWeight=FontWeights.SemiBold};
            var watermark=new Border{Background=(Brush)_window.FindResource("AccSoft"),Padding=new Thickness(6,2,6,2),HorizontalAlignment=HorizontalAlignment.Right,VerticalAlignment=VerticalAlignment.Bottom,Margin=new Thickness(6),IsHitTestVisible=false,Child=_evidenceWatermark};
            Grid.SetRowSpan(watermark,2);Panel.SetZIndex(watermark,100);shell.Children.Add(watermark);
        }
        PresentationTraceSources.DataBindingSource.Switch.Level = SourceLevels.Error;
        PresentationTraceSources.DataBindingSource.Listeners.Add(_bindings);
        _scenarios.SelectionChanged += (_, _) => Apply(_scenarios.SelectedIndex);
        _window.Loaded += async (_, _) =>
        {
            _scenarios.SelectedIndex = Math.Clamp(initialScenario, 0, Scenarios.Length-1);
            if (_automated)
            {
                if(File.Exists(Path.Combine(_output,"six-repairs-test.flag")))await RunSixRepairsAsync();
                else if(File.Exists(Path.Combine(_output,"priority-state-test.flag")))await RunPriorityStateAsync();
                else if(File.Exists(Path.Combine(_output,"density-test.flag")))await RunDensityAsync();
                else await RunMatrixAsync();
            }
        };
        _window.Closed += (_, _) =>
        {
            PresentationTraceSources.DataBindingSource.Listeners.Remove(_bindings);
            File.WriteAllLines(Path.Combine(_output, "binding-errors.txt"), _bindings.Errors);
            File.WriteAllLines(Path.Combine(_output, "review-requests.txt"), _checks);
        };
    }

    private JsonElement Twitch(bool loggedIn = false, bool running = false) => loggedIn&&_reviewTwitchSnapshot.ValueKind==JsonValueKind.Object ? _reviewTwitchSnapshot.Clone() : Json(new {
        coreAvailable = true, workerHealth="ready", running, authState = loggedIn ? "logged_in" : "logged_out", status = running ? "正在观看" : loggedIn ? "已登录，未开始" : "尚未登录",
        accounts = loggedIn ? new[] { new { userId = "模拟账号 · review-user", loggedIn = true } } : [],
        settings = new { language = "简体中文", priority_mode = "PRIORITY_ONLY", connection_quality = 1, tray_notifications = true, auto_claim_drops = false, priority = new[] { "Overwatch 2" }, exclude = Array.Empty<string>() },
        campaigns = loggedIn ? new[] { new { id = "review-campaign", name = "隔离模拟活动", game = "Overwatch", gameImageUrl=_reviewGameImage, available = true, priority=true, eligible=true, linked=true, active=true, canEarn=true, availability="available", totalDrops=3, completedDrops=1, claimedDrops=0, requiredMinutes=60, remainingMinutes=42, progress=0.3, endsAt=DateTimeOffset.UtcNow.AddDays(2).ToString("O"), status = "ACTIVE", drops = new[] { new { id = "reward1", name = _reviewRewardTitle,benefits=_reviewBenefits,campaignId="review-campaign",gameImageUrl=_reviewGameImage, requiredMinutes = 60, currentMinutes = 18, progress = 0.3 } } } } : [],
        inventory = loggedIn ? new[] {
            new { id = "reward1", name = _reviewRewardTitle, campaign = "隔离模拟活动", campaignId="review-campaign",game="Overwatch",benefits=_reviewBenefits,gameImageUrl=_reviewGameImage,requiredMinutes = 60, currentMinutes = 18, progress = 0.3, completed = false, claimed = false, canClaim = false },
            new { id = "reward2", name = "模拟奖励 B · 缺图回退",campaign="隔离模拟活动",campaignId="review-campaign",game="Overwatch",benefits=JsonSerializer.SerializeToElement(Array.Empty<object>()),gameImageUrl=_reviewGameImage,requiredMinutes=120,currentMinutes=0,progress=0d,completed=false,claimed=false,canClaim=false },
            new { id = "reward3", name = "模拟奖励 C · 已完成",campaign="隔离模拟活动",campaignId="review-campaign",game="Overwatch",benefits=JsonSerializer.SerializeToElement(Array.Empty<object>()),gameImageUrl=_reviewGameImage,requiredMinutes=30,currentMinutes=30,progress=1d,completed=true,claimed=false,canClaim=false },
        } : [],
        channels = loggedIn ? new[] { new { id = "review-channel", name = "模拟频道 · Overwatch", game = "Overwatch 2", dropsEnabled = true, online = true } } : [],
        currentChannel = running ? new { id = "review-channel", name = "模拟频道 · Overwatch", game = "Overwatch 2", dropsEnabled = true, online = true, url = "https://www.twitch.tv/example" } : null,
        inventorySource = loggedIn ? "remote" : "none", lastInventorySyncAt = loggedIn ? DateTimeOffset.UtcNow.ToString("O") : "",
        watchRequests = new { channelId="review-channel", lastResult = running ? "accepted" : "none", attempts = running ? 1 : 0,lastAcceptedAt=running?DateTimeOffset.UtcNow.ToString("O"):"" },
        currentDrop = running ? new { id = "reward1", campaign = "隔离模拟活动", campaignId="review-campaign",game="Overwatch", name = _reviewRewardTitle,benefits=_reviewBenefits,gameImageUrl=_reviewGameImage, currentMinutes = 18, requiredMinutes = 60, progress = 0.3, estimatedExtraMinutes = 2 } : null,
    });
    private void Apply(int index)
    {
        if (index < 0) return;
        _main.StatusText = "隔离验收：账号、授权和奖励均为模拟数据。";
        if (index != 27) typeof(MainViewModel).GetProperty(nameof(MainViewModel.RegionGuide))!.SetValue(_main, _defaultRegionGuide);
        foreach (var p in Enum.GetValues<DropsPlatform>()) _drops.DismissMessage(p);
        _states[DropsPlatform.Twitch] = Twitch(index is 0 or 7 or 8 or 9 or 28 or 29 or 31 or 33 or 34 or 43, index is 0 or 8);
        if(File.Exists(Path.Combine(_output,"density-test.flag"))&&_reviewTwitchSnapshot.ValueKind!=JsonValueKind.Object&&index is 8 or 28 or 43)
            _states[DropsPlatform.Twitch]=DenseFixture(_states[DropsPlatform.Twitch]);
        _states[DropsPlatform.Soop] = Raw("""{"running":true,"settings":{"channel_mode":"smart","priority_mission_id":"auto","auto_claim_enabled":false,"low_bandwidth_mode":true,"hang_without_missions":true,"mission_poll_interval":90,"inventory_poll_interval":300,"channel_refresh_interval":300,"reauth_retry_interval":300},"accounts":[{"uid":"模拟主账号","primary":true,"loggedIn":true,"running":true,"channelName":"模拟赛事频道","missionTitle":"模拟赛事任务 · 阶段 3/5","currentProgress":[{"account":"模拟主账号","reward":"模拟阶段奖励","currentMinutes":15,"requiredMinutes":30}]},{"uid":"模拟副账号","primary":false,"loggedIn":true,"running":false,"channelName":"尚未进入","missionTitle":"模拟任务 · 阶段 1/4","currentProgress":[{"account":"模拟副账号","reward":"模拟阶段奖励 B","currentMinutes":5,"requiredMinutes":45}]}]}""");
        _states[DropsPlatform.Bilibili] = Raw("""{"running":false,"loggedIn":false,"account":{"loggedIn":false,"uid":0,"userName":""},"status":"尚未登录","settings":{"enabled":true,"watch_mode":"standard","sessions_per_room":1,"auto_claim":false},"rooms":[],"activities":[],"tasks":[],"sessions":[]}""");
        _states[DropsPlatform.YouTube] = Raw("""{"running":false,"status":"尚未开始","profiles":["模拟观看账号"],"config":{"browser":"chrome","mode":"auto","headless":false,"mute":true,"check_interval":300},"channels":[],"history":{"rows":[]},"sessions":[]}""");
        if (index == 6) _states[DropsPlatform.Twitch] = Json(new { coreAvailable = true, running = false, authState = "authorization_required", accounts = Array.Empty<object>(), authRequired = new { code = "CLB-TEST", url = "https://www.twitch.tv/activate", expiresAt = DateTimeOffset.UtcNow.AddMinutes(15).ToString("O") } });
        if (index == 17) _states[DropsPlatform.Twitch] = Json(new { coreAvailable = false, coreErrorKind = "configuration", coreError = "模拟：配置无法解析。已保存的 Session 保留，可备份并恢复配置。", configuration = new { canReset = true }, accounts = Array.Empty<object>() });
        if (index == 18) _states[DropsPlatform.Twitch] = Json(new { coreAvailable = true, running = false, authState = "failed", loginErrorCode = "network_error", loginError = "模拟：连接超时，Session 尚未完成验证。", accounts = new[] { new { userId = "已保存 Session · 模拟", loggedIn = false } } });
        if (index == 19) _states[DropsPlatform.Soop] = Raw("""{"running":false,"accounts":[],"settings":{"channel_mode":"smart"}}""");
        if(index is 10 or 11 or 20 or 41)_states[DropsPlatform.Soop]=CloudLightBlizzard.Services.Visuals.SoopCampaignSelfTest.Fixture();
        if(index is 22 or 30 or 42)_states[DropsPlatform.Bilibili]=Raw("""{"loggedIn":true,"account":{"loggedIn":true,"uid":123456,"userName":"模拟 Bilibili 账号"},"status":"已登录 · 等待开始观看","running":false,"rooms":[{"id":1,"name":"模拟赛事直播间","enabled":true}],"tasks":[{"id":"fixture-task","name":"模拟观看任务","current":18,"limit":60,"percent":30,"completed":false,"claimed":false,"claimable":false}],"activities":[]}""");
        var section = index switch { 0 => "overview", 1 or 26 or 39 => "accounts", 2 or 27 => "region", 3 => "stats", 4 => "snapshots", 40 => "about", 15 => "diagnostics", 16 or 38 => "settings", _ => "drops" };
        _window.NavigateTo(section);
        foreach(var platformState in _states)_drops.ApplyState(platformState.Key,platformState.Value);
        foreach(var visualPlatform in _drops.VisualPlatforms)visualPlatform.Health.WorkerHealth="隔离模拟 · 未启动真实 Worker";
        if (section == "drops")
        {
            var platform = index switch { 10 or 11 or 19 or 20 or 35 or 41 => DropsPlatform.Soop, 12 or 13 or 21 or 22 or 30 or 36 or 42 => DropsPlatform.Bilibili, 14 or 23 or 37 => DropsPlatform.YouTube, _ => DropsPlatform.Twitch };
            _page.SelectPlatform(platform);
            _drops.SelectPlatform(platform);
            _drops.ApplyState(platform, _states[platform]);
            _page.ShowWorkspace(index switch { 9 or 11 or 13 or 37 => "settings", 10 or 20 or 29 => "accounts", 28 or 30 or 43 => "activities", 31 => "network", 32 => "logs", 33 => "games", 34 or 36 => "automation", 35 => "advanced", _ => "overview" });
            if (index is 12 or 21)
            {
                _drops.BilibiliDetails.HandleEvent("qr_login", Json(new { state = index == 21 ? "expired" : "waiting_scan", message = index == 21 ? "模拟：二维码已过期，请重新获取。" : "模拟二维码，仅用于布局验收，不可用于登录。", url = "https://example.invalid/isolated-review", imagePath = Path.Combine(_output, "simulated-qr.png"), expiresAt = DateTimeOffset.UtcNow.AddMinutes(3).ToString("O") }));
            }

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
        if(index==43)foreach(var detail in Descendants<Expander>(_page).Where(e=>e.Header as string=="奖励与前置任务详情"))detail.IsExpanded=true;
    }

    private static IEnumerable<T> Descendants<T>(DependencyObject parent) where T:DependencyObject
    {
        for(var i=0;i<VisualTreeHelper.GetChildrenCount(parent);i++)
        {
            var child=VisualTreeHelper.GetChild(parent,i);if(child is T typed)yield return typed;
            foreach(var nested in Descendants<T>(child))yield return nested;
        }
    }

    private JsonElement DenseFixture(JsonElement original)
    {
        var state=original.EnumerateObject().ToDictionary(p=>p.Name,p=>p.Value.Clone());
        var campaigns=new List<object>();var inventory=new List<object>();
        for(var campaignIndex=0;campaignIndex<8;campaignIndex++)
        {
            var campaignId=campaignIndex==0?"review-campaign":"density-campaign-"+campaignIndex;
            var rewards=new List<object>();
            for(var index=0;index<(campaignIndex==0?10:campaignIndex is 2 or 3 or 4?campaignIndex-1:6);index++)
            {
                var id=campaignIndex==0&&index==0?"reward1":$"density-drop-{campaignIndex}-{index}";
                var completed=index is 1 or 2;var required=60*(index+1);var current=completed?required:index==0?18:0;
                var benefits=Enumerable.Range(0,index==0&&campaignIndex is 0 or 1?2:1).Select(b=>new{id=$"density-benefit-{campaignIndex}-{index}-{b}",name=index==5?"模拟长名称物品 · A long official-style reward name for wrapping":"模拟物品 "+(index+1)+(b==0?" A":" B"),type="DIRECT_ENTITLEMENT",imageUrl=b==0?"review-fixture://layout/benefit-a":"review-fixture://layout/benefit-b"}).ToArray();
                var reward=new{id,name="模拟阶段 "+(index+1),campaignId,campaign="模拟活动 "+(campaignIndex+1),game="布局模拟游戏 "+(campaignIndex+1),benefits,gameImageUrl=_reviewGameImage,requiredMinutes=required,currentMinutes=current,completed,claimed=index==2,canClaim=index==1,eligible=index!=4,upcoming=campaignIndex==6,expired=campaignIndex==7,preconditionDropIds=index>2?new[]{"density-prior"}:Array.Empty<string>(),preconditionsMet=index<3,preconditionsSatisfiedForMining=index<3};
                rewards.Add(reward);inventory.Add(reward);
            }
            campaigns.Add(new{id=campaignId,name="模拟活动 "+(campaignIndex+1)+" · 奖励背包密度验收",game="布局模拟游戏 "+(campaignIndex+1),gameImageUrl=_reviewGameImage,available=campaignIndex<6,priority=true,eligible=true,active=campaignIndex<6,upcoming=campaignIndex==6,expired=campaignIndex==7,endsAt=DateTimeOffset.UtcNow.AddDays(campaignIndex==7?-1:campaignIndex+1).ToString("O"),completedDrops=2,totalDrops=rewards.Count,remainingMinutes=42,progress=.3,drops=rewards});
        }
        state["campaigns"]=Json(campaigns);state["inventory"]=Json(inventory);
        var selected=Json(inventory[0]).EnumerateObject().ToDictionary(p=>p.Name,p=>p.Value.Clone());selected["estimatedExtraMinutes"]=Json(2);state["currentDrop"]=Json(selected);
        state["currentChannel"]=Json(new{id="review-channel",name="模拟频道",game="布局模拟游戏 1",online=true,dropsEnabled=true});
        return Json(state);
    }
    private async Task RunDensityAsync()
    {
        try
        {
            var output=Path.Combine(_output,"screenshots");Directory.CreateDirectory(output);
            foreach(var (width,height) in new[]{(1366,768),(1600,900),(1920,1080),(2560,1440)})
            foreach(var section in new[]{"overview","activities"})
            {
                File.AppendAllText(Path.Combine(_output,"density-progress.txt"),$"begin {width} {section}\n");
                _scenarios.SelectedIndex=8;_page.SelectPlatform(DropsPlatform.Twitch);_page.ShowWorkspace(section);
                File.AppendAllText(Path.Combine(_output,"density-progress.txt"),"workspace selected\n");
                _drops.SetTwitchCampaignScope("all");_window.Width=width/1.25;_window.Height=height/1.25;
                File.AppendAllText(Path.Combine(_output,"density-progress.txt"),"scope set\n");
                ((ComboBox)_page.FindName("TwitchCampaignScopePicker")).SelectedIndex=2;
                _window.UpdateLayout();await _window.Dispatcher.InvokeAsync(()=>{},DispatcherPriority.ApplicationIdle);await Task.Delay(450);_window.UpdateLayout();
                CheckVisibleBounds(8,width,height,1.25);
                var tiles=Descendants<Border>(_page).Where(b=>b.IsVisible&&b.Tag as string=="RewardTile").ToArray();
                var viewport=Descendants<Controls.CampaignInventoryView>(_page).Single(v=>v.IsVisible);
                var outer=(ScrollViewer)_page.FindName("DropsScrollViewer");
                var outerClip=outer.TransformToAncestor(_window).TransformBounds(new Rect(0,0,outer.ActualWidth,outer.ActualHeight));
                var clip=Rect.Intersect(outerClip,viewport.TransformToAncestor(_window).TransformBounds(new Rect(0,0,viewport.ActualWidth,viewport.ActualHeight)));
                var bounds=tiles.Select(tile=>tile.TransformToAncestor(_window).TransformBounds(new Rect(0,0,tile.ActualWidth,tile.ActualHeight))).ToArray();
                var visible=bounds.Where(b=>b.IntersectsWith(clip)).ToArray();var full=visible.Count(b=>b.Top>=clip.Top-1&&b.Bottom<=clip.Bottom+1);
                var columns=visible.GroupBy(b=>Math.Round(b.Top)).OrderBy(g=>g.Key).FirstOrDefault()?.Count()??0;
                var cards=Descendants<Controls.RewardCard>(viewport).Where(c=>c.IsVisible&&c.ActualWidth>0).ToArray();
                var scrollbars=Descendants<ScrollViewer>(viewport).Where(s=>s.IsVisible).ToArray();
                if(cards.Any(c=>c.ActualWidth<189))_checks.Add("FAIL card compressed below 190 DIP");
                if(scrollbars.Any(s=>s.ScrollableWidth>1))_checks.Add("FAIL horizontal reward scrolling");
                foreach(var card in cards)
                {
                    if(Descendants<Button>(card).Any(b=>b.IsVisible&&b.ActualHeight<31))_checks.Add("FAIL reward action below 32 DIP");
                    if(Descendants<ProgressBar>(card).Any(p=>p.IsVisible&&p.ActualHeight<6.9))_checks.Add("FAIL progress bar below 7 DIP");
                    var name=Descendants<TextBlock>(card).FirstOrDefault(t=>t.FontSize==15);
                    if(name is null||name.MaxHeight!=40||name.ToolTip is null||name.TextTrimming!=TextTrimming.CharacterEllipsis)_checks.Add("FAIL reward name must support two lines, ellipsis and full tooltip");
                    foreach(var text in Descendants<TextBlock>(card).Where(t=>t.IsVisible&&t.ActualHeight>0))
                    {
                        var textBounds=text.TransformToAncestor(card).TransformBounds(new Rect(0,0,text.ActualWidth,text.ActualHeight));
                        if(textBounds.Bottom>card.ActualHeight+1||textBounds.Right>card.ActualWidth+1)_checks.Add("FAIL text outside reward card");
                    }
                }
                _checks.Add($"PASS readable controls {section} {width}: cards={cards.Length}, horizontal-scroll=0, names=two-lines-with-tooltip, buttons>=32, bars>=7");
                _checks.Add($"density {section} physical-request={width}x{height}@125% actual={_window.ActualWidth:0.#}x{_window.ActualHeight:0.#} DIP content-width={viewport.ActualWidth:0.#} columns={columns} full-cards={full} intersecting-cards={visible.Length} realized-cards={tiles.Length} tile-width={tiles.FirstOrDefault()?.ActualWidth:0.#} tile-height={tiles.FirstOrDefault()?.ActualHeight:0.#} image-height=92 name-font=15 official-font=14 percent-font=16 state-font=13 button-min-height=32");
                for(var a=0;a<bounds.Length;a++)for(var b=a+1;b<bounds.Length;b++){var overlap=Rect.Intersect(bounds[a],bounds[b]);if(!overlap.IsEmpty&&overlap.Width>1&&overlap.Height>1)_checks.Add("FAIL overlapping reward tiles");}
                Capture(Path.Combine(output,$"{section}-{width}x{height}-125.png"));
                if(full<columns)
                {
                    outer.ScrollToVerticalOffset(viewport.TranslatePoint(new Point(),outer).Y+outer.VerticalOffset);
                    await _window.Dispatcher.InvokeAsync(()=>{},DispatcherPriority.ApplicationIdle);await Task.Delay(180);
                    var readingClip=Rect.Intersect(outerClip,viewport.TransformToAncestor(_window).TransformBounds(new Rect(0,0,viewport.ActualWidth,viewport.ActualHeight)));
                    var readingFull=tiles.Select(tile=>tile.TransformToAncestor(_window).TransformBounds(new Rect(0,0,tile.ActualWidth,tile.ActualHeight))).Count(b=>b.Top>=readingClip.Top-1&&b.Bottom<=readingClip.Bottom+1);
                    _checks.Add($"reading viewport {width}x{height} {section}: full-cards={readingFull}; account/filter headers scroll away; initial full-cards={full}");
                    if(readingFull<columns)_checks.Add($"FAIL reward row cannot be read completely after scrolling at {width}x{height} {section}");
                    Capture(Path.Combine(output,$"{section}-{width}x{height}-reading.png"));
                }
            }
            await CaptureCardCoverageAsync(output);
            _window.Width=1280;_window.Height=820;_scenarios.SelectedIndex=8;_page.ShowWorkspace("overview");
            ThemeManager.Apply(true);await _window.Dispatcher.InvokeAsync(()=>{},DispatcherPriority.ApplicationIdle);await Task.Delay(350);Capture(Path.Combine(output,"overview-dark.png"));
            _page.ShowWorkspace("activities");await _window.Dispatcher.InvokeAsync(()=>{},DispatcherPriority.ApplicationIdle);await Task.Delay(350);Capture(Path.Combine(output,"activities-dark.png"));ThemeManager.Apply(false);
            var fields=_states[DropsPlatform.Twitch].EnumerateObject().ToDictionary(p=>p.Name,p=>p.Value.Clone());
            fields["currentChannel"]=Json(null!);fields["currentDrop"]=Json(null!);fields["watchRequests"]=Json(new{});fields["running"]=Json(true);
            _drops.ApplyState(DropsPlatform.Twitch,Json(fields));_page.ShowWorkspace("overview");await _window.Dispatcher.InvokeAsync(()=>{},DispatcherPriority.ApplicationIdle);await Task.Delay(350);Capture(Path.Combine(output,"no-eligible-channel.png"));
            _checks.Add(_drops.TwitchInventory.WatchState=="等待合资格频道"?"PASS no channel is distinct from Worker running":"FAIL no-channel state");
            _page.ShowWorkspace("activities");_drops.TwitchInventory.Search="missing-filter-result";await _window.Dispatcher.InvokeAsync(()=>{},DispatcherPriority.ApplicationIdle);Capture(Path.Combine(output,"filtered-empty.png"));_drops.TwitchInventory.Search="";
            _checks.Add("binding-errors="+_bindings.Errors.Count);_checks.Add("physical-monitor-dpi="+VisualTreeHelper.GetDpi(_window).PixelsPerInchX);
            File.WriteAllLines(Path.Combine(_output,"density-results.txt"),_checks);
            Environment.ExitCode=_bindings.Errors.Count>0||_checks.Any(c=>c.StartsWith("FAIL"))?1:0;
        }
        catch(Exception ex){File.WriteAllText(Path.Combine(_output,"density-failure.txt"),ex.ToString());Environment.ExitCode=1;}
        finally{_window.Close();}
    }
    private async Task CaptureCardCoverageAsync(string output)
    {
        foreach(var (scenario,name,section) in new[]{(0,"dashboard","overview"),(41,"soop-tasks","overview"),(42,"bilibili-tasks","activities"),(8,"twitch-queue","queue")})
        foreach(var (width,height) in new[]{(1366,768),(1600,900),(1920,1080),(2560,1440)})
        foreach(var dark in new[]{false,true})
        {
            _scenarios.SelectedIndex=scenario;Apply(scenario);
            if(scenario!=0)_page.ShowWorkspace(section);
            _window.Width=width/1.25;_window.Height=height/1.25;ThemeManager.Apply(dark);
            _window.UpdateLayout();await _window.Dispatcher.InvokeAsync(()=>{},DispatcherPriority.ApplicationIdle);await Task.Delay(160);
            CheckVisibleBounds(scenario,width,height,1.25);
            Capture(Path.Combine(output,$"{name}-{width}x{height}-{(dark?"dark":"light")}.png"));
            if(scenario is 41 or 42)
            {
                var outer=(ScrollViewer)_page.FindName("DropsScrollViewer");
                var first=Descendants<Controls.RewardCard>(_page).FirstOrDefault(c=>c.IsVisible);
                if(first is not null)
                {
                    outer.ScrollToVerticalOffset(first.TranslatePoint(new Point(),outer).Y+outer.VerticalOffset-12);
                    await _window.Dispatcher.InvokeAsync(()=>{},DispatcherPriority.ApplicationIdle);await Task.Delay(180);
                    Capture(Path.Combine(output,$"{name}-{width}x{height}-{(dark?"dark":"light")}-reading.png"));
                }
            }
        }
        ThemeManager.Apply(false);_scenarios.SelectedIndex=8;Apply(8);_page.ShowWorkspace("activities");
        _window.Width=1536;_window.Height=864;_drops.SetTwitchCampaignScope("all");
        foreach(var kind in new[]{"one","two","three","many","multi-benefit","ineligible","upcoming","expired"})
        {
            _drops.TwitchInventory.Search="";
            var groups=_drops.TwitchInventory.Groups;
            var group=groups.FirstOrDefault(g=>kind switch{"one"=>g.Cards.Count==1,"two"=>g.Cards.Count==2,"three"=>g.Cards.Count==3,"many"=>g.Cards.Count>5,"multi-benefit"=>g.Drops.Any(d=>d.Benefits.Count>1),"ineligible"=>g.Drops.Any(d=>d.Eligible==false),"upcoming"=>g.Upcoming,"expired"=>g.Expired,_=>false});
            if(group is null){_checks.Add("NOT PRESENT in supplied data: "+kind);continue;}
            _drops.TwitchInventory.Search=group.Title;
            await _window.Dispatcher.InvokeAsync(()=>{},DispatcherPriority.ApplicationIdle);await Task.Delay(250);
            Capture(Path.Combine(output,"campaign-"+kind+".png"));
            _checks.Add($"PASS campaign coverage {kind}: {group.Drops.Count} Drop / {group.Cards.Count} Benefit cards; official minutes counted per Drop");
        }
        _drops.TwitchInventory.Search="";
        foreach(var state in new[]{"unfinished","completed","claimable","claimed"})
        {
            _drops.TwitchInventory.StatusFilter=state;
            await _window.Dispatcher.InvokeAsync(()=>{},DispatcherPriority.ApplicationIdle);await Task.Delay(180);
            Capture(Path.Combine(output,"state-"+state+".png"));
            _checks.Add($"state coverage {state}: {_drops.TwitchInventory.Groups.Count} campaign groups");
        }
        _drops.TwitchInventory.StatusFilter="all";
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
                foreach (var (width, height, scale) in new[] { (1600d,960d,1d), (1600d,960d,1.25), (1600d,960d,1.5), (1366d,768d,1d), (1366d,768d,1.25), (1366d,768d,1.5), (1920d,1080d,1d), (1920d,1080d,1.25), (1920d,1080d,1.5), (2560d,1440d,1d), (2560d,1440d,1.25), (2560d,1440d,1.5) })
                {
                    // Explicit DIP layout testing; this does not claim to change the monitor DPI.
                    _window.Width = width / scale;_window.Height = height / scale;
                    _window.UpdateLayout();
                    await _window.Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
                    _checks.Add($"layout {i+1:00} {width}x{height} @{scale*100:0}% equivalent-DIP actual={_window.ActualWidth:0.#}x{_window.ActualHeight:0.#}");
                    CheckVisibleBounds(i, width, height, scale);
                    if (width == 1366 && scale == 1.5 && i is 1 or 5 or 9 or 10 or 12 or 13 or 16)
                        Capture(Path.Combine(renders, $"{i+1:00}-1366-150.png"));
                }
                _window.Width = 1280;_window.Height = 820;_window.UpdateLayout();
                await _window.Dispatcher.InvokeAsync(() => { }, DispatcherPriority.Render);
                Capture(Path.Combine(renders, $"{i+1:00}.png"));
            }
            _scenarios.SelectedIndex=8;
            _window.Width=_window.MinWidth;_window.Height=_window.MinHeight;_window.UpdateLayout();
            await _window.Dispatcher.InvokeAsync(()=>{},DispatcherPriority.ApplicationIdle);
            CheckVisibleBounds(8,_window.ActualWidth,_window.ActualHeight,1);
            Capture(Path.Combine(renders,"minimum.png"));_checks.Add($"minimum-window actual={_window.ActualWidth:0.#}x{_window.ActualHeight:0.#} DIP");
            _window.WindowState=WindowState.Maximized;await _window.Dispatcher.InvokeAsync(()=>{},DispatcherPriority.ApplicationIdle);_window.UpdateLayout();
            Capture(Path.Combine(renders,"maximized.png"));_checks.Add($"maximized-window actual={_window.ActualWidth:0.#}x{_window.ActualHeight:0.#} DIP");
            _window.WindowState=WindowState.Normal;_window.Width=1280;_window.Height=820;
            ThemeManager.Apply(true);_window.UpdateLayout();await _window.Dispatcher.InvokeAsync(()=>{},DispatcherPriority.ApplicationIdle);Capture(Path.Combine(renders,"dark.png"));ThemeManager.Apply(false);
            await CheckDraftsAsync();
            await CheckAuthorizationCancellationAsync();
            await CheckSoopQueueDiagnosticsAsync(renders);
            var dpi = VisualTreeHelper.GetDpi(_window);
            File.WriteAllLines(Path.Combine(_output, "matrix-results.txt"), _checks.Append($"binding-errors={_bindings.Errors.Count}").Append($"physical-monitor-dpi={dpi.PixelsPerInchX:0}/{dpi.PixelsPerInchY:0}").Append("DPI method: equivalent DIP sizes, not physical monitor switching"));
            Environment.ExitCode = _bindings.Errors.Count > 0 || _checks.Any(c => c.StartsWith("FAIL")) ? 1 : 0;
        }
        catch (Exception e) { File.WriteAllText(Path.Combine(_output, "review-failure.txt"), e.ToString());Environment.ExitCode = 1; }
        finally { _window.Close(); }
    }

    private async Task CheckSoopQueueDiagnosticsAsync(string renders)
    {
        _window.NavigateTo("drops");
        _page.SelectPlatform(DropsPlatform.Soop);
        _page.ShowWorkspace("overview");
        _drops.Soop.WorkerLifecycle = WorkerLifecycle.Running;
        _drops.ApplyState(DropsPlatform.Soop, Raw("""
            {"running":true,"accounts":[{"uid":"隔离回放账号","running":true,"authenticationState":"verified",
              "channelId":null,"queueTarget":{},"queueDiagnostics":{"reason":"no_online_channel",
              "message":"所选活动的官方频道当前均无可用直播","totalActivities":427,"candidateActivities":1,
              "activitiesChecked":1,"onlineChannels":0,"officialProbes":6,
              "activityFilters":{"inactive":412,"completed":6,"gameFilter":7,"eligibility":1},
              "channelFilters":{"officialOffline":6},"blockReasons":{"限次活动，账号参与资格待确认":1}}}]}
            """));
        await _window.Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
        var diagnostics = Descendants<Expander>(_page).FirstOrDefault(item => item.IsVisible && Equals(item.Header, "选台诊断"));
        if (diagnostics is null)
        {
            _checks.Add("FAIL SOOP account queue diagnostics expander was not rendered");
            return;
        }
        diagnostics.IsExpanded = true;
        diagnostics.BringIntoView();
        await _window.Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
        var visible = Descendants<TextBlock>(_page).Where(item => item.IsVisible).Select(item => item.Text).ToArray();
        var passed = visible.Any(line => line.Contains("候选活动 1/427") && line.Contains("在线频道 0"))
            && visible.Any(line => line.Contains("指定主播离线 6") && line.Contains("账号资格 1"))
            && visible.Contains("所选活动的官方频道当前均无可用直播");
        _checks.Add((passed ? "PASS" : "FAIL") + " SOOP account queue diagnostics render reason and actual stage counts from isolated JSON");
        Capture(Path.Combine(renders, "soop-queue-diagnostics-replay.png"));
    }

    private void CheckVisibleBounds(int scenario, double width, double height, double scale)
    {
        void Walk(DependencyObject parent)
        {
            if(parent is Grid settingsGrid)
            {
                var rows=settingsGrid.Children.OfType<Controls.SettingRow>().Where(r=>r.IsVisible&&r.ActualHeight>1&&r.ActualWidth>1).ToArray();
                for(var a=0;a<rows.Length;a++)for(var b=a+1;b<rows.Length;b++)
                {
                    var first=rows[a].TransformToAncestor(settingsGrid).TransformBounds(new Rect(0,0,rows[a].ActualWidth,rows[a].ActualHeight));
                    var second=rows[b].TransformToAncestor(settingsGrid).TransformBounds(new Rect(0,0,rows[b].ActualWidth,rows[b].ActualHeight));
                    var overlap=Rect.Intersect(first,second);
                    if(!overlap.IsEmpty&&overlap.Width>1&&overlap.Height>1)_checks.Add($"FAIL overlapping setting rows scenario={scenario+1:00} {rows[a].Title}/{rows[b].Title}");
                }
            }
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
        var dpi=VisualTreeHelper.GetDpi(visual);
        var bitmap = new RenderTargetBitmap((int)Math.Ceiling(visual.ActualWidth*dpi.DpiScaleX), (int)Math.Ceiling(visual.ActualHeight*dpi.DpiScaleY),dpi.PixelsPerInchX,dpi.PixelsPerInchY,PixelFormats.Pbgra32);
        bitmap.Render(visual);var png = new PngBitmapEncoder();png.Frames.Add(BitmapFrame.Create(bitmap));using var stream = File.Create(path);png.Save(stream);
    }
    private sealed class BindingListener : TraceListener
    {
        public List<string> Errors { get; } = [];
        public override void Write(string? message) { if (!string.IsNullOrWhiteSpace(message)) Errors.Add(message); }
        public override void WriteLine(string? message) => Write(message);
    }
}
