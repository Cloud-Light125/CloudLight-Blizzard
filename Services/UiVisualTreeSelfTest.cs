using System.Reflection;
using System.IO;
using System.Text;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Media3D;
using CloudLightBlizzard.Services.OverwatchRegion;
using CloudLightBlizzard.Views;
using CloudLightBlizzard.Views.Pages;
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

            checks.Insert(0, "UI VisualTree selftest: " + (checks.All(item => item.StartsWith("PASS", StringComparison.Ordinal)) ? "PASS" : "FAIL"));
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
