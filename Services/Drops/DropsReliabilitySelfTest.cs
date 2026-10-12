using System.IO;
using System.Reflection;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using CloudLightBlizzard.ViewModels;
using CloudLightBlizzard.Views.Pages;

namespace CloudLightBlizzard.Services.Drops;

/// <summary>Real WPF controls with simulated Worker responses; never uses account credentials.</summary>
internal static class DropsReliabilitySelfTest
{
    internal static void Run(DropsPage page, DropsViewModel vm, DropsHostService host,
        Window window, string outputPath, ICollection<string> checks)
    {
        void Check(bool ok, string message) => checks.Add((ok ? "PASS " : "FAIL ") + message);
        var applyEvent = typeof(DropsViewModel).GetMethod("OnEventReceived", BindingFlags.Instance | BindingFlags.NonPublic)!;
        JsonElement State(object value) => JsonSerializer.SerializeToElement(value);
        vm.SelectPlatform(DropsPlatform.Soop);
        vm.ApplyState(DropsPlatform.Soop, State(new
        {
            accounts = new[] { new { uid = "test-soop", running = false } },
            tasks = Array.Empty<object>(), inventory = new[] { new { uid = "test-soop", id = "safe", name = "测试奖励" } },
            currentProgress = Array.Empty<object>(), _workerGeneration = 1L, _workerSequence = 10L,
        }));
        vm.ApplyState(DropsPlatform.Twitch, State(new
        {
            accounts = Array.Empty<object>(), campaigns = Array.Empty<object>(), inventory = Array.Empty<object>(),
            authState = "logged_out", running = false,
        }));
        Check(vm.Accounts.Single().Id == "test-soop" && vm.Inventory.Count == 1,
            "后台 Twitch 快照不覆盖前台 SOOP 账号与背包");
        vm.ApplyState(DropsPlatform.Soop, State(new { running = false, _workerGeneration = 1L, _workerSequence = 11L }));
        Check(vm.Inventory.Count == 1, "缺少背包字段的局部状态保留最近成功快照");
        vm.ApplyState(DropsPlatform.Soop, State(new { inventory = Array.Empty<object>(), _workerGeneration = 1L, _workerSequence = 9L }));
        Check(vm.Inventory.Count == 1, "乱序旧响应不能清空新背包快照");
        vm.ApplyState(DropsPlatform.Soop, State(new { running = false, _workerGeneration = 1L, _workerSequence = 12L, _stateTime = 100L }));
        vm.ApplyState(DropsPlatform.Soop, State(new { inventory = Array.Empty<object>(), _workerGeneration = 1L, _workerSequence = 13L, _stateTime = 90L }));
        Check(vm.Inventory.Count == 1, "采集较早但送达较晚的快照不能覆盖更新状态");
        vm.ApplyState(DropsPlatform.Soop, State(new { inventory = Array.Empty<object>(), _workerGeneration = 2L, _workerSequence = 1L }));
        Check(vm.Inventory.Count == 0, "新 Worker 的成功空背包可替换旧快照");
        vm.SetTwitchAuthorization("https://www.twitch.tv.evil/activate", "TEST", false);
        Check(vm.TwitchAuthorizationVisibility == Visibility.Collapsed, "不可信授权网址不会显示验证码操作区");
        vm.SetTwitchAuthorization("https://www.twitch.tv/activate", "TEST", automatic: true);
        Check(vm.TwitchAuthorizationUrl.Length == 0 && vm.TwitchAuthorizationCode.Length == 0 && vm.CanTwitchLogin,
            "自动恢复结束后不复用无人轮询的设备码，手动登录重新取得授权");
        applyEvent.Invoke(vm, new object?[] { null, new WorkerEvent(DropsPlatform.Twitch, "error",
            State(new { category = "oauth", code = "expired_token", message = "授权已过期，请重试授权", retryable = false })) });
        Drain();
        Check(vm.Twitch.Status.Contains("授权已过期") && vm.TwitchAuthorizationVisibility == Visibility.Collapsed,
            "授权过期使用授权提示并隐藏验证码，不误报网络故障");

        // Account status events exercise the exact Host -> VM consumption path.
        vm.SelectPlatform(DropsPlatform.Soop);
        foreach (var account in new[] { (Uid: "test-soop", Running: true), (Uid: "test-second", Running: false) })
            applyEvent.Invoke(vm, new object?[] { null, new WorkerEvent(DropsPlatform.Soop, "account_status", State(new
            {
                uid = account.Uid, running = account.Running, status = account.Running ? "观看中" : "已停止",
                currentProgress = Array.Empty<object>(),
            })) });
        Drain();
        Check(vm.Soop.Running, "停止一个 SOOP 账号不会把其它运行账号标记为全部停止");
        vm.BeginSoopStart("test-soop", automatic: false);
        vm.SetSoopFailure("SOOP 网络连接异常", uid: "test-soop");
        vm.StopSoopByUser("test-second");
        Check(vm.SoopRetryLoopActive, "停止 B 账号不会取消 A 账号的恢复意图");
        vm.StopSoopByUser("test-soop");
        Check(!vm.SoopRetryLoopActive, "停止 A 账号取消其自身重试");

        var requests = new List<string>();
        bool running = false;
        host.ConfigureRequestOverrideForSelfTest((platform, command, _, _) =>
        {
            requests.Add(command);
            if (command == "start") running = true;
            if (command == "stop") running = false;
            return Task.FromResult(State(new { running, accounts = new[] { new { userId = "test-twitch", loggedIn = true } },
                authState = "logged_in", campaigns = Array.Empty<object>(), inventory = Array.Empty<object>(), channels = Array.Empty<object>() }));
        });
        if (page.FindName("TwitchTab") is RadioButton tab) { tab.IsChecked = false; tab.IsChecked = true; }
        Drain();
        vm.ApplyState(DropsPlatform.Twitch, State(new { running = false, authState = "logged_in",
            accounts = new[] { new { userId = "test-twitch", loggedIn = true } } }));
        Drain();
        if (page.FindName("DropsActiveStartButton") is Button start && page.FindName("DropsActiveStopButton") is Button stop)
        {
            start.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent)); Drain();
            Check(requests.Count(command => command == "start") == 1 && vm.Twitch.Running && !start.IsEnabled && stop.IsEnabled,
                "真实开始按钮发送一次 start，并切换运行/停止控件状态（模拟响应）");
            stop.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent)); Drain();
            Check(requests.Count(command => command == "stop") == 1 && !vm.Twitch.Running && start.IsEnabled,
                "真实停止按钮发送 stop，停止后恢复开始按钮（模拟响应）");
        }
        else Check(false, "当前平台主要操作按钮存在");
        host.ConfigureRequestOverrideForSelfTest((_, _, _, _) => Task.FromResult(State(new { })));
        vm.SelectPlatform(DropsPlatform.Soop);
        vm.ApplySoopAccountStatus(State(new
        {
            uid = "test-soop", running = true, status = "示例：心跳业务状态待确认",
            channelId = "example", channelName = "示例直播间", connectionHealthy = true, bridgeConnected = true,
            heartbeatStatus = "unknown", currentProgress = new[] { new
            {
                id = "test-soop:example", account = "test-soop", channel = "示例直播间",
                campaign = "示例：服务端确认进度", reward = "测试奖励", currentMinutes = 12, requiredMinutes = 60,
                percent = 20, progressState = "waiting_server", progressDiagnostic = "心跳业务响应未知，保留服务端确认值。",
                lastConfirmedAt = DateTimeOffset.Now.ToString("O"), watchEligible = true,
                connectionHealthy = true, bridgeConnected = true, heartbeatSuccess = false,
            } },
        }));
        Check(vm.SoopCurrentProgress.Single().CurrentMinutes == 12 && vm.SoopCurrentProgress.Single().SyncState.Contains("待确认"),
            "SOOP 心跳 unknown 仍呈现服务端确认数值，观看状态独立提醒");
        vm.SelectPlatform(DropsPlatform.Twitch);
        vm.ReportMessage(DropsPlatform.Twitch, "测试：接口暂时不可用，保留上次确认进度。", "警告");
        Drain();
        Check(page.FindName("DropsNoticeBanner") is Border { IsVisible: true }, "Worker 故障提示在当前页面可见");

        var directory = Path.Combine(Path.GetDirectoryName(Path.GetFullPath(outputPath))!, "drops-layout");
        Directory.CreateDirectory(directory);
        vm.SoopQuickStart.IsExpanded = vm.YouTubeQuickStart.IsExpanded = vm.TwitchQuickStart.IsExpanded = vm.BilibiliQuickStart.IsExpanded = false;
        if (page.FindName("DropsDiagnosticsExpander") is Expander diagnostics) diagnostics.IsExpanded = false;
        window.MinWidth = 780; window.MinHeight = 480;
        foreach (var platform in Enum.GetValues<DropsPlatform>())
        {
            if (page.FindName(platform switch { DropsPlatform.Soop => "SoopTab", DropsPlatform.YouTube => "YouTubeTab",
                DropsPlatform.Twitch => "TwitchTab", _ => "BilibiliTab" }) is RadioButton platformTab)
            { platformTab.IsChecked = false; platformTab.IsChecked = true; }
            Drain();
            foreach (var physical in new[] { new Size(1366, 768), new Size(1920, 1080), new Size(2560, 1440), new Size(1200, 760) })
            foreach (var scale in new[] { 1.0, 1.25, 1.5 })
            {
                // Divide physical display dimensions into WPF DIPs; OS DPI itself is unchanged.
                window.Width = physical.Width / scale; window.Height = physical.Height / scale;
                window.Measure(new Size(window.Width, window.Height));
                window.Arrange(new Rect(0, 0, window.Width, window.Height)); window.UpdateLayout(); Drain();
                var clipped = Descendants(page).OfType<FrameworkElement>()
                    .Where(control => control.IsVisible && control.ActualWidth > 0 && control is Button or TextBox or ComboBox)
                    .Where(control =>
                    {
                        var bounds = control.TransformToAncestor(page).TransformBounds(new Rect(0, 0, control.ActualWidth, control.ActualHeight));
                        return bounds.Left < -1 || bounds.Right > page.ActualWidth + 1;
                    }).Select(control => string.IsNullOrEmpty(control.Name) ? control.GetType().Name : control.Name).ToArray();
                Check(clipped.Length == 0, $"{platform} {physical.Width:0}×{physical.Height:0} / {scale:P0} 操作控件无横向裁切 {string.Join(",", clipped)}");
                if (page.FindName("DropsScrollViewer") is ScrollViewer scroll)
                {
                    scroll.ScrollToTop(); window.UpdateLayout();
                    Check(scroll.ViewportWidth > 0 && scroll.ExtentWidth <= scroll.ViewportWidth + 1,
                        $"{platform} {physical.Width:0}×{physical.Height:0} / {scale:P0} DIP模拟无横向溢出");
                    scroll.ScrollToBottom(); window.UpdateLayout();
                    Check(scroll.VerticalOffset >= scroll.ScrollableHeight - 1, $"{platform} 底部内容可滚动到达");
                    scroll.ScrollToTop(); window.UpdateLayout();
                }
                if (physical.Width == 1366 && scale is 1.0 or 1.5)
                {
                    var bitmap = new RenderTargetBitmap((int)Math.Ceiling(page.ActualWidth * scale),
                        (int)Math.Ceiling(page.ActualHeight * scale), 96 * scale, 96 * scale, PixelFormats.Pbgra32);
                    bitmap.Render(page);
                    var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
                    using var stream = File.Create(Path.Combine(directory, $"{platform}-1366-{scale * 100:0}.png")); encoder.Save(stream);
                }
            }
        }
        foreach (var result in Task.Run(RunHostProtocolSmokeAsync).GetAwaiter().GetResult()) checks.Add(result);
    }

    private static async Task<List<string>> RunHostProtocolSmokeAsync()
    {
        var checks = new List<string>();
        await using var host = new DropsHostService();
        foreach (var platform in Enum.GetValues<DropsPlatform>())
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            var state = await host.RequestAsync(platform, "load_state", cancellationToken: timeout.Token);
            var valid = state.ValueKind == JsonValueKind.Object &&
                state.TryGetProperty("_workerSequence", out var sequence) && sequence.GetInt64() > 0 &&
                state.TryGetProperty("running", out var running) && !running.GetBoolean();
            checks.Add((valid ? "PASS " : "FAIL ") + $"真实 {platform} Worker → JSONL → DropsHostService 握手与未登录状态（隔离数据）");
            if (platform == DropsPlatform.Twitch && host.Snapshots.First(item => item.Platform == platform).ProcessId is { } pid)
            {
                using var process = System.Diagnostics.Process.GetProcessById(pid);
                process.Kill(entireProcessTree: true);
                await process.WaitForExitAsync(timeout.Token);
                var restarted = await host.RequestAsync(platform, "load_state", cancellationToken: timeout.Token);
                checks.Add((restarted.GetProperty("_workerGeneration").GetInt64() > state.GetProperty("_workerGeneration").GetInt64()
                    ? "PASS " : "FAIL ") + "真实 Worker 崩溃后重建进程并使用新 generation（无账号）");
            }
            await host.StopAsync(platform, timeout.Token);
        }
        return checks;
    }

    private static void Drain() => Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
    private static IEnumerable<DependencyObject> Descendants(DependencyObject parent)
    {
        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(parent); index++)
        {
            var child = VisualTreeHelper.GetChild(parent, index);
            yield return child;
            foreach (var descendant in Descendants(child)) yield return descendant;
        }
    }
}
