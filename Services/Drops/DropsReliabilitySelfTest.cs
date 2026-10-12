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
        Check(!SensitiveDataRedactor.Redact("https://fixture.invalid/?sig=fixture-signature&signature=fixture-signature")
            .Contains("fixture-signature", StringComparison.Ordinal), "播放 URL 签名在 Host 日志中脱敏");
        var applyEvent = typeof(DropsViewModel).GetMethod("OnEventReceived", BindingFlags.Instance | BindingFlags.NonPublic)!;
        var applySnapshot = typeof(DropsViewModel).GetMethod("OnSnapshotChanged", BindingFlags.Instance | BindingFlags.NonPublic)!;
        applySnapshot.Invoke(vm, new object?[] { null, new WorkerSnapshot(DropsPlatform.Soop, WorkerLifecycle.Running,
            "new", "", DateTimeOffset.Now, null, null, 9, 2) });
        applySnapshot.Invoke(vm, new object?[] { null, new WorkerSnapshot(DropsPlatform.Soop, WorkerLifecycle.Crashed,
            "old", "", DateTimeOffset.Now, null, "late failure", 9, 1) });
        Drain();
        Check(vm.Soop.WorkerLifecycle == WorkerLifecycle.Running, "同代 Worker 的迟到 Host 健康快照不能覆盖较新快照");
        var clipboardCopy = Task.Run(() => ClipboardService.CopyTextAsync("CloudLight-Drops-Clipboard-Test"));
        var clipboardDeadline = DateTimeOffset.Now.AddSeconds(15);
        while (!clipboardCopy.IsCompleted && DateTimeOffset.Now < clipboardDeadline) Drain();
        if (clipboardCopy.IsCompletedSuccessfully && !clipboardCopy.Result && ClipboardService.LastFailureCode == unchecked((int)0x800401D0))
            checks.Add($"BLOCKED 真实剪贴板写入/读回：被 {ClipboardService.BusyProcessName ?? "其它程序（已释放所有者窗口）"} 占用，HRESULT=0x{ClipboardService.LastFailureCode:X8}；需解除占用后验收");
        else
        {
            Check(clipboardCopy.IsCompletedSuccessfully && clipboardCopy.Result, $"真实剪贴板后台 STA 写入操作完成（非模拟；状态 {clipboardCopy.Status}，HRESULT=0x{ClipboardService.LastFailureCode:X8}）");
            Check(clipboardCopy.IsCompletedSuccessfully && clipboardCopy.Result && Clipboard.GetText(TextDataFormat.UnicodeText) == "CloudLight-Drops-Clipboard-Test",
                "真实 Win32 剪贴板写入后可读回相同内容（后台调用，非模拟）");
        }
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
        vm.SetTwitchAuthorization("https://www.twitch.tv/activate", "TEST", automatic: false);
        vm.SelectPlatform(DropsPlatform.Twitch);
        if (page.FindName("TwitchCopyCodeButton") is Button copyCode)
        {
            copyCode.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
            var deadline = DateTimeOffset.Now.AddSeconds(15);
            while (!copyCode.IsEnabled && DateTimeOffset.Now < deadline) Drain();
            Check(copyCode.IsEnabled, "真实复制按钮操作结束后恢复可用");
            if (ClipboardService.LastFailureCode == 0)
                Check(Clipboard.GetText(TextDataFormat.UnicodeText) == "TEST", "真实复制按钮 → ClipboardService → 系统剪贴板读回（模拟授权码）");
            else
                Check(vm.Twitch.Notice.Contains("未复制") || vm.Twitch.Notice.Contains("暂不可用"), "系统剪贴板被占用时复制按钮明确告知失败，提供选中文字备用入口");
        }
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
        if (page.FindName("SoopTab") is RadioButton soopTab) { soopTab.IsChecked = false; soopTab.IsChecked = true; }
        Drain();
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
        vm.ApplyState(DropsPlatform.Soop, State(new { running = true, refreshCompleted = true }));
        var refreshSent = false;
        host.ConfigureRequestOverrideForSelfTest((_, command, _, _) =>
        {
            if (command != "refresh") return Task.FromResult(State(new { }));
            refreshSent = true;
            return Task.FromException<JsonElement>(new IOException("模拟只读刷新失败"));
        });
        var refreshSoop = typeof(DropsPage).GetMethod("RefreshSoopAsync", BindingFlags.Instance | BindingFlags.NonPublic)!;
        var loadingGuard = typeof(DropsPage).GetField("_loading", BindingFlags.Instance | BindingFlags.NonPublic)!;
        loadingGuard.SetValue(page, false); // The outer UI fixture holds this guard during navigation.
        vm.CompleteSoopRefresh();
        var failedRefresh = (Task)refreshSoop.Invoke(page, new object[] { false })!;
        Drain();
        loadingGuard.SetValue(page, true);
        Check(refreshSent && failedRefresh.IsCompletedSuccessfully && vm.Soop.Running && vm.SoopCurrentProgress.Single().CurrentMinutes == 12 &&
            vm.Soop.Notice.Contains("保留此前成功数据"), "SOOP 页面只读刷新失败保留运行状态和官方快照，不将全部账号误报停止");
        host.ConfigureRequestOverrideForSelfTest((_, _, _, _) => Task.FromResult(State(new { })));
        vm.ApplyState(DropsPlatform.Soop, State(new { progressHistory = new[] { new {
            uid = "test-soop", taskId = "mission", cycle = "cycle", stageId = "stable-item:60",
            officialMinutes = 0, historicalHighWaterMinutes = 10, source = "cache",
            queriedAt = "2026-10-12T00:00:00Z", rollbackAt = "2026-10-12T00:00:00Z",
            beforeMinutes = 10, afterMinutes = 0,
        } } }));
        Check(vm.SoopProgressHistory.Single().Status.Contains("缓存") && vm.Soop.Notice.Contains("10 → 0"),
            "重启历史独立展示缓存与真实回退证据，不冒充当前进度");
        if (page.FindName("DropsDismissNoticeButton") is Button dismiss)
        {
            var originalNotice = vm.Soop.Notice;
            dismiss.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent)); Drain();
            vm.ReportMessage(DropsPlatform.Soop, originalNotice, "警告");
            Check(vm.Soop.NoticeVisibility == Visibility.Collapsed, "真实关闭提醒按钮生效，同一问题重复回调不重新显示");
            vm.ApplyState(DropsPlatform.Soop, State(new { refreshCompleted = true, refreshDiagnostics = Array.Empty<object>() }));
            vm.ReportMessage(DropsPlatform.Soop, "新的网络故障", "警告");
            Check(vm.Soop.Notice == "新的网络故障", "成功刷新后新的故障仍可提醒");
        }
        vm.ApplyState(DropsPlatform.Twitch, State(new { currentDrop = new {
            name = "reward", campaign = "campaign", currentMinutes = 10, requiredMinutes = 60,
            estimatedExtraMinutes = 2, progress = 1.0 / 6,
        } }));
        Check(vm.TwitchCurrentProgressText.Contains("本地临时估算") && Math.Abs(vm.TwitchCurrentProgress - 100.0 / 6) < .01,
            "Twitch 进度条只使用远端分钟数，额外估算独立说明");
        vm.BeginTwitchStart(false);
        vm.ApplyState(DropsPlatform.Twitch, State(new { running = true, authState = "running",
            accounts = new[] { new { userId = "fixture", loggedIn = true } },
            watchRequests = new { attempts = 3, lastResult = "failed", failure = new { stage = "media_segment", httpStatus = 403 } } }));
        Check(vm.TwitchWatchRequestText.Contains("HTTP 403") && Math.Abs(vm.TwitchCurrentProgress - 100.0 / 6) < .01,
            "Twitch 观看连接失败显示具体阶段/HTTP，保留官方奖励进度");
        vm.ApplyState(DropsPlatform.Twitch, State(new { watchRequests = new { attempts = 4, lastResult = "accepted" } }));
        Check(vm.TwitchWatchRequestText.Contains("以官方奖励进度为准") && !vm.TwitchWatchRequestText.Contains("HTTP 403"),
            "Twitch 观看请求恢复清除旧连接故障，不将分片接受等同官方进度增长");
        applyEvent.Invoke(vm, new object?[] { null, new WorkerEvent(DropsPlatform.Twitch, "watch_request", State(new {
            attempts = 5, lastResult = "failed", failure = new { stage = "media_segment", httpStatus = 403 } })) });
        Drain();
        Check(vm.TwitchWatchRequestText.Contains("HTTP 403") && Math.Abs(vm.TwitchCurrentProgress - 100.0 / 6) < .01,
            "Twitch 分片失败事件直接更新诊断，无需等待奖励变化或手动刷新");
        applyEvent.Invoke(vm, new object?[] { null, new WorkerEvent(DropsPlatform.Twitch, "watch_request", State(new {
            attempts = 6, lastResult = "accepted" })) });
        Drain();
        Check(!vm.TwitchWatchRequestText.Contains("HTTP 403"), "Twitch 分片恢复事件立即清理旧请求错误");
        vm.StopTwitchByUser();
        applyEvent.Invoke(vm, new object?[] { null, new WorkerEvent(DropsPlatform.Twitch, "watch_request", State(new {
            attempts = 7, lastResult = "accepted" })) });
        Drain();
        Check(vm.TwitchWatchRequestText == "观看已停止。", "手动停止后迟到分片事件不能恢复观看诊断");
        var populate = typeof(DropsPage).GetMethod("PopulateSettings", BindingFlags.Instance | BindingFlags.NonPublic)!;
        if (page.FindName("SoopPreferredChannel") is TextBox preferred && page.FindName("SoopSaveSettingsButton") is Button save)
        {
            populate.Invoke(page, new object[] { DropsPlatform.Soop, State(new { settings = new { preferred_bjid = "server-value" } }) });
            preferred.Text = "unsaved-choice";
            populate.Invoke(page, new object[] { DropsPlatform.Soop, State(new { settings = new { preferred_bjid = "background-value" } }) });
            Check(preferred.Text == "unsaved-choice", "SOOP 后台状态不覆盖正在编辑的设置");
            host.ConfigureRequestOverrideForSelfTest((_, command, _, _) => command == "save_settings"
                ? Task.FromException<JsonElement>(new IOException("模拟保存失败")) : Task.FromResult(State(new { })));
            save.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent)); Drain();
            Check(preferred.Text == "unsaved-choice" && save.IsEnabled, "真实保存按钮失败后保留草稿并恢复可用");
            var saved = false;
            host.ConfigureRequestOverrideForSelfTest((_, command, _, _) =>
            {
                if (command == "save_settings") saved = true;
                return Task.FromResult(State(new { settings = new { preferred_bjid = "unsaved-choice" } }));
            });
            save.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent)); Drain();
            populate.Invoke(page, new object[] { DropsPlatform.Soop, State(new { settings = new { preferred_bjid = "applied-value" } }) });
            Check(saved && preferred.Text == "applied-value" && vm.Soop.Notice.Contains("已保存"),
                "真实保存按钮发送 save_settings，成功后接受服务器设置并提示应用结果");
        }
        host.ConfigureRequestOverrideForSelfTest((_, _, _, _) => Task.FromResult(State(new { })));
        vm.SelectPlatform(DropsPlatform.YouTube);
        vm.ApplyState(DropsPlatform.YouTube, State(new { running = true,
            stream = new { videoId = "fixture-live", title = "测试直播" },
            sessions = new[] { new { profile = "fixture", running = true, playing = false, playbackState = "buffering" } } }));
        Check(vm.YouTube.Status == "等待播放确认" && vm.Accounts[0].Status.Contains("播放待确认"),
            "YouTube 浏览器存在且发现直播仍须等待实际播放，不能误报正在观看");
        vm.ApplyState(DropsPlatform.YouTube, State(new { running = true,
            stream = new { videoId = "fixture-live", title = "测试直播" },
            sessions = new[] { new { profile = "fixture", running = true, playing = true, playbackState = "playing" } } }));
        Check(vm.YouTube.Status == "正在观看" && vm.Accounts[0].Status == "已确认播放", "YouTube 已确认播放事件正确投影到账号与页面状态");
        applyEvent.Invoke(vm, new object?[] { null, new WorkerEvent(DropsPlatform.YouTube, "playback", State(new {
            profile = "fixture", playing = false, playbackState = "buffering" })) });
        applyEvent.Invoke(vm, new object?[] { null, new WorkerEvent(DropsPlatform.YouTube, "watch_time", State(new { seconds = 15,
            record = new { date = "2026-10-12", profile = "fixture", videoId = "fixture-live", title = "测试直播", watch_seconds = 15 } })) });
        Drain();
        Check(vm.YouTube.Status == "等待播放确认" && vm.Accounts[0].Status.Contains("播放待确认") && vm.History.Single().Status == "00:00:15",
            "YouTube 后台播放变化和本地历史自动投影，无需手动刷新（模拟事件）");
        vm.ApplyState(DropsPlatform.YouTube, State(new { running = false, stream = (object?)null, sessions = Array.Empty<object>() }));
        applyEvent.Invoke(vm, new object?[] { null, new WorkerEvent(DropsPlatform.YouTube, "watch_time", State(new { seconds = 5 })) });
        Drain();
        Check(vm.YouTube.Status == "未运行", "YouTube 停止后的迟到本地计时事件不能恢复观看提示");
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
                await host.ApplyProxyAsync(new DropsProxySettings(true, "http://127.0.0.1:9", false), timeout.Token);
                await host.StartAsync(platform, timeout.Token);
                await Task.Delay(250, timeout.Token);
                var refresh = host.RequestAsync(platform, "refresh", cancellationToken: timeout.Token);
                await Task.Delay(150, timeout.Token);
                var clock = System.Diagnostics.Stopwatch.StartNew();
                var stopped = await host.StopAsync(platform, timeout.Token);
                try { await refresh; } catch { /* cancelled read-only refresh */ }
                checks.Add((clock.Elapsed < TimeSpan.FromSeconds(6) && !stopped.GetProperty("running").GetBoolean()
                    ? "PASS " : "FAIL ") + "真实 Host 停止绕过刷新队列，Worker 取消只读刷新并退出观看（隔离、不可用测试代理）");
                await host.StartAsync(platform, timeout.Token);
                await Task.Delay(250, timeout.Token);
                using var cancel = new CancellationTokenSource(TimeSpan.FromMilliseconds(50));
                try { await host.RequestAsync(platform, "refresh", cancellationToken: cancel.Token); }
                catch (OperationCanceledException) { }
                catch (IOException) { }
                var afterCancel = await host.RequestAsync(platform, "load_state", cancellationToken: timeout.Token);
                checks.Add((afterCancel.GetProperty("_workerGeneration").GetInt64() > restarted.GetProperty("_workerGeneration").GetInt64()
                    ? "PASS " : "FAIL ") + "已发送请求取消后回收旧 Worker，新请求使用新 generation，无迟到业务遗留");
                host.ConfigureProxy(new DropsProxySettings(false, "", false));
            }
            await host.StopAsync(platform, timeout.Token);
        }
        checks.Add(await RunScopedSoopCancellationSmokeAsync());
        return checks;
    }

    private static async Task<string> RunScopedSoopCancellationSmokeAsync()
    {
        var repo = new DirectoryInfo(AppContext.BaseDirectory);
        while (repo is not null && !File.Exists(Path.Combine(repo.FullName, ".worker-build-venv", "Scripts", "python.exe")))
            repo = repo.Parent;
        if (repo is null) return "BLOCKED SOOP 单账号取消传输回归需要本地 Worker 构建环境";
        var root = Path.Combine(AppPaths.Current.Root, "scoped-soop-fixture");
        Directory.CreateDirectory(root);
        var script = Path.Combine(root, "fixture.py");
        File.WriteAllText(script, """
import sys
from pathlib import Path
from concurrent.futures import Future
sys.path.insert(0, sys.argv[1])
from Shared.protocol import WorkerBase, event
class Worker(WorkerBase):
    platform = 'soop'
    needs_https = False
    def __init__(self):
        super().__init__(Path(sys.argv[2]), Path(sys.argv[2])/'worker.log')
        self.accounts = {'A': True, 'B': True}
        self.commands['stop_account'] = self.stop_account
    def load_state(self, payload):
        return {'running': any(self.accounts.values()), 'accounts': self.accounts}
    def refresh(self, payload):
        event('refresh_waiting', {})
        self.wait_future(Future(), 20)
    def stop_account(self, payload):
        self.accounts[payload['uid']] = False
        return self.load_state({})
raise SystemExit(Worker().run())
""");
        await using var host = new DropsHostService();
        host.ConfigureStartInfoForSelfTest(_ =>
        {
            var info = new System.Diagnostics.ProcessStartInfo
            {
                FileName = Path.Combine(repo.FullName, ".worker-build-venv", "Scripts", "python.exe"),
                UseShellExecute = false, CreateNoWindow = true,
                RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true,
            };
            foreach (var argument in new[] { script, Path.Combine(repo.FullName, "Integrations", "Drops"), root })
                info.ArgumentList.Add(argument);
            return info;
        });
        var waiting = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        host.EventReceived += (_, e) => { if (e.Name == "refresh_waiting") waiting.TrySetResult(); };
        using var budget = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        var before = await host.RequestAsync(DropsPlatform.Soop, "load_state", cancellationToken: budget.Token);
        using var retry = new CancellationTokenSource();
        var refresh = host.RequestAsync(DropsPlatform.Soop, "refresh", new { uid = "B" }, retry.Token);
        await waiting.Task.WaitAsync(budget.Token);
        retry.Cancel();
        var stopped = await host.RequestAsync(DropsPlatform.Soop, "stop_account", new { uid = "B" }, budget.Token);
        try { await refresh; } catch (InvalidOperationException) { } catch (OperationCanceledException) { }
        var after = await host.RequestAsync(DropsPlatform.Soop, "load_state", cancellationToken: budget.Token);
        var accounts = stopped.GetProperty("accounts");
        return (accounts.GetProperty("A").GetBoolean() && !accounts.GetProperty("B").GetBoolean() &&
                before.GetProperty("_workerGeneration").GetInt64() == after.GetProperty("_workerGeneration").GetInt64()
            ? "PASS " : "FAIL ") + "真实 Host/JSONL 取消 B 恢复并停止 B 时保留 A 与原 Worker generation（账号业务模拟）";
    }

    private static void Drain()
    {
        var frame = new DispatcherFrame();
        Dispatcher.CurrentDispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle, new Action(() => frame.Continue = false));
        Dispatcher.PushFrame(frame);
    }
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
