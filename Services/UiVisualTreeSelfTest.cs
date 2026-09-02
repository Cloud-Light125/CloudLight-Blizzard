using System.Reflection;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using Ellipse = System.Windows.Shapes.Ellipse;
using InlineRun = System.Windows.Documents.Run;
using System.Windows.Media;
using System.Windows.Media.Media3D;
using System.Windows.Threading;
using CloudLightBlizzard.Services.Drops;
using CloudLightBlizzard.Services.OverwatchRegion;
using CloudLightBlizzard.Views;
using CloudLightBlizzard.Views.Pages;
using CloudLightBlizzard.ViewModels;
using RegionKind = CloudLightBlizzard.Services.OverwatchRegion.OverwatchRegion;
using CurrentRegionKind = CloudLightBlizzard.Services.OverwatchRegion.CurrentGameRegion;

namespace CloudLightBlizzard.Services;

/// <summary>
/// A deterministic UI smoke test for environments where computer-use cannot read a WPF window.
/// It instantiates the real pages, walks their visual trees, and verifies the navigation surface.
/// It never clicks destructive controls or starts long-running services.
/// </summary>
public static class UiVisualTreeSelfTest
{
    public static int Run(string outputPath)
    {
        var checks = new List<string>();
        MainWindow? window = null;
        SwitchPreviewWindow? preview = null;
        TraceSource? bindingSource = null;
        TraceListener? bindingListener = null;
        SourceLevels? previousBindingLevel = null;
        DropsHostService? requestOverrideHost = null;
        DispatcherUnhandledExceptionEventHandler? dispatcherUnhandledHandler = null;
        var dispatcherExceptions = new List<Exception>();
        string? settingsPath = null;
        FileSnapshot? settingsSnapshot = null;
        try
        {
            settingsPath = AppSettings.FilePath;
            settingsSnapshot = CaptureFile(settingsPath);
            File.WriteAllText(settingsPath, "{\"LastMainSection\":\"overview\"}");
            bindingSource = PresentationTraceSources.DataBindingSource;
            previousBindingLevel = bindingSource.Switch.Level;
            bindingListener = new BindingErrorTraceListener();
            bindingSource.Switch.Level = SourceLevels.Error;
            bindingSource.Listeners.Add(bindingListener);
            dispatcherUnhandledHandler = (_, args) =>
            {
                dispatcherExceptions.Add(args.Exception);
                args.Handled = true;
            };
            Application.Current.DispatcherUnhandledException += dispatcherUnhandledHandler;
            window = new MainWindow(startHidden: true);
            Application.Current.MainWindow = window;
            requestOverrideHost = typeof(MainWindow).GetField("_vm", BindingFlags.Instance | BindingFlags.NonPublic)
                ?.GetValue(window) is MainViewModel mainViewModel ? mainViewModel.DropsHost : null;
            requestOverrideHost?.ConfigureRequestOverrideForSelfTest((_, _, _, _) =>
                Task.FromResult(JsonSerializer.SerializeToElement(new { })));
            window.ShowActivated = false;
            window.Opacity = 0;
            window.ApplyTemplate();
            window.Measure(new Size(1440, 960));
            window.Arrange(new Rect(0, 0, 1440, 960));
            window.UpdateLayout();

            var navigation = new[]
            {
                (Name: "AccountsNav", Tag: "accounts"),
                (Name: "RegionNav", Tag: "region"),
                (Name: "StatsNav", Tag: "stats"),
                (Name: "DropsNav", Tag: "drops"),
                (Name: "SnapshotsNav", Tag: "snapshots"),
                (Name: "DiagnosticsNav", Tag: "diagnostics"),
                (Name: "SettingsNav", Tag: "settings"),
                (Name: "AboutNav", Tag: "about"),
            };
            foreach (var item in navigation)
            {
                var control = FindNamed(window, item.Name) as RadioButton;
                Assert(control is not null && string.Equals(control.Tag as string, item.Tag, StringComparison.Ordinal),
                    checks, $"导航 {item.Tag} 存在且 Tag 正确");
            }
            Assert(FindNamed(window, "OverviewNav") is null &&
                   typeof(MainWindow).GetField("_overviewPage", BindingFlags.Instance | BindingFlags.NonPublic) is null,
                checks, "概览导航和 MainWindow 概览页面生命周期已完全移除");

            var pageHost = FindNamed(window, "PageHost") as ContentControl;
            var accountFooter = FindNamed(window, "AccountFooter") as Border;
            var windowViewModel = typeof(MainWindow).GetField("_vm", BindingFlags.Instance | BindingFlags.NonPublic)
                ?.GetValue(window) as MainViewModel;
            var accountsPage = typeof(MainWindow).GetField("_accountsPage", BindingFlags.Instance | BindingFlags.NonPublic)
                ?.GetValue(window) as AccountsPage;
            Assert(pageHost is not null, checks, "主窗口包含单一 PageHost");
            Assert(windowViewModel?.Settings.LastMainSection == "accounts" &&
                   FindNamed(window, "AccountsNav") is RadioButton { IsChecked: true } &&
                   ReferenceEquals(pageHost?.Content, accountsPage),
                checks, "旧 LastMainSection=overview 安全迁移到 accounts 并默认打开账号页面");
            Assert(accountFooter?.Visibility == Visibility.Visible,
                checks, "AccountFooter 在账号页面可见");
            Assert(window.MinWidth >= 1000 && window.MinHeight >= 660, checks, "主窗口设置了窄窗口安全最小尺寸");

            var pages = new[]
            {
                (Field: "_accountsPage", Nav: "AccountsNav", Tag: "accounts", Name: "账号"),
                (Field: "_regionPage", Nav: "RegionNav", Tag: "region", Name: "区服切换"),
                (Field: "_statsPage", Nav: "StatsNav", Tag: "stats", Name: "战绩"),
                (Field: "_dropsPage", Nav: "DropsNav", Tag: "drops", Name: "Drops"),
                (Field: "_snapshotsPage", Nav: "SnapshotsNav", Tag: "snapshots", Name: "区服快照"),
                (Field: "_diagnosticsPage", Nav: "DiagnosticsNav", Tag: "diagnostics", Name: "诊断中心"),
                (Field: "_settingsPage", Nav: "SettingsNav", Tag: "settings", Name: "设置"),
                (Field: "_aboutPage", Nav: "AboutNav", Tag: "about", Name: "关于"),
            };
            foreach (var item in pages)
            {
                var page = typeof(MainWindow).GetField(item.Field, BindingFlags.Instance | BindingFlags.NonPublic)
                    ?.GetValue(window) as UserControl;
                Assert(page is not null && page.IsInitialized, checks, $"{item.Name} 页面已初始化");
                if (item.Name is not ("战绩" or "设置"))
                    Assert(page?.DataContext is not null, checks, $"{item.Name} 页面绑定了 ViewModel");
                else
                    Assert(page is not null, checks, $"{item.Name} 页面由现有 code-behind 状态管理");
                page?.ApplyTemplate();
                page?.Measure(new Size(1400, 900));
                page?.Arrange(new Rect(0, 0, 1400, 900));
                Assert(page is not null && ContainsVisual(page, value => value is ScrollViewer), checks,
                    $"{item.Name} 页面包含可滚动视觉树");
            }

            if (pageHost is not null)
            {
                foreach (var item in pages)
                {
                    var page = typeof(MainWindow).GetField(item.Field, BindingFlags.Instance | BindingFlags.NonPublic)
                        ?.GetValue(window) as UserControl;
                    var nav = FindNamed(window, item.Nav) as RadioButton;
                    if (page is null || nav is null) continue;
                    window.Dispatcher.Invoke(() =>
                    {
                        nav.IsChecked = false;
                        nav.IsChecked = true;
                    }, DispatcherPriority.Input);
                    DrainDispatcher(window.Dispatcher);
                    Assert(ReferenceEquals(pageHost.Content, page),
                        checks, $"真实导航 {item.Tag} 将 {item.Name} 页面放入 PageHost");
                    Assert((item.Tag == "accounts") == (accountFooter?.Visibility == Visibility.Visible),
                        checks, $"真实导航 {item.Tag} 的 AccountFooter 可见性正确");
                }
            }

            var regionPage = typeof(MainWindow).GetField("_regionPage", BindingFlags.Instance | BindingFlags.NonPublic)
                ?.GetValue(window) as RegionFilesPage;
            var manualRegionValidationUiPresent = regionPage is not null &&
                FindVisual(regionPage, value => value is Button button &&
                    button.Content is string content && new[]
                    {
                        "检查备份完整性", "执行第四步验证", "恢复第四步提醒",
                    }.Contains(content, StringComparer.Ordinal)) is not null;
            Assert(!manualRegionValidationUiPresent,
                checks, "区服文件页已移除手动完整性验证和第四步验证入口");

            var aboutPage = typeof(MainWindow).GetField("_aboutPage", BindingFlags.Instance | BindingFlags.NonPublic)
                ?.GetValue(window) as AboutPage;
            var componentsList = aboutPage is null ? null : FindNamed(aboutPage, "ThirdPartyComponentsList") as ItemsControl;
            Assert(componentsList is not null && componentsList.Items.Count >= 6,
                checks, $"关于页第三方组件列表已实例化（实际 {componentsList?.Items.Count ?? 0} 项）");
            Assert(aboutPage is not null &&
                   FindVisual(aboutPage, value => value is TextBlock text && text.Text == "BiliBiliDropsMiner") is not null &&
                   FindVisual(aboutPage, value => value is TextBlock text && text.Text == "Microsoft.Data.Sqlite") is not null &&
                   FindVisual(aboutPage, value => value is TextBlock text && text.Text == "CommunityToolkit.WinUI.Notifications") is not null,
                checks, "关于页包含 Worker、SQLite 与 Toast 组件说明");
            Assert(aboutPage is not null &&
                   FindVisual(aboutPage, value => value is Button button && button.Content as string == "打开上游项目") is not null &&
                   FindVisual(aboutPage, value => value is Button button && button.Content as string == "打开第三方说明") is not null,
                checks, "关于页第三方组件提供上游项目与第三方说明入口");

            var snapshotsPage = typeof(MainWindow).GetField("_snapshotsPage", BindingFlags.Instance | BindingFlags.NonPublic)
                ?.GetValue(window) as SnapshotsPage;
            var snapshotsVm = snapshotsPage?.DataContext as SnapshotsViewModel;
            var snapshotSummary = snapshotsPage is null ? null : FindNamed(snapshotsPage, "SnapshotSummaryCard");
            var snapshotWorkspace = snapshotsPage is null ? null : FindNamed(snapshotsPage, "SnapshotWorkspace");
            var snapshotList = snapshotsPage is null ? null : FindNamed(snapshotsPage, "SnapshotsList") as ListBox;
            var snapshotDetails = snapshotsPage is null ? null : FindNamed(snapshotsPage, "SnapshotDetailsPanel");
            var snapshotEmptyDetails = snapshotsPage is null ? null : FindNamed(snapshotsPage, "SnapshotDetailsEmptyPanel");
            var snapshotActionsPresent = false;
            var snapshotVerificationUiPresent = false;
            var snapshotRouteBindingsOneWay = false;
            if (snapshotList?.ItemTemplate is DataTemplate snapshotTemplate &&
                snapshotTemplate.LoadContent() is FrameworkElement snapshotTemplateRoot)
            {
                snapshotActionsPresent = new[] { "details", "regenerate", "open", "delete" }
                    .All(tag => FindVisual(snapshotTemplateRoot, value => value is Button button &&
                        string.Equals(button.Tag as string, tag, StringComparison.Ordinal)) is not null);
                snapshotVerificationUiPresent = FindVisual(snapshotTemplateRoot, value =>
                    value is Button button && string.Equals(button.Tag as string, "verify", StringComparison.Ordinal)) is not null ||
                    FindVisual(snapshotTemplateRoot, value => value is TextBlock text &&
                        (text.Text?.Contains("验证", StringComparison.Ordinal) == true ||
                         text.Text?.Contains("未验证", StringComparison.Ordinal) == true)) is not null;
                var routeText = FindVisual(snapshotTemplateRoot, value => value is TextBlock text &&
                    text.Inlines.OfType<InlineRun>().Any(run =>
                    {
                        var binding = BindingOperations.GetBindingBase(run, InlineRun.TextProperty) as Binding;
                        return binding?.Path?.Path is "SourceText" or "TargetText";
                    })) as TextBlock;
                var routeBindings = routeText?.Inlines.OfType<InlineRun>()
                    .Where(run =>
                    {
                        var binding = BindingOperations.GetBindingBase(run, InlineRun.TextProperty) as Binding;
                        return binding?.Path?.Path is "SourceText" or "TargetText";
                    }).ToArray() ?? [];
                snapshotRouteBindingsOneWay = routeBindings.Length == 2 && routeBindings.All(run =>
                    BindingOperations.GetBindingBase(run, InlineRun.TextProperty) is Binding
                    {
                        Mode: BindingMode.OneWay,
                    });
            }
            Assert(snapshotsPage is not null && snapshotsVm is not null && snapshotSummary is Border &&
                   snapshotWorkspace is Grid snapshotGrid && snapshotGrid.ColumnDefinitions.Count == 2 &&
                   snapshotList is not null &&
                   snapshotList.ItemTemplate is not null,
                checks, "区服快照页包含摘要区、左右工作区、列表与快照卡片模板");
            Assert(snapshotsPage is not null &&
                   FindVisual(snapshotsPage, value => value is TextBlock text && text.Text == "快照列表") is not null &&
                   FindVisual(snapshotsPage, value => value is TextBlock text && text.Text == "当前状态") is not null &&
                   FindVisual(snapshotsPage, value => value is TextBlock text && text.Text == "文件与大小") is not null &&
                   FindVisual(snapshotsPage, value => value is TextBlock text && text.Text == "路径与时间") is not null &&
                   FindVisual(snapshotsPage, value => value is TextBlock text && text.Text == "基本信息") is not null &&
                   !snapshotVerificationUiPresent &&
                   snapshotRouteBindingsOneWay &&
                   FindVisual(snapshotsPage, value => value is ProgressBar ||
                       value is Button button && button.Content as string == "取消验证") is null,
                 checks, "区服快照页摘要和详情分组标签清晰可见，路线只读绑定为 OneWay");
            Assert(snapshotDetails is Border && snapshotEmptyDetails is Border &&
                   ((snapshotsVm?.HasSelectedSnapshot == true && snapshotDetails.Visibility == Visibility.Visible) ||
                    (snapshotsVm?.HasSelectedSnapshot != true && snapshotEmptyDetails.Visibility == Visibility.Visible)),
                checks, "区服快照页按当前选择在详情与空状态之间切换");
            Assert(snapshotActionsPresent,
                checks, "区服快照卡片保留详情、重新生成、打开目录和删除操作入口，并移除验证入口");

            var dropsPage = typeof(MainWindow).GetField("_dropsPage", BindingFlags.Instance | BindingFlags.NonPublic)
                ?.GetValue(window) as DropsPage;
            var dropsVm = dropsPage?.DataContext as DropsViewModel;
            var bilibiliDetails = dropsVm?.BilibiliDetails;
            Assert(dropsPage is not null && dropsVm is not null && bilibiliDetails is not null,
                checks, "Drops 页面实例化了共享 DropsViewModel 与 BilibiliDropsViewModel");
            if (dropsPage is not null && dropsVm is not null && bilibiliDetails is not null)
            {
                // MainWindow navigation also refreshes all four providers. Keep this
                // deterministic UI test focused on the real visual/event chain by
                // holding the page's existing loading guard while the transparent
                // window is shown; no product control or visibility is overridden.
                typeof(DropsPage).GetField("_loading", BindingFlags.Instance | BindingFlags.NonPublic)
                    ?.SetValue(dropsPage, true);
                window.Show();
                window.UpdateLayout();

                var dropsNav = FindNamed(window, "DropsNav") as RadioButton;
                var soopTab = FindNamed(dropsPage, "SoopTab") as RadioButton;
                var youtubeTab = FindNamed(dropsPage, "YouTubeTab") as RadioButton;
                var twitchTab = FindNamed(dropsPage, "TwitchTab") as RadioButton;
                var bilibiliTab = FindNamed(dropsPage, "BilibiliTab") as RadioButton;
                var youtubeStartButton = FindNamed(dropsPage, "YouTubeStartButton");
                var youtubeStopButton = FindNamed(dropsPage, "YouTubeStopButton");
                var initializedField = typeof(DropsPage).GetField("_initialized", BindingFlags.Instance | BindingFlags.NonPublic);
                var platformField = typeof(DropsPage).GetField("_platform", BindingFlags.Instance | BindingFlags.NonPublic);
                var initializedBeforeClick = initializedField?.GetValue(dropsPage) is true;
                Assert(pageHost is not null && dropsNav is not null &&
                       soopTab is not null && youtubeTab is not null && twitchTab is not null && bilibiliTab is not null,
                    checks, "真实 MainWindow 包含 PageHost、SOOP Tab 与哔哩哔哩 Tab");
                Assert(dropsVm.SelectedPlatform == DropsPlatform.Soop && initializedBeforeClick,
                    checks, "真实页面初始化完成且初始平台为 SOOP");

                if (pageHost is not null && dropsNav is not null &&
                    soopTab is not null && youtubeTab is not null && twitchTab is not null && bilibiliTab is not null)
                {
                    // Exercise the actual MainWindow navigation event chain after the
                    // main navigation loop has visited every supported page.
                    window.Dispatcher.Invoke(() =>
                    {
                        dropsNav.IsChecked = false;
                        dropsNav.IsChecked = true;
                    }, DispatcherPriority.Input);
                    DrainDispatcher(window.Dispatcher);
                    Assert(ReferenceEquals(pageHost.Content, dropsPage), checks,
                        "MainWindow 真实导航将自身持有的 DropsPage 放入 PageHost");

                    dropsPage.ApplyTemplate();
                    dropsPage.Measure(new Size(1400, 900));
                    dropsPage.Arrange(new Rect(0, 0, 1400, 900));
                    dropsPage.UpdateLayout();

                    var routedChecked = false;
                    RoutedEventHandler checkedHandler = (_, args) =>
                    {
                        if (ReferenceEquals(args.OriginalSource, bilibiliTab)) routedChecked = true;
                    };
                    dropsPage.AddHandler(ToggleButton.CheckedEvent, checkedHandler, handledEventsToo: true);
                    try
                    {
                        // Force a false -> true transition so this test always exercises
                        // RadioButton.Checked even when a previous navigation selected the tab.
                        dropsPage.Dispatcher.Invoke(() =>
                        {
                            bilibiliTab.IsChecked = false;
                            bilibiliTab.IsChecked = true;
                        }, DispatcherPriority.Input);
                        DrainDispatcher(dropsPage.Dispatcher);
                    }
                    finally { dropsPage.RemoveHandler(ToggleButton.CheckedEvent, checkedHandler); }

                    var platformAfterClick = platformField?.GetValue(dropsPage) is DropsPlatform value ? value : (DropsPlatform?)null;
                    Assert(routedChecked, checks, "BilibiliTab.IsChecked=true 触发并冒泡了真实 Checked RoutedEvent");
                    Assert(platformAfterClick == DropsPlatform.Bilibili, checks,
                        $"Checked 事件链更新 DropsPage._platform（实际={platformAfterClick?.ToString() ?? "null"}）");
                    Assert(dropsVm.SelectedPlatform == DropsPlatform.Bilibili, checks,
                        "Checked 事件链更新 DropsViewModel.SelectedPlatform=Bilibili");
                }

                dropsPage.ApplyTemplate();
                dropsPage.Measure(new Size(1400, 900));
                dropsPage.Arrange(new Rect(0, 0, 1400, 900));
                dropsPage.UpdateLayout();
                var bilibiliPanel = FindNamed(dropsPage, "BilibiliPanel");
                var bilibiliAccount = FindNamed(dropsPage, "BilibiliAccountCard");
                var bilibiliQrLogin = FindNamed(dropsPage, "BilibiliQrLoginButton");
                var bilibiliQuickStart = FindNamed(dropsPage, "BilibiliQuickStartCard");
                var bilibiliNetwork = FindNamed(dropsPage, "BilibiliNetworkCard");
                var bilibiliUseProxy = FindNamed(dropsPage, "BilibiliUseProxyCheck");
                var bilibiliSettings = FindNamed(dropsPage, "BilibiliSettingsCard");
                var bilibiliActivityList = FindNamed(dropsPage, "BilibiliActivityList");
                var dropsHeader = FindNamed(dropsPage, "DropsPageHeader");
                var dropsNetworkSummary = FindNamed(dropsPage, "DropsNetworkSummary");
                var platformTabsBar = FindNamed(dropsPage, "PlatformTabsBar");
                var platformStatusDots = new[]
                {
                    FindNamed(dropsPage, "SoopStatusDot"),
                    FindNamed(dropsPage, "YouTubeStatusDot"),
                    FindNamed(dropsPage, "TwitchStatusDot"),
                    FindNamed(dropsPage, "BilibiliStatusDot"),
                };
                var soopPanel = FindNamed(dropsPage, "SoopPanel");
                var youtubePanel = FindNamed(dropsPage, "YouTubePanel");
                var twitchPanel = FindNamed(dropsPage, "TwitchPanel");
                Assert(dropsHeader is not null && dropsNetworkSummary is Border networkSummary &&
                       networkSummary.ActualHeight > 0 && platformTabsBar is Grid tabsGrid &&
                       tabsGrid.ColumnDefinitions.Count == 4,
                    checks, "掉宝页标题、独立网络摘要行和四列平台 Tab 均存在");
                Assert(platformStatusDots.All(value => value is Ellipse { Style: not null, IsVisible: true }),
                    checks, "掉宝页四个平台标题均包含可见状态点和统一状态样式");
                Assert(FindVisual(dropsPage, value => value is UniformGrid grid && grid.Columns >= 4) is null,
                    checks, "掉宝页顶部四个大平台卡片已从视觉树移除");
                var proxyRowBelowHeader = false;
                try
                {
                    if (dropsHeader is FrameworkElement header && dropsNetworkSummary is FrameworkElement network &&
                        header.ActualHeight > 0 && network.ActualHeight > 0)
                    {
                        var headerBottom = header.TranslatePoint(new Point(0, header.ActualHeight), dropsPage).Y;
                        var networkTop = network.TranslatePoint(new Point(0, 0), dropsPage).Y;
                        proxyRowBelowHeader = networkTop >= headerBottom - 0.5;
                    }
                }
                catch { }
                Assert(proxyRowBelowHeader, checks, "掉宝页网络代理摘要位于标题区下方且不与右上操作区重叠");
                Assert(bilibiliPanel is StackPanel && bilibiliPanel.Visibility == Visibility.Visible,
                    checks, "Checked 事件链更新 BilibiliPanel.Visibility=Visible");
                Assert(bilibiliPanel is StackPanel && bilibiliPanel.IsVisible && bilibiliPanel.ActualHeight > 0,
                    checks, "切换到哔哩哔哩后 BilibiliPanel 在真实窗口中可见且有布局高度");
                Assert(bilibiliAccount is Border && bilibiliAccount.IsVisible && bilibiliAccount.ActualHeight > 0,
                    checks, "未登录状态下 BilibiliAccountCard 在真实窗口中可见且有布局高度");
                Assert(!bilibiliDetails.LoggedIn && bilibiliQrLogin is Button &&
                       bilibiliQrLogin.Visibility == Visibility.Visible && bilibiliQrLogin.IsVisible &&
                       VisualPath(dropsPage, bilibiliQrLogin) is not null,
                    checks, "未登录状态下扫码登录按钮存在于有效视觉树");
                var removedRecoveryLabels = new[]
                {
                    "自愈状态", "最近心跳", "最近进度", "连续失败", "自动重连次数", "下次重试", "重启 Worker",
                };
                Assert(removedRecoveryLabels.All(label =>
                        FindVisual(dropsPage, value => value is TextBlock text &&
                            string.Equals(text.Text, label, StringComparison.Ordinal)) is null),
                    checks, "Drops 页面已移除独立自愈状态区域及其 UI-only 字段/按钮");
                Assert(bilibiliQuickStart is Expander &&
                       FindVisual(bilibiliQuickStart, value => value is ItemsControl) is not null,
                    checks, "Bilibili 页面包含复用 QuickStartStepTemplate 的快速开始卡片");
                Assert(bilibiliNetwork is Border && bilibiliUseProxy is CheckBox bilibiliProxyCheck &&
                       string.Equals(bilibiliProxyCheck.Content as string,
                            "使用 CloudLight Blizzard 全局代理", StringComparison.Ordinal),
                    checks, "Bilibili 网络卡片包含正式的全局代理选项");
                Assert(bilibiliSettings is Border && bilibiliActivityList is ListBox &&
                       FindNamed(dropsPage, "BilibiliWatchModePicker") is ComboBox &&
                       FindNamed(dropsPage, "BilibiliSessionsPerRoomText") is TextBox &&
                       FindNamed(dropsPage, "BilibiliReconnectDelayText") is TextBox &&
                       FindNamed(dropsPage, "BilibiliTaskIntervalText") is TextBox &&
                       FindNamed(dropsPage, "BilibiliTaskIdsText") is TextBox &&
                       FindNamed(dropsPage, "BilibiliAutoStart") is CheckBox &&
                       FindNamed(dropsPage, "BilibiliAutoResume") is CheckBox &&
                       FindNamed(dropsPage, "BilibiliNotifierUrlBox") is PasswordBox,
                    checks, "Bilibili 精简设置仍保留活动、观看模式、任务、自动恢复、通知和网络入口");
                Assert(soopPanel is StackPanel && soopPanel.Visibility == Visibility.Collapsed && !soopPanel.IsVisible &&
                       youtubePanel is StackPanel && youtubePanel.Visibility == Visibility.Collapsed && !youtubePanel.IsVisible &&
                       twitchPanel is StackPanel && twitchPanel.Visibility == Visibility.Collapsed && !twitchPanel.IsVisible,
                    checks, "选择哔哩哔哩后其它三个平台面板均被正确隐藏");
                checks.Add($"PASS Drops navigation state: SelectedPlatform={dropsVm.SelectedPlatform} BilibiliTabChecked={bilibiliTab?.IsChecked} BilibiliPanelVisibility={bilibiliPanel?.Visibility} BilibiliPanelIsVisible={bilibiliPanel?.IsVisible} BilibiliPanelHeight={bilibiliPanel?.ActualHeight:0.##}");
                if (soopTab is RadioButton soopPlatformTab && soopPanel is StackPanel soopPlatformPanel &&
                    youtubeTab is RadioButton youtubePlatformTab && youtubePanel is StackPanel youtubePlatformPanel &&
                    twitchTab is RadioButton twitchPlatformTab && twitchPanel is StackPanel twitchPlatformPanel &&
                    bilibiliTab is RadioButton bilibiliPlatformTab && bilibiliPanel is StackPanel bilibiliPlatformPanel)
                {
                    var platformViews = new[]
                    {
                        (Platform: DropsPlatform.Soop, Tab: soopPlatformTab, Panel: soopPlatformPanel),
                        (Platform: DropsPlatform.YouTube, Tab: youtubePlatformTab, Panel: youtubePlatformPanel),
                        (Platform: DropsPlatform.Twitch, Tab: twitchPlatformTab, Panel: twitchPlatformPanel),
                        (Platform: DropsPlatform.Bilibili, Tab: bilibiliPlatformTab, Panel: bilibiliPlatformPanel),
                    };
                    foreach (var selected in platformViews)
                    {
                        dropsPage.Dispatcher.Invoke(() =>
                        {
                            selected.Tab.IsChecked = false;
                            selected.Tab.IsChecked = true;
                        }, DispatcherPriority.Input);
                        DrainDispatcher(dropsPage.Dispatcher);
                        Assert(selected.Tab.IsChecked == true && dropsVm.SelectedPlatform == selected.Platform,
                            checks, $"真实平台 Tab 切换到 {selected.Platform} 后状态同步");
                        Assert(selected.Panel.Visibility == Visibility.Visible && selected.Panel.IsVisible && selected.Panel.ActualHeight > 0,
                            checks, $"真实平台 {selected.Platform} 内容可见且有布局高度");
                        foreach (var other in platformViews.Where(item => item.Platform != selected.Platform))
                            Assert(other.Panel.Visibility == Visibility.Collapsed && !other.Panel.IsVisible,
                                checks, $"选择 {selected.Platform} 后 {other.Platform} 内容隐藏");
                    }
                }
                if (bilibiliPanel is not null)
                {
                    var bindingExpression = BindingOperations.GetBindingExpression(
                        bilibiliPanel, UIElement.VisibilityProperty);
                    Assert(bindingExpression is not null &&
                           string.Equals(bindingExpression.ParentBinding.Path?.Path,
                               "BilibiliPanelVisibility", StringComparison.Ordinal) &&
                           bindingExpression.DataItem is DropsViewModel &&
                           !bindingExpression.HasError,
                        checks, "BilibiliPanel.Visibility Binding 指向当前 DropsViewModel.BilibiliPanelVisibility 且无错误");
                    Assert(bilibiliPanel.DataContext is DropsViewModel,
                        checks, "BilibiliPanel.DataContext 是 DropsViewModel");
                }
                Assert(FindNamed(dropsPage, "BilibiliAccountCard") is Border &&
                       FindNamed(dropsPage, "BilibiliQrCard") is Border,
                    checks, "Bilibili 账号卡片和二维码区域存在于实际内容树");
                Assert(FindNamed(dropsPage, "BilibiliQrLoginButton") is Button &&
                       FindNamed(dropsPage, "BilibiliQrCancelButton") is Button &&
                       FindNamed(dropsPage, "BilibiliStartButton") is Button &&
                       FindNamed(dropsPage, "BilibiliStopButton") is Button &&
                       FindNamed(dropsPage, "BilibiliRefreshButton") is Button &&
                       FindNamed(dropsPage, "BilibiliDiscoverButton") is Button,
                    checks, "Bilibili 账号、扫码、开始停止、刷新和活动发现控件存在");
                Assert(youtubeStartButton is Button && youtubeStopButton is Button &&
                       string.Equals((youtubeStartButton as Button)?.Tag as string, "YouTube", StringComparison.Ordinal) &&
                       string.Equals((youtubeStopButton as Button)?.Tag as string, "YouTube", StringComparison.Ordinal),
                    checks, "删除顶部平台卡片后 YouTube 开始/停止观看入口仍保留在页面标题操作区");
                Assert(FindNamed(dropsPage, "BilibiliRoomList") is ListBox &&
                       FindNamed(dropsPage, "BilibiliTaskList") is ListBox &&
                       FindNamed(dropsPage, "BilibiliSessionList") is ListBox &&
                       FindNamed(dropsPage, "BilibiliAutoTaskProgress") is CheckBox &&
                       bilibiliDetails.ScanQrLoginCommand is not null &&
                       bilibiliDetails.StartCommand is not null &&
                       bilibiliDetails.AddRoomCommand is not null &&
                       bilibiliDetails.ClaimRewardCommand is not null,
                    checks, "Bilibili 房间、官方任务、Session 列表和关键 VM Command 可绑定");
                var twitchSettings = FindNamed(dropsPage, "TwitchSettingsCard");
                Assert(twitchSettings is Border && twitchSettings.Visibility == Visibility.Collapsed,
                    checks, "哔哩哔哩选中时不会显示 Twitch 通用活动和设置卡片");

                var logTitle = FindVisual(dropsPage, value => value is TextBlock text && text.Text == "运行日志");
                var contentOrder = bilibiliPanel is not null && logTitle is not null
                    ? CompareVisualOrder(bilibiliPanel, logTitle)
                    : null;
                Assert(contentOrder is { IsAfter: true }, checks,
                    $"运行日志区域位于真实 Bilibili 内容之后（panelPath={contentOrder?.FirstPath ?? "?"}, logPath={contentOrder?.SecondPath ?? "?"}）");

                var roomList = FindNamed(dropsPage, "BilibiliRoomList") as ListBox;
                var roomInput = FindNamed(dropsPage, "BilibiliRoomInput") as TextBox;
                var roomNameInput = FindNamed(dropsPage, "BilibiliRoomNameInput") as TextBox;
                var addRoomButton = FindVisual(dropsPage, value =>
                    value is Button button && string.Equals(button.Content as string, "添加", StringComparison.Ordinal)) as Button;
                var bindingErrorsBeforeRoom = BindingErrorCount(bindingListener);
                var dispatcherErrorsBeforeRoom = dispatcherExceptions.Count;
                const long testRoomId = 24681357;
                var testRoomUrl = $"https://live.bilibili.com/{testRoomId}";
                var mockCommands = new List<string>();
                object? addRoomPayload = null;

                requestOverrideHost?.ConfigureRequestOverrideForSelfTest((platform, command, payload, _) =>
                {
                    mockCommands.Add(command);
                    if (command == "add_room") addRoomPayload = payload;
                    var hasRoom = command is "add_room" or "set_room_enabled";
                    var rooms = hasRoom
                        ? new object[]
                        {
                            new
                            {
                                id = testRoomId,
                                name = "真实回归测试直播间",
                                url = testRoomUrl,
                                enabled = command == "add_room",
                                liveStatus = "1",
                            },
                        }
                        : Array.Empty<object>();
                    return Task.FromResult(JsonSerializer.SerializeToElement(new { rooms }));
                });

                if (roomInput is not null) roomInput.Text = testRoomUrl;
                if (roomNameInput is not null) roomNameInput.Text = "真实回归测试直播间";
                Assert(requestOverrideHost is not null && roomInput is not null && roomNameInput is not null &&
                       string.Equals(bilibiliDetails.RoomReference, testRoomUrl, StringComparison.Ordinal) &&
                       string.Equals(bilibiliDetails.RoomName, "真实回归测试直播间", StringComparison.Ordinal),
                    checks, "真实 Bilibili 添加控件通过 TwoWay Binding 写入 URL 和房间名称");
                Assert(addRoomButton is not null && addRoomButton.Command == bilibiliDetails.AddRoomCommand &&
                       addRoomButton.Command.CanExecute(addRoomButton.CommandParameter),
                    checks, "真实 Bilibili 添加按钮绑定 AddRoomCommand 且可执行");
                if (addRoomButton?.Command is not null)
                    addRoomButton.Command.Execute(addRoomButton.CommandParameter);

                var addCompleted = WaitForDispatcherCondition(dropsPage.Dispatcher,
                    () => bilibiliDetails.Rooms.Count == 1 && string.IsNullOrWhiteSpace(bilibiliDetails.RoomReference),
                    TimeSpan.FromSeconds(5));
                dropsPage.UpdateLayout();
                roomList?.UpdateLayout();
                var addedRoom = bilibiliDetails.Rooms.SingleOrDefault();
                var roomContainer = roomList is not null && addedRoom is not null
                    ? roomList.ItemContainerGenerator.ContainerFromItem(addedRoom) as ListBoxItem
                    : null;
                var roomNameText = roomContainer is not null
                    ? FindVisual(roomContainer, value => value is TextBlock text &&
                        string.Equals(text.Text, "真实回归测试直播间", StringComparison.Ordinal)) as TextBlock
                    : null;
                var roomInfo = roomContainer is not null
                    ? FindVisual(roomContainer, value => value is TextBlock text &&
                        text.Inlines.OfType<InlineRun>().Any(run => run.Text == "Room ID：")) as TextBlock
                    : null;
                var roomInlineText = roomInfo is null
                    ? ""
                    : string.Concat(roomInfo.Inlines.OfType<InlineRun>().Select(run => run.Text));
                var displayRuns = roomInfo?.Inlines.OfType<InlineRun>()
                    .Where(run => run.Text == addedRoom?.RoomIdText ||
                                  run.Text == addedRoom?.Status ||
                                  run.Text == addedRoom?.SessionText)
                    .ToArray() ?? [];
                var roomCheckBox = roomContainer is not null
                    ? FindVisual(roomContainer, value => value is CheckBox) as CheckBox
                    : null;
                var roomDeleteButton = roomContainer is not null
                    ? FindVisual(roomContainer, value => value is Button button &&
                        string.Equals(button.Content as string, "删除", StringComparison.Ordinal)) as Button
                    : null;
                var enabledBinding = roomCheckBox is not null
                    ? BindingOperations.GetBindingExpression(roomCheckBox, ToggleButton.IsCheckedProperty)
                    : null;
                Assert(addCompleted && mockCommands.Contains("add_room", StringComparer.Ordinal) &&
                       addRoomPayload is not null && JsonSerializer.Serialize(addRoomPayload).Contains(testRoomUrl, StringComparison.Ordinal),
                    checks, "AddRoomCommand → mock Worker 返回房间 → Rooms collection 增加");
                Assert(roomContainer is not null && roomContainer.ContentTemplate is not null && roomInfo is not null &&
                       roomNameText is not null && roomNameText.IsVisible && roomInfo.IsVisible &&
                       roomInlineText.Contains(testRoomId.ToString(), StringComparison.Ordinal) &&
                       roomInlineText.Contains("直播中", StringComparison.Ordinal) &&
                       roomInlineText.Contains("—", StringComparison.Ordinal),
                    checks, "真实 BilibiliRoomRowTemplate 已实例化并完成 Measure/Arrange/Render，房间名称、Room ID、状态和 Session 状态可见");
                Assert(displayRuns.Length == 3 && displayRuns.All(run =>
                        BindingOperations.GetBindingExpression(run, InlineRun.TextProperty)?.ParentBinding.Mode == BindingMode.OneWay),
                    checks, "Bilibili 房间行的 RoomIdText、Status、SessionText Run.Text 均为 OneWay");
                Assert(roomCheckBox is not null && roomCheckBox.IsChecked == true &&
                       roomCheckBox.Command == bilibiliDetails.SetRoomEnabledCommand &&
                       roomCheckBox.CommandParameter is BilibiliRoomViewModel &&
                       enabledBinding?.ParentBinding.Mode == BindingMode.TwoWay,
                    checks, "启用 CheckBox 保留合理的 TwoWay Binding，并绑定 SetRoomEnabledCommand");
                Assert(roomDeleteButton is not null && roomDeleteButton.Command == bilibiliDetails.RemoveRoomCommand &&
                       roomDeleteButton.CommandParameter is BilibiliRoomViewModel,
                    checks, "删除按钮已在真实房间模板中实例化并绑定 RemoveRoomCommand");
                Assert(BindingErrorCount(bindingListener) == bindingErrorsBeforeRoom,
                    checks, "添加并渲染 Bilibili 房间后没有新增 WPF Binding Error");
                Assert(dispatcherExceptions.Count == dispatcherErrorsBeforeRoom &&
                       !dispatcherExceptions.Skip(dispatcherErrorsBeforeRoom).Any(error =>
                           ContainsExceptionType(error, typeof(InvalidOperationException))),
                    checks, "添加并渲染 Bilibili 房间后没有 DispatcherUnhandledException/InvalidOperationException");

                if (addedRoom is not null)
                {
                    if (roomCheckBox is not null) roomCheckBox.IsChecked = false;
                    else addedRoom.Enabled = false;
                    var setCommand = roomCheckBox?.Command ?? bilibiliDetails.SetRoomEnabledCommand;
                    var setParameter = roomCheckBox?.CommandParameter ?? addedRoom;
                    setCommand.Execute(setParameter);
                }
                var disabledCompleted = WaitForDispatcherCondition(dropsPage.Dispatcher,
                    () => bilibiliDetails.Rooms.Count == 1 && !bilibiliDetails.Rooms[0].Enabled,
                    TimeSpan.FromSeconds(5));
                dropsPage.UpdateLayout();
                roomList?.UpdateLayout();
                Assert(disabledCompleted && mockCommands.Contains("set_room_enabled", StringComparer.Ordinal),
                    checks, "SetRoomEnabledCommand → mock Worker 返回停用状态 → 房间列表保持一致");

                var currentRoom = bilibiliDetails.Rooms.SingleOrDefault();
                var currentContainer = roomList is not null && currentRoom is not null
                    ? roomList.ItemContainerGenerator.ContainerFromItem(currentRoom) as ListBoxItem
                    : null;
                var currentDeleteButton = currentContainer is not null
                    ? FindVisual(currentContainer, value => value is Button button &&
                        string.Equals(button.Content as string, "删除", StringComparison.Ordinal)) as Button
                    : null;
                if (currentRoom is not null)
                {
                    var removeCommand = currentDeleteButton?.Command ?? bilibiliDetails.RemoveRoomCommand;
                    var removeParameter = currentDeleteButton?.CommandParameter ?? currentRoom;
                    removeCommand.Execute(removeParameter);
                }
                var removedCompleted = WaitForDispatcherCondition(dropsPage.Dispatcher,
                    () => bilibiliDetails.Rooms.Count == 0, TimeSpan.FromSeconds(5));
                dropsPage.UpdateLayout();
                roomList?.UpdateLayout();
                var addIndex = mockCommands.IndexOf("add_room");
                var setIndex = mockCommands.IndexOf("set_room_enabled");
                var removeIndex = mockCommands.IndexOf("remove_room");
                Assert(removedCompleted && removeIndex > setIndex && setIndex > addIndex,
                    checks, "RemoveRoomCommand → mock Worker 返回空房间 → Rooms collection 和模板正常移除");
                Assert(BindingErrorCount(bindingListener) == bindingErrorsBeforeRoom &&
                       dispatcherExceptions.Count == dispatcherErrorsBeforeRoom,
                    checks, "启用/停用/删除房间命令链未产生 Binding Error 或 DispatcherUnhandledException");
            }

            var plan = new SwitchPlan
            {
                SourceRegion = RegionKind.China,
                TargetRegion = RegionKind.International,
                CurrentRegion = CurrentRegionKind.China,
                SnapshotState = "已验证",
            };
            preview = new SwitchPreviewWindow(plan);
            preview.ApplyTemplate();
            preview.Measure(new Size(900, 700));
            preview.Arrange(new Rect(0, 0, 900, 700));
            preview.UpdateLayout();
            Assert(FindNamed(preview, "StartButton") is Button, checks, "切换预览包含开始切换按钮");
            Assert(FindNamed(preview, "BlockerBox") is Border, checks, "切换预览包含安全阻断区域");
            Assert(bindingListener is not BindingErrorTraceListener errors || !errors.HasErrors,
                checks, "WPF binding selftest 未发现 PresentationTraceSources 数据绑定错误");

        }
        catch (Exception ex)
        {
            checks.Add("FAIL 初始化 UI VisualTree 自检：" + ex.GetType().Name + "：" + ex.Message);
        }
        finally
        {
            requestOverrideHost?.ConfigureRequestOverrideForSelfTest(null);
            if (bindingSource is not null && bindingListener is not null)
            {
                try { bindingSource.Listeners.Remove(bindingListener); } catch { }
                if (previousBindingLevel.HasValue) bindingSource.Switch.Level = previousBindingLevel.Value;
            }
            try { preview?.Close(); } catch { }
            if (window is not null)
            {
                try
                {
                    typeof(MainWindow).GetMethod("BeginExit", BindingFlags.Instance | BindingFlags.NonPublic)
                        ?.Invoke(window, null);
                    typeof(MainWindow).GetMethod("CompleteExitCleanup", BindingFlags.Instance | BindingFlags.NonPublic)
                        ?.Invoke(window, null);
                }
                catch (Exception ex) { checks.Add("FAIL UI 清理：" + ex.GetBaseException().Message); }
                try { window.Close(); } catch { }
            }
            if (dispatcherUnhandledHandler is not null)
            {
                try { Application.Current.DispatcherUnhandledException -= dispatcherUnhandledHandler; }
                catch { }
            }
            if (settingsPath is not null && settingsSnapshot is not null)
            {
                try { RestoreFile(settingsPath, settingsSnapshot); }
                catch (Exception ex) { checks.Add("FAIL UI 测试恢复 settings.json：" + ex.Message); }
            }
            checks.Insert(0, "UI navigation integration selftest: " +
                (checks.All(item => item.StartsWith("PASS", StringComparison.Ordinal)) ? "PASS" : "FAIL"));
            try
            {
                var directory = Path.GetDirectoryName(Path.GetFullPath(outputPath));
                if (!string.IsNullOrWhiteSpace(directory)) Directory.CreateDirectory(directory);
                File.WriteAllLines(outputPath, checks, Encoding.UTF8);
            }
            catch { }
        }

        return checks.Any(item => item.StartsWith("FAIL", StringComparison.Ordinal)) ? 1 : 0;
    }

