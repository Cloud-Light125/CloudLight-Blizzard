using System.Reflection;
using System.IO;
using System.Text;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
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
        try
        {
            bindingSource = PresentationTraceSources.DataBindingSource;
            previousBindingLevel = bindingSource.Switch.Level;
            bindingListener = new BindingErrorTraceListener();
            bindingSource.Switch.Level = SourceLevels.Error;
            bindingSource.Listeners.Add(bindingListener);
            window = new MainWindow(startHidden: true);
            Application.Current.MainWindow = window;
            window.ShowActivated = false;
            window.Opacity = 0;
            window.ApplyTemplate();
            window.Measure(new Size(1440, 960));
            window.Arrange(new Rect(0, 0, 1440, 960));
            window.UpdateLayout();

            var navigation = new[]
            {
                (Name: "OverviewNav", Tag: "overview"),
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

            Assert(FindNamed(window, "PageHost") is ContentControl, checks, "主窗口包含单一 PageHost");
            Assert(window.MinWidth >= 1000 && window.MinHeight >= 660, checks, "主窗口设置了窄窗口安全最小尺寸");

            var pages = new[]
            {
                (Field: "_overviewPage", Name: "概览"),
                (Field: "_accountsPage", Name: "账号"),
                (Field: "_regionPage", Name: "区服切换"),
                (Field: "_statsPage", Name: "战绩"),
                (Field: "_dropsPage", Name: "Drops"),
                (Field: "_snapshotsPage", Name: "区服快照"),
                (Field: "_diagnosticsPage", Name: "诊断中心"),
                (Field: "_settingsPage", Name: "设置"),
                (Field: "_aboutPage", Name: "关于"),
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

                var pageHost = FindNamed(window, "PageHost") as ContentControl;
                var overviewNav = FindNamed(window, "OverviewNav") as RadioButton;
                var dropsNav = FindNamed(window, "DropsNav") as RadioButton;
                var soopTab = FindNamed(dropsPage, "SoopTab") as RadioButton;
                var youtubeTab = FindNamed(dropsPage, "YouTubeTab") as RadioButton;
                var twitchTab = FindNamed(dropsPage, "TwitchTab") as RadioButton;
                var bilibiliTab = FindNamed(dropsPage, "BilibiliTab") as RadioButton;
                var initializedField = typeof(DropsPage).GetField("_initialized", BindingFlags.Instance | BindingFlags.NonPublic);
                var platformField = typeof(DropsPage).GetField("_platform", BindingFlags.Instance | BindingFlags.NonPublic);
                var initializedBeforeClick = initializedField?.GetValue(dropsPage) is true;
                Assert(pageHost is not null && overviewNav is not null && dropsNav is not null &&
                       soopTab is not null && youtubeTab is not null && twitchTab is not null && bilibiliTab is not null,
                    checks, "真实 MainWindow 包含导航、PageHost、SOOP Tab 与哔哩哔哩 Tab");
                Assert(dropsVm.SelectedPlatform == DropsPlatform.Soop && initializedBeforeClick,
                    checks, "真实页面初始化完成且初始平台为 SOOP");

                if (pageHost is not null && overviewNav is not null && dropsNav is not null &&
                    soopTab is not null && youtubeTab is not null && twitchTab is not null && bilibiliTab is not null)
                {
                    // Exercise the actual MainWindow navigation event chain rather than
                    // assigning PageHost.Content directly. The initial saved section can
                    // already be Drops, so force a real Overview -> Drops transition.
                    window.Dispatcher.Invoke(() =>
                    {
                        dropsNav.IsChecked = false;
                        overviewNav.IsChecked = false;
                        overviewNav.IsChecked = true;
                    }, DispatcherPriority.Input);
                    DrainDispatcher(window.Dispatcher);
                    Assert(pageHost.Content is OverviewPage, checks,
                        "MainWindow 真实导航先切换到 Overview 页面");

                    window.Dispatcher.Invoke(() =>
                    {
                        overviewNav.IsChecked = false;
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
                var soopPanel = FindNamed(dropsPage, "SoopPanel");
                var youtubePanel = FindNamed(dropsPage, "YouTubePanel");
                var twitchPanel = FindNamed(dropsPage, "TwitchPanel");
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

            checks.Insert(0, "UI navigation integration selftest: " + (checks.All(item => item.StartsWith("PASS", StringComparison.Ordinal)) ? "PASS" : "FAIL"));
        }
        catch (Exception ex)
        {
            checks.Add("FAIL 初始化 UI VisualTree 自检：" + ex.GetType().Name + "：" + ex.Message);
        }
        finally
        {
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

        public override void Write(string? message) => Capture(message);
        public override void WriteLine(string? message) => Capture(message);

        private void Capture(string? message)
        {
            if (!string.IsNullOrWhiteSpace(message) &&
                message.Contains("System.Windows.Data Error", StringComparison.OrdinalIgnoreCase))
                HasErrors = true;
        }
    }
}