    private static void Assert(bool condition, ICollection<string> checks, string description) =>
        checks.Add((condition ? "PASS " : "FAIL ") + description);

    private sealed record FileSnapshot(bool Exists, byte[] Content, DateTime LastWriteTimeUtc);

    private static FileSnapshot CaptureFile(string path) =>
        File.Exists(path)
            ? new(true, File.ReadAllBytes(path), File.GetLastWriteTimeUtc(path))
            : new(false, Array.Empty<byte>(), default);

    private static void RestoreFile(string path, FileSnapshot snapshot)
    {
        if (!snapshot.Exists)
        {
            if (File.Exists(path)) File.Delete(path);
            return;
        }
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllBytes(path, snapshot.Content);
        File.SetLastWriteTimeUtc(path, snapshot.LastWriteTimeUtc);
    }

    private static int BindingErrorCount(TraceListener? listener) =>
        listener is BindingErrorTraceListener binding ? binding.ErrorCount : 0;

    private static bool WaitForDispatcherCondition(Dispatcher dispatcher, Func<bool> condition, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (!condition() && DateTime.UtcNow < deadline)
            DrainDispatcher(dispatcher);
        return condition();
    }

    private static bool ContainsExceptionType(Exception error, Type type)
    {
        for (Exception? current = error; current is not null; current = current.InnerException)
            if (type.IsInstanceOfType(current)) return true;
        return false;
    }

    private static FrameworkElement? FindNamed(FrameworkElement root, string name)
    {
        if (string.Equals(root.Name, name, StringComparison.Ordinal)) return root;
        if (root.FindName(name) is FrameworkElement named) return named;
        return FindVisual(root, value => value is FrameworkElement element &&
                                        string.Equals(element.Name, name, StringComparison.Ordinal));
    }

    private static bool ContainsVisual(DependencyObject root, Func<DependencyObject, bool> predicate) =>
        FindVisual(root, predicate) is not null;

    private static void DrainDispatcher(Dispatcher dispatcher)
    {
        var frame = new DispatcherFrame();
        dispatcher.BeginInvoke(DispatcherPriority.ContextIdle,
            new Action(() => frame.Continue = false));
        Dispatcher.PushFrame(frame);
    }

    private static (bool IsAfter, string FirstPath, string SecondPath)? CompareVisualOrder(
        DependencyObject first, DependencyObject second)
    {
        var firstAncestors = new HashSet<DependencyObject>();
        for (var current = first; current is not null; current = VisualTreeHelper.GetParent(current))
            firstAncestors.Add(current);

        DependencyObject? common = null;
        for (var current = second; current is not null; current = VisualTreeHelper.GetParent(current))
        {
            if (firstAncestors.Contains(current))
            {
                common = current;
                break;
            }
        }
        if (common is null) return null;

        var firstPath = VisualPath(common, first);
        var secondPath = VisualPath(common, second);
        if (firstPath is null || secondPath is null) return null;
        var length = Math.Min(firstPath.Count, secondPath.Count);
        for (var i = 0; i < length; i++)
        {
            if (firstPath[i] == secondPath[i]) continue;
            return (secondPath[i] > firstPath[i], FormatPath(firstPath), FormatPath(secondPath));
        }
        return (secondPath.Count > firstPath.Count, FormatPath(firstPath), FormatPath(secondPath));
    }

    private static List<int>? VisualPath(DependencyObject root, DependencyObject target)
    {
        if (ReferenceEquals(root, target)) return [];
        if (root is not Visual and not Visual3D) return null;
        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(root); index++)
        {
            var path = VisualPath(VisualTreeHelper.GetChild(root, index), target);
            if (path is null) continue;
            path.Insert(0, index);
            return path;
        }
        return null;
    }

    private static string FormatPath(IReadOnlyList<int> path) =>
        path.Count == 0 ? "root" : string.Join('/', path);

    private static FrameworkElement? FindVisual(DependencyObject root, Func<DependencyObject, bool> predicate)
    {
        if (predicate(root) && root is FrameworkElement element) return element;
        if (root is Visual or Visual3D)
        {
            for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
            {
                var found = FindVisual(VisualTreeHelper.GetChild(root, i), predicate);
                if (found is not null) return found;
            }
        }
        foreach (var child in LogicalTreeHelper.GetChildren(root).OfType<DependencyObject>())
        {
            var found = FindVisual(child, predicate);
            if (found is not null) return found;
        }
        return null;
    }

    private sealed class BindingErrorTraceListener : TraceListener
    {
        public bool HasErrors { get; private set; }
        public int ErrorCount { get; private set; }

        public override void Write(string? message) => Capture(message);
        public override void WriteLine(string? message) => Capture(message);

        private void Capture(string? message)
        {
            if (!string.IsNullOrWhiteSpace(message) &&
                message.Contains("System.Windows.Data Error", StringComparison.OrdinalIgnoreCase))
            {
                HasErrors = true;
                ErrorCount++;
            }
        }
    }
}
