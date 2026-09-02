using System.IO;
using System.Net;
using System.IO.Compression;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Windows;
using CloudLightBlizzard.Models;
using CloudLightBlizzard.Services.Drops;
using CloudLightBlizzard.Services.Diagnostics;
using CloudLightBlizzard.Services.Notifications;
using CloudLightBlizzard.Services.OverwatchRegion;
using CloudLightBlizzard.ViewModels;
using GameRegion = CloudLightBlizzard.Services.OverwatchRegion.OverwatchRegion;

namespace CloudLightBlizzard.Services;

public static class FeatureSelfTest
{
    public static void Run(string outputRoot, string? test = null)
    {
        outputRoot = Path.GetFullPath(outputRoot);
        Directory.CreateDirectory(outputRoot);
        var workspace = Path.Combine(outputRoot, "workspace");
        if (Directory.Exists(workspace)) Directory.Delete(workspace, true);
        Directory.CreateDirectory(workspace);
        RegionSwitchLog.FileOverride = Path.Combine(outputRoot, "region-switch.log");
        var report = new StringBuilder();
        try
        {
            if (string.Equals(test, "drops-log", StringComparison.OrdinalIgnoreCase))
            {
                RunPlatformLogTailSessionTest(workspace, report).GetAwaiter().GetResult();
                report.AppendLine("OVERALL: PASS");
                return;
            }
            if (string.Equals(test, "update", StringComparison.OrdinalIgnoreCase))
            {
                RunUpdateCheckTest(workspace, report).GetAwaiter().GetResult();
                report.AppendLine("OVERALL: PASS");
                return;
            }
            if (string.Equals(test, "region-verified", StringComparison.OrdinalIgnoreCase))
            {
                RunVerifiedDifferenceRegionTest(workspace, report).GetAwaiter().GetResult();
                RunRegionMaintenanceTests(workspace, report).GetAwaiter().GetResult();
                report.AppendLine("OVERALL: PASS");
                return;
            }
            if (string.Equals(test, "region-guide", StringComparison.OrdinalIgnoreCase))
            {
                RunRegionPreparationGuideTest(report);
                report.AppendLine("OVERALL: PASS");
                return;
            }
            if (string.Equals(test, "twitch-connection", StringComparison.OrdinalIgnoreCase))
            {
                RunTwitchConnectionStateTest(report);
                report.AppendLine("OVERALL: PASS");
                return;
            }
            if (string.Equals(test, "drops-recovery", StringComparison.OrdinalIgnoreCase))
            {
                RunDropsRecoveryStateTest(report);
                report.AppendLine("OVERALL: PASS");
                return;
            }
            RunAccountSnapshotTest(workspace, report);
            RunLoginVerificationTest(report);
            RunTwitchConnectionStateTest(report);
            RunDropsRecoveryStateTest(report);
            RunPlatformLogTailSessionTest(workspace, report).GetAwaiter().GetResult();
            RunUpdateCheckTest(workspace, report).GetAwaiter().GetResult();
            RunRegionPreparationGuideTest(report);
            RunRegionGenerationTest(workspace, report).GetAwaiter().GetResult();
            RunBestEffortRegionTest(workspace, report).GetAwaiter().GetResult();
            RunRegionMaintenanceTests(workspace, report).GetAwaiter().GetResult();
            RunAccountSwitchOrderTest(report).GetAwaiter().GetResult();
            RunAccountPreferenceTest(workspace, report);
            RunAppPathsMigrationTest(workspace, report);
            RunProductizationTests(workspace, report).GetAwaiter().GetResult();
            report.AppendLine("OVERALL: PASS");
        }
        catch (Exception ex)
        {
            report.AppendLine("OVERALL: FAIL");
            report.AppendLine(ex.ToString());
        }
        finally
        {
            File.WriteAllText(Path.Combine(outputRoot, "feature-selftest.txt"), report.ToString());
            RegionSwitchLog.FileOverride = null;
            try { Directory.Delete(workspace, true); } catch { }
        }
    }

    private static void RunAccountSnapshotTest(string workspace, StringBuilder report)
    {
        var roaming = Path.Combine(workspace, "roaming");
        var local = Path.Combine(workspace, "local");
        var profiles = Path.Combine(workspace, "accounts");
        Directory.CreateDirectory(roaming);
        Directory.CreateDirectory(local);
        var store = new AppDataStore(new BattleNetPaths(local, roaming), profiles);

        File.WriteAllText(Path.Combine(roaming, "common.config"), "A common");
        File.WriteAllText(Path.Combine(roaming, "a-only.config"), "A only");
        Directory.CreateDirectory(Path.Combine(roaming, "nested"));
        File.WriteAllText(Path.Combine(roaming, "nested", "account.config"), "A nested");
        Directory.CreateDirectory(Path.Combine(roaming, "Overlay", "logs"));
        File.WriteAllText(Path.Combine(roaming, "Overlay", "logs", "runtime.log"), "excluded");
        store.Save(1, "A#1");

        File.WriteAllText(Path.Combine(roaming, "common.config"), "B common");
        File.Delete(Path.Combine(roaming, "a-only.config"));
        File.WriteAllText(Path.Combine(roaming, "b-only.config"), "B only");
        File.WriteAllText(Path.Combine(roaming, "nested", "account.config"), "B nested");
        store.Save(2, "B#2");

        var accountABefore = CaptureBackupFiles(Path.Combine(profiles, "1"));
        var accountBBefore = CaptureBackupFiles(Path.Combine(profiles, "2"));

        store.Restore(1);
        Assert(File.Exists(Path.Combine(roaming, "a-only.config")), "A-only restored");
        Assert(!File.Exists(Path.Combine(roaming, "b-only.config")), "B-only removed when restoring A");
        Assert(File.ReadAllText(Path.Combine(roaming, "nested", "account.config")) == "A nested", "nested A restored");
        store.Restore(2);
        Assert(File.Exists(Path.Combine(roaming, "b-only.config")), "B-only restored");
        Assert(!File.Exists(Path.Combine(roaming, "a-only.config")), "A-only removed when restoring B");
        Assert(File.ReadAllText(Path.Combine(roaming, "nested", "account.config")) == "B nested", "nested B restored");
        Assert(!File.Exists(Path.Combine(profiles, "1", "BattleNet", "Overlay", "logs", "runtime.log")), "logs excluded");
        Assert(File.Exists(Path.Combine(roaming, "Overlay", "logs", "runtime.log")), "excluded live file untouched");
        Assert(store.ReadManifest(2)?.Entries.Any(entry => entry.RelativePath == "nested/account.config") == true,
            "manifest records relative nested path");
        store.Restore(1);
        store.Restore(2);
        AssertBackupFilesUnchanged(Path.Combine(profiles, "1"), accountABefore, "A backup after A-B-A-B switches");
        AssertBackupFilesUnchanged(Path.Combine(profiles, "2"), accountBBefore, "B backup after A-B-A-B switches");
        report.AppendLine("TEST 1 account controlled mirror: PASS (recursive/manifest/A-B cleanup/exclusions; A-B-A-B leaves backup contents/timestamps unchanged)");
    }

    private static void RunLoginVerificationTest(StringBuilder report)
    {
        var now = DateTime.UtcNow;
        Assert(AccountSwitchVerification.Evaluate(false, null, 2, now, now.AddMinutes(2), BattleNetLoginEvidence.None)
               == AccountSwitchVerificationState.WaitingForBattleNet, "slow start is waiting");
        Assert(AccountSwitchVerification.Evaluate(true, 1, 2, now.AddSeconds(90), now.AddMinutes(2), BattleNetLoginEvidence.None)
               == AccountSwitchVerificationState.WaitingForLogin, "90 second active-id delay is waiting");
        Assert(AccountSwitchVerification.Evaluate(true, 1, 2, now.AddMinutes(3), now, BattleNetLoginEvidence.None)
               == AccountSwitchVerificationState.Unconfirmed, "timeout remains unconfirmed");
        Assert(AccountSwitchVerification.Evaluate(true, 1, 2, now, now.AddMinutes(1), BattleNetLoginEvidence.RealAuthExpired)
               == AccountSwitchVerificationState.LoginRequired, "explicit auth evidence requires login");
        Assert(AccountSwitchVerification.Evaluate(true, 2, 2, now, now, BattleNetLoginEvidence.RealAuthExpired)
               == AccountSwitchVerificationState.LoggedIn, "confirmed target wins");
        report.AppendLine("TEST 2 login state machine: PASS (90s delay does not expire; only explicit evidence requires login)");
    }

    private static void RunTwitchConnectionStateTest(StringBuilder report)
    {
        var host = new DropsHostService();
        using (var vm = new DropsViewModel(host, TimeSpan.FromMilliseconds(20),
                   TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(2),
                   (_, _) => Task.CompletedTask))
        {
            vm.BeginTwitchLogin();
            Assert(vm.TwitchConnectionStage == TwitchConnectionStage.Connecting,
                "Twitch login immediately enters connecting stage");
            Assert(!vm.CanTwitchLogin && vm.TwitchLoginButtonText == "正在登录…",
                "Twitch login button immediately shows progress and blocks duplicates");

            vm.SetTwitchTemporaryNetworkFailure("simulated network failure");
            vm.SetTwitchTemporaryNetworkFailure("same simulated network failure");
            Assert(SpinWait.SpinUntil(() => vm.TwitchRetryLoopActive, 500),
                "Twitch network failure schedules retry");
            Assert(vm.TwitchRetryLoopStarts == 1,
                "Twitch retry supervisor remains single-instance");
        }

        var retryCalls = 0;
        using (var vm = new DropsViewModel(host, TimeSpan.FromMilliseconds(80),
                   TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(2),
                   (_, _) => { Interlocked.Increment(ref retryCalls); return Task.CompletedTask; }))
        {
            vm.BeginTwitchLogin();
            vm.SetTwitchTemporaryNetworkFailure("simulated network failure");
            vm.SetTwitchAuthorization("https://www.twitch.tv/activate", "ABCD-EFGH", automatic: false);
            Thread.Sleep(120);
            Assert(retryCalls == 0, "Twitch auth-required cancels network retry");
            Assert(vm.TwitchConnectionStage == TwitchConnectionStage.WaitingAuthorization,
                "Twitch auth-required remains waiting for the current device code");
            Assert(vm.CanTwitchLogin && vm.TwitchLoginButtonText == "打开登录页面",
                "Twitch auth-required lets the login button reopen the real device authorization URL");
        }

        using (var vm = new DropsViewModel(host, TimeSpan.FromMilliseconds(80),
                   TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(2),
                   (_, _) => Task.CompletedTask))
        {
            vm.BeginTwitchLogin();
            vm.SetTwitchTemporaryNetworkFailure("connection timeout");
            Assert(vm.TwitchLastConnectionFailureKind == TwitchConnectionFailureKind.Timeout,
                "Twitch timeout remains a network timeout");
            Assert(SpinWait.SpinUntil(() => vm.TwitchRetryLoopActive, 500),
                "Twitch timeout remains retryable");

            using var runtimeState = JsonDocument.Parse("""
                {"running":false,"accounts":[],"runtime":{"available":false,"component":"ssl","code":"ssl_runtime_unavailable","message":"DLL load failed while importing _ssl"}}
                """);
            vm.ApplyState(DropsPlatform.Twitch, runtimeState.RootElement);
            Assert(vm.TwitchConnectionStage == TwitchConnectionStage.SslRuntimeError,
                "Twitch SSL runtime failure has a dedicated state");
            Assert(vm.Twitch.Summary.Contains("Python SSL 运行库无法加载", StringComparison.Ordinal),
                "Twitch SSL runtime failure is not shown as a normal network error");
            Assert(SpinWait.SpinUntil(() => !vm.TwitchRetryLoopActive, 500),
                "Twitch SSL runtime failure cancels automatic retry");

            vm.BeginClearTwitchLogin();
            Assert(!vm.CanClearTwitchLogin, "Twitch clear-login blocks duplicate clicks only while clearing");
            using var clearedState = JsonDocument.Parse("""
                {"running":false,"accounts":[],"authState":"logged_out","runtime":{"available":true}}
                """);
            vm.CompleteClearTwitchLogin(clearedState.RootElement);
            Assert(vm.TwitchConnectionStage == TwitchConnectionStage.Unconnected && vm.CanTwitchLogin,
                "Twitch clear-login resets checking state and enables login again");
            Assert(vm.CanClearTwitchLogin, "Twitch clear-login remains available after reset");
        }

        var immediateRetryCalls = 0;
        using (var vm = new DropsViewModel(host, TimeSpan.FromSeconds(5),
                   TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(2),
                   (_, _) =>
                   {
                       Interlocked.Increment(ref immediateRetryCalls);
                       return Task.CompletedTask;
                   }))
        {
            vm.BeginTwitchLogin();
            vm.SetTwitchTemporaryNetworkFailure("connection timeout");
            Assert(SpinWait.SpinUntil(() => vm.TwitchRetryNowVisibility == System.Windows.Visibility.Visible, 500),
                "Twitch retry wait exposes one immediate-retry action");
            vm.RefreshTemporalStatus(DateTimeOffset.Now);
            Assert(vm.TwitchRetryStatusText.Contains("秒后自动重试", StringComparison.Ordinal),
                "Twitch retry wait exposes a display-only countdown");
            Assert(vm.RetryTwitchNow(), "Twitch immediate retry wakes the pending delay");
            Assert(SpinWait.SpinUntil(() => immediateRetryCalls == 1, 500),
                "Twitch immediate retry enters the existing retry flow once");
            using var recoveredState = JsonDocument.Parse("""
                {"running":true,"accounts":[{"userId":"test","loggedIn":true}],
                 "authState":"logged_in","connectionState":"running","runtime":{"available":true}}
                """);
            vm.ApplyState(DropsPlatform.Twitch, recoveredState.RootElement);
            Assert(SpinWait.SpinUntil(() => !vm.TwitchRetryLoopActive, 500),
                "Twitch success cancels the retry flow and countdown");
            Assert(vm.TwitchRetryLoopStarts == 1 && immediateRetryCalls == 1,
                "Twitch immediate retry does not create a parallel reconnect loop");
            Assert(vm.TwitchRetryStatusVisibility == System.Windows.Visibility.Collapsed,
                "Twitch success hides the retry countdown");
        }
        report.AppendLine("Twitch connection state: PASS");
    }

    private static void RunDropsRecoveryStateTest(StringBuilder report)
    {
        var host = new DropsHostService();
        var retryCalls = 0;
        using (var vm = new DropsViewModel(host, TimeSpan.FromMinutes(1),
                   TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(2), null,
                   TimeSpan.FromMilliseconds(20), (attempt, _) =>
                   {
                       Interlocked.Increment(ref retryCalls);
                       return attempt < 3
                           ? Task.FromException(new TimeoutException("simulated SOOP network timeout"))
                           : Task.CompletedTask;
                   }))
        {
            vm.SetSoopAutoStartEnabled(true);
            vm.BeginSoopStart("account", automatic: true);
            vm.SetSoopFailure("connection timeout");
            vm.SetSoopFailure("same connection timeout");
            Assert(SpinWait.SpinUntil(() => retryCalls >= 3 && !vm.SoopRetryLoopActive, 1000),
                "SOOP retry continues after multiple network failures and recovers");
            Assert(vm.SoopRetryLoopStarts == 1,
                "SOOP retry supervisor remains single-instance");
            Assert(vm.Soop.Status == "SOOP 网络连接已恢复",
                "SOOP recovery restores running status and cancels retry");
            Assert(vm.SoopQuickStart.Steps[2].Satisfied,
                "SOOP recovery completes the pending structured channel refresh");
        }

        using (var vm = new DropsViewModel(host))
        {
            using var withChannels = JsonDocument.Parse("""
                {"running":true,"settings":{},"refreshStatus":"success","refreshCompleted":true,
                 "accounts":[{"uid":"account","running":true,"channels":[{"id":"one"},{"id":"two"}]}],
                 "tasks":[],"inventory":[],"currentProgress":[],"runtime":{"available":true}}
                """);
            vm.ApplyState(DropsPlatform.Soop, withChannels.RootElement);
            Assert(vm.SoopQuickStart.Steps[2].Satisfied && vm.SoopRefreshStatus.Contains("2 个频道"),
                "SOOP structured refresh with channels completes quick-start step 3");

            using var withoutChannels = JsonDocument.Parse("""
                {"running":true,"settings":{},"refreshStatus":"success","refreshCompleted":true,
                 "accounts":[{"uid":"account","running":true,"channels":[]}],
                 "tasks":[],"inventory":[],"currentProgress":[],"runtime":{"available":true}}
                """);
            vm.ApplyState(DropsPlatform.Soop, withoutChannels.RootElement);
            Assert(vm.SoopQuickStart.Steps[2].Satisfied &&
                   vm.SoopRefreshStatus.Contains("当前没有符合条件的频道"),
                "SOOP successful empty refresh still completes quick-start step 3");
        }

        var immediateRetryCalls = 0;
        using (var vm = new DropsViewModel(host, TimeSpan.FromMinutes(1),
                   TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(2), null,
                   TimeSpan.FromSeconds(5), (_, _) =>
                   {
                       Interlocked.Increment(ref immediateRetryCalls);
                       return Task.CompletedTask;
                   }))
        {
            vm.SetSoopAutoStartEnabled(true);
            vm.BeginSoopStart("account", automatic: true);
            vm.SetSoopFailure("connection timeout");
            Assert(SpinWait.SpinUntil(() => vm.SoopRetryNowVisibility == System.Windows.Visibility.Visible, 500),
                "SOOP retry wait exposes one immediate-retry action");
            vm.RefreshTemporalStatus(DateTimeOffset.Now);
            Assert(vm.SoopRetryStatusText.Contains("秒后自动重试", StringComparison.Ordinal),
                "SOOP retry wait exposes a display-only countdown");
            Assert(vm.RetrySoopNow(), "SOOP immediate retry wakes the pending delay");
            Assert(SpinWait.SpinUntil(() => immediateRetryCalls == 1 && !vm.SoopRetryLoopActive, 500),
                "SOOP immediate retry recovers through the existing retry flow");
            Assert(vm.SoopRetryLoopStarts == 1 && immediateRetryCalls == 1,
                "SOOP immediate retry does not create a parallel reconnect loop");
            Assert(vm.SoopRetryStatusVisibility == System.Windows.Visibility.Collapsed,
                "SOOP success hides the retry countdown");
        }
        using (var vm = new DropsViewModel(host))
        {
            for (var i = 0; i < 100; i++)
                vm.AddRecoveryEventForSelfTest(DropsPlatform.Twitch, "压力事件", $"event-{i}",
                    DropsConnectionState.WaitingRetry);
            Assert(vm.RecoveryEvents.Count == DropsViewModel.RecoveryEventLimit &&
                   vm.RecoveryEvents[0].Detail == "event-99" &&
                   vm.CreateDiagnosticSnapshot().RecentEvents.Count == DropsViewModel.RecoveryEventLimit,
                "Drops recovery event history stays bounded at the configured limit under repeated events");
        }
        report.AppendLine("SOOP network recovery / quick-start refresh state: PASS");
    }

    private static async Task RunPlatformLogTailSessionTest(string workspace, StringBuilder report)
    {
        static string LogPath(string directory) => Path.Combine(directory, "drops-twitch.log");

        var existingDirectory = Path.Combine(workspace, "log-tail-existing");
        Directory.CreateDirectory(existingDirectory);
        var existingPath = LogPath(existingDirectory);
        await File.WriteAllTextAsync(existingPath, "OLD-1\nOLD-2\n");
        var existingSession = new PlatformLogSession(existingDirectory);
        await File.AppendAllTextAsync(existingPath, "NEW-1\nNEW-2\n");
        await using (var tail = new PlatformLogTailService(existingSession))
        {
            await tail.RefreshAsync(DropsPlatform.Twitch);
            Assert(tail.GetCurrentText(DropsPlatform.Twitch) == "NEW-1\nNEW-2\n",
                "log UI skips bytes written before process session capture");
        }

        var missingDirectory = Path.Combine(workspace, "log-tail-missing");
        Directory.CreateDirectory(missingDirectory);
        var missingSession = new PlatformLogSession(missingDirectory);
        await File.WriteAllTextAsync(LogPath(missingDirectory), "NEW-1\n");
        await using (var tail = new PlatformLogTailService(missingSession))
        {
            await tail.RefreshAsync(DropsPlatform.Twitch);
            Assert(tail.GetCurrentText(DropsPlatform.Twitch) == "NEW-1\n",
                "log created after process startup is read from byte zero");
        }

        var truncatedDirectory = Path.Combine(workspace, "log-tail-truncated");
        Directory.CreateDirectory(truncatedDirectory);
        var truncatedPath = LogPath(truncatedDirectory);
        await File.WriteAllTextAsync(truncatedPath, "OLD-CONTENT-BEFORE-START\n");
        var truncatedSession = new PlatformLogSession(truncatedDirectory);
        await File.AppendAllTextAsync(truncatedPath, "SESSION-BEFORE-TRUNCATE\n");
        await using (var tail = new PlatformLogTailService(truncatedSession))
        {
            await tail.RefreshAsync(DropsPlatform.Twitch);
            await File.WriteAllTextAsync(truncatedPath, "NEW-FILE\n");
            await tail.RefreshAsync(DropsPlatform.Twitch);
            Assert(tail.GetCurrentText(DropsPlatform.Twitch) ==
                   "SESSION-BEFORE-TRUNCATE\nNEW-FILE\n",
                "truncated log resets its cursor without clearing current-session UI content");

            await tail.ClearDisplayAsync(DropsPlatform.Twitch);
            await tail.RefreshAsync(DropsPlatform.Twitch);
            Assert(tail.GetCurrentText(DropsPlatform.Twitch).Length == 0,
                "clear display advances the cursor and refresh does not restore visible history");
            await File.AppendAllTextAsync(truncatedPath, "AFTER-CLEAR\n");
            await tail.RefreshAsync(DropsPlatform.Twitch);
            Assert(tail.GetCurrentText(DropsPlatform.Twitch) == "AFTER-CLEAR\n",
                "new log content remains visible after clear display");
        }

        report.AppendLine("Drops log session cursor: PASS (existing/missing/truncate/clear)");
    }

    private static async Task RunUpdateCheckTest(string workspace, StringBuilder report)
    {
        Assert(UpdateService.IsNewerVersion("1.0.0", "v1.0.1"), "1.0.0 < v1.0.1");
        Assert(UpdateService.IsNewerVersion("1.0.9", "v1.1.0"), "1.0.9 < v1.1.0");
        Assert(UpdateService.IsNewerVersion("1.9.9", "v2.0.0"), "1.9.9 < v2.0.0");
        Assert(!UpdateService.IsNewerVersion("1.1.0", "v1.0.9"), "1.1.0 > v1.0.9");
        Assert(!UpdateService.IsNewerVersion("1.0.0", "v1.0.0"), "1.0.0 == v1.0.0");
        Assert(UpdateService.NormalizeVersion("v1.0.1") == "1.0.1", "leading v is normalized");
        Assert(UpdateService.NormalizeVersion("v1.0.1-rc.1") is null, "prerelease tag is not a stable version");
        Assert(UpdateService.NormalizeReleaseVersion("v1.0.1-rc.1") == "1.0.1-rc.1",
            "beta release version keeps its prerelease identifier");

        HttpRequestMessage? capturedRequest = null;
        var updateCalls = 0;
        var releaseJson = """
            {
              "version": "1.0.1",
              "tag": "v1.0.1",
              "name": "CloudLight Blizzard 1.0.1",
              "notes": "修复与改进",
              "htmlUrl": "https://github.com/Cloud-Light125/CloudLight-Blizzard/releases/tag/v1.0.1",
              "publishedAt": "2026-08-14T08:00:00Z",
                  "assets": [
                {
                  "name": "CloudLight-Blizzard-1.0.1-win-x64-Setup.exe",
                  "downloadUrl": "https://github.com/Cloud-Light125/CloudLight-Blizzard/releases/download/v1.0.1/CloudLight-Blizzard-1.0.1-win-x64-Setup.exe",
                  "size": 123456,
                  "digest": "sha256:0000000000000000000000000000000000000000000000000000000000000000"
                }
              ]
            }
            """;
        using (var client = new HttpClient(new StubHttpHandler(request =>
               {
                   updateCalls++;
                   capturedRequest = request;
                   return JsonResponse(releaseJson);
               })))
        using (var service = new UpdateService(client, "1.0.0"))
        {
            var result = await service.CheckAsync();
            Assert(result.Status == UpdateCheckResultStatus.Success && result.HasUpdate &&
                   result.LatestVersion == "1.0.1", "Worker latest release is parsed and compared");
            Assert(result.ReleaseUrl.EndsWith("/releases/tag/v1.0.1", StringComparison.Ordinal),
                "release html_url is retained");
            Assert(result.ReleaseNotes == "修复与改进" && result.PublishedAt.HasValue,
                "release notes and publish time are retained");
            Assert(result.InstallerDownloadUrl?.EndsWith("Setup.exe", StringComparison.Ordinal) == true,
                "conventional installer asset is parsed without downloading");
            Assert(UpdateService.IsValidSha256Digest(result.InstallerDigest),
                "Worker installer digest is parsed in strict sha256 format");
            Assert(capturedRequest?.RequestUri?.AbsoluteUri == UpdateService.LatestReleaseApiUrl,
                "only the fixed Worker update endpoint is requested");
            Assert(capturedRequest?.Headers.UserAgent.ToString().Contains("CloudLight-Blizzard", StringComparison.Ordinal) == true &&
                   capturedRequest.Headers.Accept.Any(value => value.MediaType == "application/json"),
                "Worker request headers are present");
            var cached = await service.CheckAsync();
            Assert(cached.LatestVersion == "1.0.1" && updateCalls == 1,
                "successful update result is reused without another HTTP request");
        }

        HttpRequestMessage? betaRequest = null;
        var betaJson = """
            {
              "version": "1.0.2-beta.1",
              "tag": "v1.0.2-beta.1",
              "name": "CloudLight Blizzard 1.0.2 Beta 1",
              "prerelease": true,
              "htmlUrl": "https://github.com/Cloud-Light125/CloudLight-Blizzard/releases/tag/v1.0.2-beta.1",
              "assets": [
                {
                  "name": "CloudLight-Blizzard-1.0.2-beta.1-win-x64-Setup.exe",
                  "downloadUrl": "https://github.com/Cloud-Light125/CloudLight-Blizzard/releases/download/v1.0.2-beta.1/CloudLight-Blizzard-1.0.2-beta.1-win-x64-Setup.exe",
                  "size": 456,
                  "digest": "sha256:1111111111111111111111111111111111111111111111111111111111111111"
                }
              ]
            }
            """;
        using (var client = new HttpClient(new StubHttpHandler(request =>
               {
                   betaRequest = request;
                   return JsonResponse(betaJson);
               })))
        using (var service = new UpdateService(client, "1.0.0"))
        {
            var beta = await service.CheckAsync(UpdateChannel.Beta);
            Assert(beta.Status == UpdateCheckResultStatus.Success && beta.HasUpdate &&
                   beta.LatestVersion == "1.0.2-beta.1" &&
                   beta.InstallerName == "CloudLight-Blizzard-1.0.2-beta.1-win-x64-Setup.exe" &&
                   UpdateService.IsValidSha256Digest(beta.InstallerDigest),
                "beta channel accepts prerelease releases and selects the matching installer");
            Assert(betaRequest?.RequestUri?.Query == "?channel=beta",
                "beta update checks pass channel=beta to the Worker");
        }

        var invalidJson = releaseJson.Replace("\"version\": \"1.0.1\"", "\"version\": \"preview\"")
            .Replace("\"tag\": \"v1.0.1\"", "\"tag\": \"preview\"");
        using (var client = new HttpClient(new StubHttpHandler(_ => JsonResponse(invalidJson))))
        using (var service = new UpdateService(client, "1.0.0"))
            Assert((await service.CheckAsync()).FailureKind == UpdateFailureKind.InvalidResponse,
                "invalid Worker response is classified");

        var olderReleaseJson = releaseJson.Replace("1.0.1", "2.0.6", StringComparison.Ordinal);
        using (var client = new HttpClient(new StubHttpHandler(_ => JsonResponse(olderReleaseJson))))
        using (var service = new UpdateService(client, "2.0.7"))
        {
            var olderRemote = await service.CheckAsync();
            Assert(olderRemote.Status == UpdateCheckResultStatus.Success && !olderRemote.HasUpdate &&
                   olderRemote.LatestVersion == "2.0.6" && olderRemote.FailureKind == UpdateFailureKind.None,
                "local 2.0.7 newer than remote 2.0.6 is a successful up-to-date result");
        }

        var installerBytes = new byte[32 * 1024];
        installerBytes[0] = (byte)'M';
        installerBytes[1] = (byte)'Z';
        var installerDigest = "sha256:" + Convert.ToHexString(SHA256.HashData(installerBytes));
        var routedUris = new List<Uri?>();
        var downloadSettings = new AppSettings
        {
            EnableProxy = true,
            ProxyUrl = "http://127.0.0.1:7897",
            FallbackDirect = false,
        };
        using (var routed = new CloudHttpClientFactory(
                   downloadSettings,
                   Path.Combine(workspace, "update-download-network.log"),
                   proxyUri =>
                   {
                       routedUris.Add(proxyUri);
                       return new HttpClient(new StubHttpHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
                       {
                           Content = new ByteArrayContent(installerBytes),
                       }));
                   }))
        {
            var downloader = new UpdateDownloadService(routed);
            UpdateDownloadProgress? lastProgress = null;
            var progress = new InlineProgress<UpdateDownloadProgress>(value => lastProgress = value);
            var downloadResult = new UpdateCheckResult
            {
                Status = UpdateCheckResultStatus.Success,
                CurrentVersion = "2.0.7",
                LatestVersion = "2.0.8",
                HasUpdate = true,
                ReleaseUrl = "https://github.com/Cloud-Light125/CloudLight-Blizzard/releases/tag/v2.0.8",
                 InstallerDownloadUrl =
                    "https://github.com/Cloud-Light125/CloudLight-Blizzard/releases/download/v2.0.8/CloudLight-Blizzard-2.0.8-win-x64-Setup.exe",
                 InstallerSize = installerBytes.Length,
                 InstallerDigest = installerDigest,
                 InstallerName = "CloudLight-Blizzard-2.0.8-win-x64-Setup.exe",
                 Tag = "v2.0.8",
            };
            var downloaded = await downloader.DownloadInstallerAsync(downloadResult, progress);
            Assert(File.Exists(downloaded) && File.ReadAllBytes(downloaded).AsSpan().SequenceEqual(installerBytes),
                "online update streams the installer to disk");
            Assert(routedUris.Any(uri => uri is not null && uri.Host == "127.0.0.1" && uri.Port == 7897),
                "online update uses the configured application proxy");
            Assert(lastProgress?.Percentage == 100 && lastProgress.BytesReceived == installerBytes.Length,
                "online update reports complete download progress");
            Assert(lastProgress?.Phase == UpdateDownloadPhase.Verifying,
                "online update reports verification after all download streams are closed");
            Assert(!File.Exists(downloaded + ".partial"), "online update leaves no partial file after success");

            using (var exclusive = new FileStream(downloaded, FileMode.Open, FileAccess.Read, FileShare.None))
                Assert(exclusive.Length == installerBytes.Length,
                    "installer file handles are released before launch");

            var lifecycle = new List<string>();
            var launchCts = new CancellationTokenSource();
            var launchToken = launchCts.Token;
            var tokenUsableAtLaunch = false;
            var launchCoordinator = new UpdateInstallerLaunchCoordinator(
                new DelegateInstallerLauncher(path =>
                {
                    Assert(File.Exists(path), "installer launch sees the finalized installer file");
                    lifecycle.Add("Process.Start");
                    return new Process();
                }));
            lifecycle.Add("download complete");
            Assert(launchCoordinator.TryLaunchAndRequestShutdown(
                    downloaded,
                    () =>
                    {
                        launchToken.ThrowIfCancellationRequested();
                        tokenUsableAtLaunch = true;
                        lifecycle.Add("installer started");
                    },
                    () => lifecycle.Add("shutdown"),
                    out _),
                "successful installer launch requests shutdown");
            Assert(lifecycle.SequenceEqual(new[]
                    { "download complete", "Process.Start", "installer started", "shutdown" }),
                "shutdown is requested only after Process.Start succeeds");
            Assert(tokenUsableAtLaunch, "installer launch does not require a disposed update CTS");

            var duplicateCallbackCount = 0;
            Assert(!launchCoordinator.TryLaunchAndRequestShutdown(
                    downloaded,
                    () => duplicateCallbackCount++,
                    () => { },
                    out var alreadyStartedError) &&
                   alreadyStartedError.Contains("已经启动", StringComparison.Ordinal),
                "installer launch coordinator rejects a duplicate launch request after the first start");
            Assert(duplicateCallbackCount == 0, "duplicate installer launch does not invoke callbacks again");
            launchCts.Dispose();
            File.Delete(downloaded);
        }

        await RunUpdateCancellationLifecycleTest(workspace);
        RunInstallerLaunchFailureTest(workspace);
        report.AppendLine("TEST 4A online update installer lifecycle: PASS (streams closed/verification/launch ordering/CTS cancellation/launch failure)");

        using (var client = new HttpClient(new StubHttpHandler(_ => new HttpResponseMessage(HttpStatusCode.ServiceUnavailable)
               {
                   Content = new StringContent("{\"success\":false,\"error\":\"rate_limited\",\"resetAt\":\"2026-08-24T18:00:00Z\"}"),
                   Headers = { { "X-Update-Error", "rate_limited" } }
               })))
        using (var service = new UpdateService(client, "1.0.0"))
        {
            var limited = await service.CheckAsync();
            Assert(limited.FailureKind == UpdateFailureKind.RateLimited &&
                   limited.ErrorMessage?.StartsWith("GitHub 更新服务请求过于频繁", StringComparison.Ordinal) == true &&
                   !limited.ErrorMessage.Contains("Response status code", StringComparison.Ordinal),
                "rate limit is classified and shown as a safe Chinese message");
        }

        var updateLog = new UpdateLog(Path.Combine(workspace, "update-test.log"));
        var skippedSettings = new AppSettings { SkippedUpdateVersion = "1.0.1" };
        var skipped = new UpdateCheckCoordinator(
            new StubUpdateService(UpdateResult("1.0.1", hasUpdate: true)), skippedSettings, updateLog);
        Assert((await skipped.CheckAsync(UpdateCheckMode.Automatic)).Kind == UpdateCheckOutcomeKind.Suppressed,
            "automatic check suppresses exactly skipped 1.0.1");

        var newer = new UpdateCheckCoordinator(
            new StubUpdateService(UpdateResult("1.0.2", hasUpdate: true)), skippedSettings, updateLog);
        Assert((await newer.CheckAsync(UpdateCheckMode.Automatic)).Kind == UpdateCheckOutcomeKind.UpdateAvailable,
            "1.0.2 breaks through skipped 1.0.1");

        var manual = new UpdateCheckCoordinator(
            new StubUpdateService(UpdateResult("1.0.1", hasUpdate: true)), skippedSettings, updateLog);
        Assert((await manual.CheckAsync(UpdateCheckMode.Manual)).Kind == UpdateCheckOutcomeKind.UpdateAvailable,
            "manual check ignores skipped version");

        using (var client = new HttpClient(new ThrowingHttpHandler()))
        using (var failedService = new UpdateService(client, "1.0.0"))
        {
            var failedResult = await failedService.CheckAsync();
            Assert(failedResult.Status == UpdateCheckResultStatus.Failed, "network failure becomes a result");
            var automaticFailure = new UpdateCheckCoordinator(
                new StubUpdateService(failedResult), new AppSettings(), updateLog);
            var manualFailure = new UpdateCheckCoordinator(
                new StubUpdateService(failedResult), new AppSettings(), updateLog);
            Assert((await automaticFailure.CheckAsync(UpdateCheckMode.Automatic)).Kind == UpdateCheckOutcomeKind.Suppressed,
                "automatic network failure is silent");
            Assert((await manualFailure.CheckAsync(UpdateCheckMode.Manual)).Kind == UpdateCheckOutcomeKind.Failed,
                "manual network failure requests user feedback");
        }

        var delayedService = new StubUpdateService(UpdateResult("1.0.0", hasUpdate: false));
        var delayedCoordinator = new UpdateCheckCoordinator(delayedService, new AppSettings(), updateLog);
        var delayedTask = delayedCoordinator.CheckAfterDelayAsync(TimeSpan.FromMilliseconds(40));
        Assert(delayedService.Calls == 0 && !delayedTask.IsCompleted,
            "startup scheduling returns before the delayed HTTP check starts");
        await delayedTask;
        Assert(delayedService.Calls == 1, "startup performs one automatic request after delay");
        Assert((await delayedCoordinator.CheckAsync(UpdateCheckMode.Automatic)).Kind == UpdateCheckOutcomeKind.AlreadyChecked &&
               delayedService.Calls == 1, "automatic check runs at most once per process");

        var concurrentService = new GatedUpdateService(UpdateResult("1.0.1", hasUpdate: true));
        var concurrentCoordinator = new UpdateCheckCoordinator(concurrentService, new AppSettings(), updateLog);
        var automaticTask = concurrentCoordinator.CheckAsync(UpdateCheckMode.Automatic);
        var manualTask = concurrentCoordinator.CheckAsync(UpdateCheckMode.Manual);
        await Task.Yield();
        Assert(concurrentService.Calls == 1, "automatic and manual checks share one in-flight request");
        concurrentService.Release();
        await Task.WhenAll(automaticTask, manualTask);
        Assert(concurrentService.Calls == 1, "shared check does not send a duplicate HTTP request");

        report.AppendLine("TEST 4 Worker updates: PASS (semantic versions/release/assets/rate-limit/cache/skip/manual/failure/delay/single-flight)");
    }

    private static async Task RunUpdateCancellationLifecycleTest(string workspace)
    {
        const string latestVersion = "2.0.82";
        var installerName = $"CloudLight-Blizzard-{latestVersion}-win-x64-Setup.exe";
        var expectedInstaller = Path.Combine(
            Path.GetTempPath(), "CloudLight Blizzard", "updates", latestVersion, installerName);
        var expectedPartial = expectedInstaller + ".partial";
        if (File.Exists(expectedInstaller)) File.Delete(expectedInstaller);
        if (File.Exists(expectedPartial)) File.Delete(expectedPartial);

        var handler = new BlockingUpdateHandler();
        var settings = new AppSettings { EnableProxy = false };
        using var clients = new CloudHttpClientFactory(
            settings, Path.Combine(workspace, "update-cancellation.log"), _ => new HttpClient(handler));
        var downloader = new UpdateDownloadService(clients);
        var result = new UpdateCheckResult
        {
            Status = UpdateCheckResultStatus.Success,
            CurrentVersion = "2.0.81",
            LatestVersion = latestVersion,
            HasUpdate = true,
            InstallerDownloadUrl =
                $"https://github.com/Cloud-Light125/CloudLight-Blizzard/releases/download/v{latestVersion}/{installerName}",
            InstallerSize = 2,
            InstallerDigest = "sha256:" + Convert.ToHexString(SHA256.HashData(new byte[] { (byte)'M', (byte)'Z' })),
            InstallerName = installerName,
            Tag = $"v{latestVersion}",
        };
        var cts = new CancellationTokenSource();
        var token = cts.Token;
        var disposedInFinally = false;
        var operation = RunDownloadWithOwnedCancellationAsync();
        await handler.Started.Task.WaitAsync(TimeSpan.FromSeconds(1));
        cts.Cancel();
        await operation;

        Assert(disposedInFinally, "cancelled update disposes CTS only after the async operation ends");
        Assert(!File.Exists(expectedInstaller) && !File.Exists(expectedPartial),
            "cancelled update never exposes an incomplete installer as final output");
        Assert(File.Exists(Path.Combine(Path.GetDirectoryName(expectedPartial)!, "update-download.json")),
            "cancelled update keeps resume metadata");
        TryDeleteDirectory(Path.GetDirectoryName(expectedPartial)!);

        async Task RunDownloadWithOwnedCancellationAsync()
        {
            try
            {
                await downloader.DownloadInstallerAsync(result, cancellationToken: token);
                throw new InvalidOperationException("cancelled update unexpectedly completed");
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested)
            {
            }
            finally
            {
                disposedInFinally = true;
                cts.Dispose();
            }
        }
    }

    private static void RunInstallerLaunchFailureTest(string workspace)
    {
        var installerPath = Path.Combine(workspace, "installer-launch-failure-test.exe");
        File.WriteAllBytes(installerPath, new byte[] { (byte)'M', (byte)'Z' });
        var shutdownRequested = false;
        var coordinator = new UpdateInstallerLaunchCoordinator(
            new DelegateInstallerLauncher(_ => throw new InvalidOperationException("simulated launcher failure")));
        Assert(!coordinator.TryLaunchAndRequestShutdown(
                installerPath, () => { }, () => shutdownRequested = true, out var error),
            "installer launch failure is reported");
        Assert(error.Contains("simulated launcher failure", StringComparison.Ordinal) && !shutdownRequested,
            "installer launch failure never requests app shutdown");
    }

    private static void RunRegionPreparationGuideTest(StringBuilder report)
    {
        Assert(MainWindow.ShouldStartHidden(new[] { "--tray" }, startMinimized: false),
            "--tray starts without showing the main window");
        Assert(MainWindow.ShouldStartHidden(Array.Empty<string>(), startMinimized: true),
            "StartMinimized starts without showing the main window");
        Assert(!MainWindow.ShouldStartHidden(new[] { "--tray", "--visible" }, startMinimized: true),
            "--visible explicitly overrides hidden startup");

        const string backupRoot = @"D:\RegionBackup";
        var empty = new RegionSnapshotStatus { State = RegionBackupState.Empty, GamePathValid = true };
        var notPrepared = RegionPreparationGuide.Create(empty, RegionOperationPhase.None, false, false, null, backupRoot);
        Assert(notPrepared.State == RegionPreparationState.NotPrepared && notPrepared.ShowPreparationWarning &&
               notPrepared.Title == "记录当前区服文件" &&
               notPrepared.PreparationWarningTitle == "备份期间请勿启动游戏" &&
               notPrepared.PreparationWarningText.Contains("即使 Battle.net 已经显示“开始游戏”", StringComparison.Ordinal),
            "Step1 shows the no-launch warning");
        AssertActions(notPrepared, RegionPreparationAction.ChooseChina, RegionPreparationAction.ChooseInternational);

        var preparingCurrent = RegionPreparationGuide.Create(empty, RegionOperationPhase.PreparingCurrentRegion,
            false, true, new RegionProgress("copying", 1, 2), backupRoot);
        Assert(preparingCurrent.State == RegionPreparationState.PreparingCurrentRegion && preparingCurrent.CanCancel &&
               preparingCurrent.ShowPreparationWarning,
            "PreparingCurrentRegion shows the warning and only allows cancellation");
        AssertActions(preparingCurrent, RegionPreparationAction.Cancel);

        var waitingStatus = new RegionSnapshotStatus
        {
            State = RegionBackupState.Preparing,
            GamePathValid = true,
            PendingSourceRegion = GameRegion.China,
            PendingTargetRegion = GameRegion.International,
            ChinaCaptured = true,
        };
        var waiting = RegionPreparationGuide.Create(waitingStatus, RegionOperationPhase.None, false, false, null, backupRoot);
        Assert(waiting.State == RegionPreparationState.WaitingForOtherRegion && waiting.ShowPreparationWarning &&
               waiting.ContinueButtonText == "我已完成国际服更新" &&
               waiting.Description.Contains("不要启动游戏", StringComparison.Ordinal),
            "Step2 names the target region and forbids launching the game");
        AssertActions(waiting, RegionPreparationAction.ContinueOtherRegion, RegionPreparationAction.Restart);
        waitingStatus.PendingSourceRegion = GameRegion.International;
        waitingStatus.PendingTargetRegion = GameRegion.China;
        var waitingForChina = RegionPreparationGuide.Create(waitingStatus, RegionOperationPhase.None,
            false, false, null, backupRoot);
        Assert(waitingForChina.ContinueButtonText == "我已完成国服更新",
            "international source explicitly names China in step two");
        waitingStatus.PendingSourceRegion = GameRegion.China;
        waitingStatus.PendingTargetRegion = GameRegion.International;

        var building = RegionPreparationGuide.Create(waitingStatus, RegionOperationPhase.BuildingBackup,
            false, true, new RegionProgress("building", 1, 3), backupRoot);
        Assert(building.State == RegionPreparationState.BuildingBackup && building.CanCancel,
            "BuildingBackup only allows cancellation");
        AssertActions(building, RegionPreparationAction.Cancel);

        var step3Status = new RegionSnapshotStatus
        {
            State = RegionBackupState.Preparing,
            BackupMode = RegionBackupMode.VerifiedDifference,
            PreparationCheckpoint = RegionPreparationCheckpoint.Step2Ready,
            GamePathValid = true,
            PendingSourceRegion = GameRegion.China,
            PendingTargetRegion = GameRegion.International,
        };
        var waitingForOriginal = RegionPreparationGuide.Create(step3Status, RegionOperationPhase.None,
            false, false, null, backupRoot);
        Assert(waitingForOriginal.State == RegionPreparationState.WaitingForOriginalRegion &&
               waitingForOriginal.ShowPreparationWarning && waitingForOriginal.Title == "返回国服验证" &&
               waitingForOriginal.Description.Contains("不要启动游戏", StringComparison.Ordinal),
            "Step3 shows the no-launch warning before final verification");

        var readyStatus = new RegionSnapshotStatus
        {
            State = RegionBackupState.Ready,
            GamePathValid = true,
            CurrentRegion = CurrentGameRegion.China,
            GenerationCompatibility = GenerationCompatibility.Compatible,
            SwitchEligibility = RegionSwitchEligibility.Normal,
            ChinaBackupComplete = true,
            InternationalBackupComplete = true,
            ActiveGenerationId = "existing-active",
            ExactSnapshotMatch = true,
        };
        var ready = RegionPreparationGuide.Create(readyStatus, RegionOperationPhase.None, false, false, null, backupRoot);
        Assert(ready.State == RegionPreparationState.Ready && !ready.ShowNotPrepared,
            "Ready hides first preparation choices");
        AssertActions(ready, RegionPreparationAction.Validate, RegionPreparationAction.Restart);

        var restartStep = RegionPreparationGuide.Create(readyStatus, RegionOperationPhase.None, true, false, null, backupRoot);
        Assert(restartStep.State == RegionPreparationState.NotPrepared && readyStatus.ActiveGenerationId == "existing-active",
            "reprepare returns to step one without mutating active generation status");

        var outdatedStatus = new RegionSnapshotStatus
        {
            State = RegionBackupState.Stale,
            GamePathValid = true,
            GenerationCompatibility = GenerationCompatibility.Updated,
            SwitchEligibility = RegionSwitchEligibility.BestEffort,
            PossibleGameUpdate = true,
            ChinaBackupComplete = true,
            InternationalBackupComplete = true,
            ActiveGenerationId = "existing-active",
        };
        var outdated = RegionPreparationGuide.Create(outdatedStatus, RegionOperationPhase.None, false, false, null, backupRoot);
        Assert(outdated.State == RegionPreparationState.Mixed &&
               outdated.Title == "检测到游戏文件可能已经更新",
            "updated generation maps to BestEffort recovery");
        AssertActions(outdated, RegionPreparationAction.RestoreChina, RegionPreparationAction.RestoreInternational);

        var legacyStatus = new RegionSnapshotStatus
        {
            State = RegionBackupState.Legacy,
            GamePathValid = true,
            ActiveGenerationId = "legacy-active",
        };
        var legacy = RegionPreparationGuide.Create(legacyStatus, RegionOperationPhase.None, false, false, null, backupRoot);
        Assert(legacy.State == RegionPreparationState.Outdated, "legacy generation maps to Outdated");
        AssertActions(legacy, RegionPreparationAction.Restart);

        readyStatus.CurrentRegion = CurrentGameRegion.Mixed;
        readyStatus.ExactSnapshotMatch = false;
        var mixed = RegionPreparationGuide.Create(readyStatus, RegionOperationPhase.None, false, false, null, backupRoot);
        Assert(mixed.State == RegionPreparationState.Mixed && mixed.CanRestore, "Mixed offers local recovery");
        AssertActions(mixed, RegionPreparationAction.RestoreChina, RegionPreparationAction.RestoreInternational);

        readyStatus.CurrentRegion = CurrentGameRegion.Unknown;
        readyStatus.GenerationCompatibility = GenerationCompatibility.Unknown;
        readyStatus.SwitchEligibility = RegionSwitchEligibility.BestEffort;
        var bestEffort = RegionPreparationGuide.Create(readyStatus, RegionOperationPhase.None, false, false, null, backupRoot);
        Assert(bestEffort.State == RegionPreparationState.Mixed && bestEffort.CanSwitchChina &&
               bestEffort.CanSwitchInternational && bestEffort.Title == "当前游戏版本无法确认",
            "Unknown compatibility with complete backups offers both BestEffort restore actions");
        AssertActions(bestEffort, RegionPreparationAction.RestoreChina, RegionPreparationAction.RestoreInternational);

        var error = RegionPreparationGuide.Create(empty, RegionOperationPhase.None, false, false, null,
            backupRoot, error: "simulated read error");
        Assert(error.State == RegionPreparationState.Error && error.CanRetry, "Error offers retry only");
        AssertActions(error, RegionPreparationAction.Retry);

        readyStatus.CurrentRegion = CurrentGameRegion.China;
        var busy = RegionPreparationGuide.Create(readyStatus, RegionOperationPhase.None, false, true, null, backupRoot);
        Assert(!busy.CanChangePaths && !busy.CanClear && !busy.CanRestart && !busy.CanValidate &&
               !busy.CanSwitchChina && !busy.CanSwitchInternational,
            "Busy disables path changes, clear, reprepare, validation, and region switching");
        report.AppendLine("TEST 5 region preparation guide: PASS (NotPrepared/Preparing/Waiting/Building/Ready/Outdated/Mixed/Error and Busy gates)");
    }

    private static async Task RunRegionGenerationTest(string workspace, StringBuilder report)
    {
        Assert(OverwatchRegionManager.ClassifyEvidence(5, 1, 0, 0) == RegionEvidenceResult.StrongChina,
            "ChinaOnly evidence with a clear numerical lead resolves China despite a small opposite residue");
        Assert(OverwatchRegionManager.ClassifyEvidence(1, 5, 0, 0) == RegionEvidenceResult.StrongInternational,
            "InternationalOnly evidence with a clear numerical lead resolves International despite a small opposite residue");
        Assert(OverwatchRegionManager.ClassifyEvidence(2, 1, 0, 0) == RegionEvidenceResult.StrongConflict &&
               OverwatchRegionManager.ClassifyEvidence(2, 2, 0, 0) == RegionEvidenceResult.StrongConflict,
            "close or equal exclusive evidence remains a strong conflict");

        var game = Path.Combine(workspace, "game");
        var store = Path.Combine(workspace, "region-store");
        Directory.CreateDirectory(game);
        File.WriteAllText(Path.Combine(game, "Overwatch.exe"), "test executable");
        Directory.CreateDirectory(Path.Combine(game, "_retail_"));
        File.WriteAllText(Path.Combine(game, "_retail_", "Overwatch_loader.dll"), "common loader");
        File.WriteAllText(Path.Combine(game, ".build.info"), "China build metadata");
        File.WriteAllText(Path.Combine(game, "same.txt"), "same");
        File.WriteAllText(Path.Combine(game, "china-only.txt"), "china only");
        File.WriteAllText(Path.Combine(game, "different.txt"), "China content");
        CreateLargeFile(Path.Combine(game, "large.bin"), 256L * 1024 * 1024, 0x43);

        var manager = new OverwatchRegionManager(store, () => false, 0);
        Assert(await manager.StartPreparationAsync(game, GameRegion.China) == RegionBackupState.Preparing,
            "first side enters preparation");

        File.Delete(Path.Combine(game, "china-only.txt"));
        File.WriteAllText(Path.Combine(game, "international-only.txt"), "international only");
        File.WriteAllText(Path.Combine(game, "different.txt"), "International content");
        File.WriteAllText(Path.Combine(game, ".build.info"), "International build metadata");
        CreateLargeFile(Path.Combine(game, "large.bin"), 256L * 1024 * 1024, 0x49);
        WriteRuntimeFiles(game);
        Assert(await manager.ContinuePreparationAsync(game) == RegionBackupState.Ready,
            "one cross-region continuation makes both sides ready");

        var status = await manager.GetStatusAsync(game);
        Assert(status.State == RegionBackupState.Ready && status.ChinaBackupComplete && status.InternationalBackupComplete,
            "generation is ready for both regions");
        Assert(status.CurrentRegion == CurrentGameRegion.International && status.ExactSnapshotMatch,
            "activation records the second captured region as the exact current snapshot");
        Assert(status.LastSuccessfulRegion == GameRegion.International &&
               status.LastSuccessfulGenerationId == status.ActiveGenerationId,
            "generation activation persists LastSuccessfulRegion and generation id");
        var generationRoot = Path.Combine(store, "generations", status.ActiveGenerationId!);
        var generation = OverwatchRegionBackupStore.ReadJson<OverwatchRegionGeneration>(Path.Combine(generationRoot, "pair.json"))!;
        var kinds = generation.Differences.ToDictionary(item => item.RelativePath, item => item.Kind, StringComparer.OrdinalIgnoreCase);
        Assert(!kinds.ContainsKey("same.txt"), "Same is not persisted as a switch operation");
        Assert(kinds["china-only.txt"] == RegionDifferenceKind.ChinaOnly, "ChinaOnly classification");
        Assert(kinds["international-only.txt"] == RegionDifferenceKind.InternationalOnly, "InternationalOnly classification");
        Assert(kinds["different.txt"] == RegionDifferenceKind.Different, "Different classification");
        Assert(kinds["large.bin"] == RegionDifferenceKind.Different, "large Different classification");
        Assert(!generation.Differences.Any(item => OverwatchRegionScanner.IsIgnoredRelativePath(item.RelativePath)),
            "runtime cache/log/temp/dump/shmem files are excluded before comparison");
        Assert(new FileInfo(Path.Combine(generationRoot, "backups", "china", "large.bin")).Length == 256L * 1024 * 1024,
            "full China large file stored");
        Assert(new FileInfo(Path.Combine(generationRoot, "backups", "international", "large.bin")).Length == 256L * 1024 * 1024,
            "full International large file stored");

        // Build metadata may be partially changed by Battle.net and must not by itself stale the generation.
        File.WriteAllText(Path.Combine(game, ".build.info"), "partial Battle.net metadata");

        // A normal runtime drift in Different must not turn a usable International directory into Mixed.
        File.WriteAllText(Path.Combine(game, "different.txt"), "International runtime drift");
        var internationalDrift = await manager.GetStatusAsync(game);
        Assert(internationalDrift.CurrentRegion == CurrentGameRegion.International &&
               !internationalDrift.ExactSnapshotMatch,
            "International + ordinary Different drift remains International");

        await manager.NormalizeToRegionAsync(game, GameRegion.China);
        File.WriteAllText(Path.Combine(game, "different.txt"), "China runtime drift");
        var chinaDrift = await manager.GetStatusAsync(game);
        Assert(chinaDrift.CurrentRegion == CurrentGameRegion.China && !chinaDrift.ExactSnapshotMatch,
            "China + ordinary Different drift remains China");

        File.Delete(Path.Combine(game, "china-only.txt"));
        File.WriteAllText(Path.Combine(game, ".build.info"), "runtime metadata drift after China normalize");
        var extensivelyDamaged = await manager.GetStatusAsync(game);
        Assert(extensivelyDamaged.CurrentRegion == CurrentGameRegion.Unknown,
            "widespread target snapshot damage is not hidden by LastSuccessfulRegion");

        // Clear China-only evidence and establish the opposite exclusive evidence. This is strong enough
        // to correct a stale LastSuccessfulRegion without requiring a full snapshot match.
        File.WriteAllText(Path.Combine(game, "international-only.txt"), "international only");
        var corrected = await manager.GetStatusAsync(game);
        Assert(corrected.CurrentRegion == CurrentGameRegion.International &&
               corrected.LastSuccessfulRegion == GameRegion.International,
            "strong opposite evidence corrects LastSuccessfulRegion");

        // Construct a true mixed directory while preserving the common baseline.
        File.WriteAllText(Path.Combine(game, "china-only.txt"), "china only");
        File.WriteAllText(Path.Combine(game, "international-only.txt"), "international only");
        File.WriteAllText(Path.Combine(game, "different.txt"), "mixed random content");
        Assert((await manager.GetStatusAsync(game)).CurrentRegion == CurrentGameRegion.Mixed,
            "mixed region is detected independently from generation compatibility");
        Assert((await manager.GetStatusAsync(game)).GenerationCompatibility == GenerationCompatibility.Compatible,
            "mixed difference files and .build.info do not stale the generation");

        var internationalResult = await manager.NormalizeToRegionAsync(game, GameRegion.International);
        Assert(internationalResult.Verified, "Mixed -> International completes full hash verification");
        var pointerAfterInternational = OverwatchRegionBackupStore.ReadJson<ActiveGenerationPointer>(
            Path.Combine(store, "active-generation.json"))!;
        Assert(pointerAfterInternational.LastSuccessfulRegion == GameRegion.International &&
               pointerAfterInternational.LastSuccessfulGenerationId == generation.GenerationId,
            "successful International normalize persists successful region and active generation");
        Assert(!File.Exists(Path.Combine(game, "china-only.txt")), "Mixed -> International removes ChinaOnly");
        Assert(File.ReadAllText(Path.Combine(game, "international-only.txt")) == "international only",
            "Mixed -> International restores InternationalOnly");
        Assert(File.ReadAllText(Path.Combine(game, "different.txt")) == "International content",
            "Mixed -> International restores Different");

        File.WriteAllText(Path.Combine(game, "china-only.txt"), "china only");
        File.WriteAllText(Path.Combine(game, "international-only.txt"), "international only");
        File.WriteAllText(Path.Combine(game, "different.txt"), "another mixed value");
        var chinaResult = await manager.NormalizeToRegionAsync(game, GameRegion.China);
        Assert(chinaResult.Verified, "Mixed -> China completes full hash verification");
        Assert(File.Exists(Path.Combine(game, "china-only.txt")), "ChinaOnly restored");
        Assert(!File.Exists(Path.Combine(game, "international-only.txt")), "InternationalOnly removed for China");
        Assert(FirstByte(Path.Combine(game, "large.bin")) == 0x43, "China large file restored exactly");
        await manager.NormalizeToRegionAsync(game, GameRegion.International);
        Assert(!File.Exists(Path.Combine(game, "china-only.txt")), "ChinaOnly removed for International");
        Assert(File.Exists(Path.Combine(game, "international-only.txt")), "InternationalOnly restored");
        Assert(FirstByte(Path.Combine(game, "large.bin")) == 0x49, "International large file restored exactly");

        // A failed normalize must leave the last successful state untouched. Backup validation fails before
        // any live game file is changed.
        var stalePointer = OverwatchRegionBackupStore.ReadJson<ActiveGenerationPointer>(
            Path.Combine(store, "active-generation.json"))!;
        stalePointer.LastSuccessfulRegion = GameRegion.China;
        OverwatchRegionBackupStore.WriteJson(Path.Combine(store, "active-generation.json"), stalePointer);
        var pointerBeforeFailedNormalize = File.ReadAllText(Path.Combine(store, "active-generation.json"));
        File.WriteAllText(Path.Combine(generationRoot, "backups", "china", "different.txt"), "corrupt backup");
        try
        {
            await manager.NormalizeToRegionAsync(game, GameRegion.China);
            throw new InvalidOperationException("corrupt target backup should have rejected normalize");
        }
        catch (InvalidDataException) { }
        Assert(File.ReadAllText(Path.Combine(store, "active-generation.json")) == pointerBeforeFailedNormalize,
            "failed normalize does not update LastSuccessfulRegion even when strong evidence is opposite");

        File.WriteAllText(Path.Combine(game, "_retail_", "Overwatch_loader.dll"), "new game version");
        var updatedStatus = await manager.GetStatusAsync(game);
        Assert(updatedStatus.GenerationCompatibility == GenerationCompatibility.Updated &&
               updatedStatus.State == RegionBackupState.Stale &&
               updatedStatus.SwitchEligibility == RegionSwitchEligibility.BackupUnavailable,
            "changed common Same core marks generation updated while corrupt FullSnapshot remains blocked");
        try
        {
            await manager.NormalizeToRegionAsync(game, GameRegion.China);
            throw new InvalidOperationException("corrupt FullSnapshot should remain blocked after a game update");
        }
        catch (InvalidDataException) { }

        var pointerBefore = File.ReadAllText(Path.Combine(store, "active-generation.json"));
        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();
        try { await manager.StartPreparationAsync(game, GameRegion.International, cancellationToken: cancelled.Token); }
        catch (OperationCanceledException) { }
        Assert(File.ReadAllText(Path.Combine(store, "active-generation.json")) == pointerBefore,
            "failed/cancelled next generation keeps active generation");

        var legacyRoot = Path.Combine(workspace, "legacy-store");
        Directory.CreateDirectory(Path.Combine(legacyRoot, "manifests"));
        File.WriteAllText(Path.Combine(legacyRoot, "manifests", "pair.json"), "{\"SchemaVersion\":1}");
        var legacyManager = new OverwatchRegionManager(legacyRoot, () => false, 0);
        Assert((await legacyManager.GetStatusAsync(game)).State == RegionBackupState.Legacy,
            "old schema is rejected");
        report.AppendLine("TEST 6 Generation/Staging: PASS (drift-tolerant detection, successful/failed normalize state, strict FullSnapshot restore hash, 256MB copies)");
    }

    private static async Task RunVerifiedDifferenceRegionTest(string workspace, StringBuilder report)
    {
        var game = Path.Combine(workspace, "verified-game");
        var store = Path.Combine(workspace, "verified-store");
        Directory.CreateDirectory(game);
        File.WriteAllText(Path.Combine(game, "Overwatch.exe"), "stable executable");
        File.WriteAllText(Path.Combine(game, "foo.txt"), "AAA");
        File.WriteAllText(Path.Combine(game, "runtime.dat"), "111");
        File.WriteAllText(Path.Combine(game, "broken.txt"), "CCC");
        File.WriteAllText(Path.Combine(game, "locked.txt"), "AAA");
        for (var i = 2; i <= 5; i++) File.WriteAllText(Path.Combine(game, $"different-{i}.txt"), $"A0{i}");
        File.WriteAllText(Path.Combine(game, "china-only.txt"), "China only");

        var gameRunningManager = new OverwatchRegionManager(store, () => true, 0);
        var step1Blocked = false;
        try
        {
            await gameRunningManager.StartPreparationAsync(game, GameRegion.China,
                RegionBackupMode.VerifiedDifference);
        }
        catch (InvalidOperationException ex) when (ex.Message.Contains("备份期间不要启动游戏", StringComparison.Ordinal))
        {
            step1Blocked = true;
        }
        Assert(step1Blocked && (await gameRunningManager.GetStatusAsync(game, verifyFiles: false)).State ==
               RegionBackupState.Empty, "running game blocks Step1 before preparation data is written");

        var manager = new OverwatchRegionManager(store, () => false, 0);
        Assert(await manager.StartPreparationAsync(game, GameRegion.China,
                   RegionBackupMode.VerifiedDifference) == RegionBackupState.Preparing,
            "verified Step1 enters persistent preparation");
        var step1 = await manager.GetStatusAsync(game, verifyFiles: false);
        Assert(step1.BackupMode == RegionBackupMode.VerifiedDifference &&
               step1.PreparationCheckpoint == RegionPreparationCheckpoint.Step1Ready,
            "verified Step1Ready survives through state.json");
        Assert(!Directory.EnumerateFiles(Path.Combine(store, "preparation"), "*",
                SearchOption.AllDirectories).Any(path => path.EndsWith("foo.txt", StringComparison.OrdinalIgnoreCase)),
            "verified Step1 writes metadata only and does not copy game contents");
        gameRunningManager = new OverwatchRegionManager(store, () => true, 0);
        var step2Blocked = false;
        try
        {
            await gameRunningManager.ContinuePreparationAsync(game);
        }
        catch (InvalidOperationException ex) when (ex.Message.Contains("备份期间不要启动游戏", StringComparison.Ordinal))
        {
            step2Blocked = true;
        }
        Assert(step2Blocked && (await gameRunningManager.GetStatusAsync(game, verifyFiles: false))
                   .PreparationCheckpoint == RegionPreparationCheckpoint.Step1Ready,
            "running game blocks Step2 and preserves the Step1 checkpoint");
        manager = new OverwatchRegionManager(store, () => false, 0);

        File.WriteAllText(Path.Combine(game, "foo.txt"), "BBB");
        File.WriteAllText(Path.Combine(game, "runtime.dat"), "222");
        File.WriteAllText(Path.Combine(game, "broken.txt"), "DDD");
        File.WriteAllText(Path.Combine(game, "locked.txt"), "BBB");
        for (var i = 2; i <= 5; i++) File.WriteAllText(Path.Combine(game, $"different-{i}.txt"), $"B0{i}");
        File.Delete(Path.Combine(game, "china-only.txt"));
        File.WriteAllText(Path.Combine(game, "international-only.txt"), "International only");
        await using (var locked = new FileStream(Path.Combine(game, "locked.txt"), FileMode.Open,
                         FileAccess.ReadWrite, FileShare.None))
        {
            Assert(await manager.ContinuePreparationAsync(game) == RegionBackupState.Preparing,
                "one locked candidate does not fail verified Step2");
        }
        var step2 = await manager.GetStatusAsync(game, verifyFiles: false);
        Assert(step2.PreparationCheckpoint == RegionPreparationCheckpoint.Step2Ready &&
               File.ReadAllText(Path.Combine(store, "preparation", "current", "candidate",
                    "international", "foo.txt")) == "BBB" &&
               step2.HasWarnings && step2.SkippedFileCount == 1,
            "Step2Ready persists usable B1 candidates and records one unavailable candidate");
        File.WriteAllText(Path.Combine(store, "preparation", "current", "candidate",
            "international", "broken.txt"), "XXX");
        gameRunningManager = new OverwatchRegionManager(store, () => true, 0);
        var step3Blocked = false;
        try
        {
            await gameRunningManager.ContinuePreparationAsync(game);
        }
        catch (InvalidOperationException ex) when (ex.Message.Contains("备份期间不要启动游戏", StringComparison.Ordinal))
        {
            step3Blocked = true;
        }
        Assert(step3Blocked && (await gameRunningManager.GetStatusAsync(game, verifyFiles: false))
                   .PreparationCheckpoint == RegionPreparationCheckpoint.Step2Ready,
            "running game blocks Step3 and preserves the Step2 checkpoint");
        manager = new OverwatchRegionManager(store, () => false, 0);

        File.WriteAllText(Path.Combine(game, "foo.txt"), "AAA");
        File.WriteAllText(Path.Combine(game, "runtime.dat"), "333");
        File.WriteAllText(Path.Combine(game, "broken.txt"), "CCC");
        File.WriteAllText(Path.Combine(game, "locked.txt"), "AAA");
        for (var i = 2; i <= 5; i++) File.WriteAllText(Path.Combine(game, $"different-{i}.txt"), $"A0{i}");
        File.WriteAllText(Path.Combine(game, "china-only.txt"), "China only");
        File.Delete(Path.Combine(game, "international-only.txt"));
        Assert(await manager.ContinuePreparationAsync(game) == RegionBackupState.Ready,
            "verified Step3 commits a Ready generation even with a rejected runtime change");
        var ready = await manager.GetStatusAsync(game);
        var generationRoot = Path.Combine(store, "generations", ready.ActiveGenerationId!);
        var generation = OverwatchRegionBackupStore.ReadJson<OverwatchRegionGeneration>(
            Path.Combine(generationRoot, "pair.json"))!;
        var differences = generation.Differences.ToDictionary(value => value.RelativePath,
            StringComparer.OrdinalIgnoreCase);
        Assert(generation.BackupMode == RegionBackupMode.VerifiedDifference &&
               generation.State == RegionBackupState.Ready &&
               differences["foo.txt"].Kind == RegionDifferenceKind.Different,
            "A1=AAA, B1=BBB, A2=AAA becomes verified Different with Ready generation");
        Assert(differences["china-only.txt"].Kind == RegionDifferenceKind.ChinaOnly &&
               differences["international-only.txt"].Kind == RegionDifferenceKind.InternationalOnly,
            "verified AOnly and BOnly are retained with directional deletion semantics");
        Assert(!differences.ContainsKey("runtime.dat") && !differences.ContainsKey("broken.txt") &&
               !differences.ContainsKey("locked.txt") &&
               generation.VerificationSummary is
               { RejectedCount: 1, VerifiedCount: 7, SkippedFileCount: 2, HasWarnings: true },
            "rejected runtime and two file issues are excluded without failing preparation");
        Assert(generation.VerificationSummary!.Results.Any(item => item.RelativePath == "foo.txt" &&
                   item.Outcome == CandidateVerificationOutcome.VerifiedUsable) &&
               generation.VerificationSummary.Results.Any(item => item.RelativePath == "runtime.dat" &&
                   item.Outcome == CandidateVerificationOutcome.VerificationRejected) &&
               generation.VerificationSummary.Results.Any(item => item.RelativePath == "broken.txt" &&
                   item.Outcome == CandidateVerificationOutcome.FileIssueSkipped),
            "Step3 persists VerifiedUsable, VerificationRejected, and FileIssueSkipped classifications");
        Assert(File.ReadAllText(Path.Combine(generationRoot, "backups", "china", "foo.txt")) == "AAA" &&
               File.ReadAllText(Path.Combine(generationRoot, "backups", "international", "foo.txt")) == "BBB",
            "verified Different stores both A and B contents");

        File.WriteAllText(Path.Combine(game, "unverified-cache.bin"), "must remain untouched");
        var corruptTargetBackup = Path.Combine(generationRoot, "backups", "international", "foo.txt");
        File.WriteAllText(corruptTargetBackup, "ZZZ");
        var damagedStatus = await manager.GetStatusAsync(game, verifyBackupHashes: true);
        Assert(damagedStatus.SwitchEligibility == RegionSwitchEligibility.Normal &&
               damagedStatus.BackupFileIssueCount == 1,
            "one damaged VerifiedDifference entry remains switchable and is reported as a file warning");
        var partial = await manager.NormalizeToRegionAsync(game, GameRegion.International);
        Assert(partial.Outcome == RegionSwitchOutcome.PartialSuccess && partial.FailedCount == 1 &&
               !partial.Verified && File.ReadAllText(Path.Combine(game, "foo.txt")) == "AAA" &&
               Enumerable.Range(2, 4).All(i =>
                   File.ReadAllText(Path.Combine(game, $"different-{i}.txt")) == $"B0{i}") &&
               !File.Exists(Path.Combine(game, "china-only.txt")) &&
               File.Exists(Path.Combine(game, "international-only.txt")) &&
               File.Exists(Path.Combine(game, "unverified-cache.bin")),
            "one corrupt verified backup is skipped while all other independent entries continue");

        var legacyGame = Path.Combine(workspace, "legacy-full-game");
        var legacyStore = Path.Combine(workspace, "legacy-full-store");
        Directory.CreateDirectory(legacyGame);
        File.WriteAllText(Path.Combine(legacyGame, "Overwatch.exe"), "stable executable");
        File.WriteAllText(Path.Combine(legacyGame, "different.txt"), "China");
        var fullManager = new OverwatchRegionManager(legacyStore, () => false, 0);
        await fullManager.StartPreparationAsync(legacyGame, GameRegion.China, RegionBackupMode.FullSnapshot);
        File.WriteAllText(Path.Combine(legacyGame, "different.txt"), "International");
        await fullManager.ContinuePreparationAsync(legacyGame);
        var legacyStatus = await fullManager.GetStatusAsync(legacyGame);
        var pairFile = Path.Combine(legacyStore, "generations", legacyStatus.ActiveGenerationId!, "pair.json");
        File.WriteAllText(pairFile, File.ReadAllText(pairFile)
            .Replace("  \"BackupMode\": \"FullSnapshot\",\r\n", "", StringComparison.Ordinal)
            .Replace("  \"BackupMode\": \"FullSnapshot\",\n", "", StringComparison.Ordinal));
        var legacyReader = new OverwatchRegionManager(legacyStore, () => false, 0);
        var legacyReady = await legacyReader.GetStatusAsync(legacyGame);
        Assert(legacyReady.State == RegionBackupState.Ready &&
               legacyReady.BackupMode == RegionBackupMode.FullSnapshot,
            "Generation without BackupMode is read as legacy FullSnapshot");
        await legacyReader.NormalizeToRegionAsync(legacyGame, GameRegion.China);
        Assert(File.ReadAllText(Path.Combine(legacyGame, "different.txt")) == "China",
            "legacy FullSnapshot generation remains switchable without re-preparation");
        var activeBeforeNewPreparation = File.ReadAllText(Path.Combine(legacyStore, "active-generation.json"));
        await legacyReader.StartPreparationAsync(legacyGame, GameRegion.China,
            RegionBackupMode.VerifiedDifference);
        Assert(File.ReadAllText(Path.Combine(legacyStore, "active-generation.json")) == activeBeforeNewPreparation,
            "new verified Step1 does not replace the old Ready active generation");
        await legacyReader.NormalizeToRegionAsync(legacyGame, GameRegion.International);
        Assert(File.ReadAllText(Path.Combine(legacyGame, "different.txt")) == "International" &&
               File.ReadAllText(Path.Combine(legacyStore, "active-generation.json"))
                   .Contains(legacyStatus.ActiveGenerationId!, StringComparison.OrdinalIgnoreCase),
            "old FullSnapshot active generation remains usable while verified preparation is incomplete");

        Assert(await legacyReader.ContinuePreparationAsync(legacyGame) == RegionBackupState.Preparing,
            "new verified preparation reaches Step2Ready without replacing old Active");
        File.WriteAllText(Path.Combine(legacyStore, "preparation", "current", "candidate",
            "international", "different.txt"), "Broken");
        await legacyReader.NormalizeToRegionAsync(legacyGame, GameRegion.China);
        var activeBeforeNoUsable = File.ReadAllText(Path.Combine(legacyStore, "active-generation.json"));
        try
        {
            await legacyReader.ContinuePreparationAsync(legacyGame);
            throw new InvalidOperationException("zero usable candidates should not activate an empty generation");
        }
        catch (InvalidOperationException ex) when (ex.Message.Contains("未生成可用", StringComparison.Ordinal)) { }
        var noUsableStatus = await legacyReader.GetStatusAsync(legacyGame, verifyFiles: false);
        Assert(File.ReadAllText(Path.Combine(legacyStore, "active-generation.json")) == activeBeforeNoUsable &&
               noUsableStatus.PreparationCheckpoint == RegionPreparationCheckpoint.Step2Ready,
            "zero usable candidates preserve old Active and keep Step3 retryable");

        report.AppendLine("VerifiedDifference region preparation: PASS (per-file Step2/Step3 classification, partial daily restore, zero-usable old Active protection, legacy FullSnapshot)");
    }

    private static async Task RunRegionMaintenanceTests(string workspace, StringBuilder report)
    {
        static RegionFileEntry Entry(string path, string content)
        {
            var bytes = Encoding.UTF8.GetBytes(content);
            return new RegionFileEntry
            {
                RelativePath = path,
                Size = bytes.Length,
                Sha256 = Convert.ToHexString(SHA256.HashData(bytes)),
            };
        }

        static void WriteBackup(OverwatchRegionBackupStore store, string generationId,
            GameRegion region, string path, string content)
        {
            var file = store.BackupFile(generationId, region, path);
            Directory.CreateDirectory(Path.GetDirectoryName(file)!);
            File.WriteAllText(file, content);
        }

        static OverwatchRegionGeneration SeedGeneration(string root, string id,
            IReadOnlyList<RegionDifference> differences, GameRegion current,
            OverwatchRegionManifest? chinaReference = null,
            OverwatchRegionManifest? internationalReference = null)
        {
            var store = new OverwatchRegionBackupStore(root);
            var generation = new OverwatchRegionGeneration
            {
                GenerationId = id,
                State = RegionBackupState.Ready,
                BackupMode = RegionBackupMode.VerifiedDifference,
                SourceRegion = GameRegion.China,
                TargetRegion = GameRegion.International,
                ChinaBackupComplete = true,
                InternationalBackupComplete = true,
                Differences = differences.ToList(),
                ChinaReferenceComplete = chinaReference is not null,
                InternationalReferenceComplete = internationalReference is not null,
            };
            var china = new OverwatchRegionManifest { Region = GameRegion.China };
            var international = new OverwatchRegionManifest { Region = GameRegion.International };
            foreach (var difference in differences)
            {
                if (difference.China is not null) china.Files[difference.RelativePath] = difference.China;
                if (difference.International is not null) international.Files[difference.RelativePath] = difference.International;
            }
            generation.ChinaManifestId = china.ManifestId;
            generation.InternationalManifestId = international.ManifestId;
            store.SaveGenerationManifest(id, china);
            store.SaveGenerationManifest(id, international);
            if (chinaReference is not null) store.SaveGenerationReferenceManifest(id, chinaReference);
            if (internationalReference is not null) store.SaveGenerationReferenceManifest(id, internationalReference);
            store.SaveGeneration(generation);
            store.Activate(id, current);
            return generation;
        }

        // Test 1: a pair.json without a Step4 field remains Ready and can derive a new generation.
        var step4Game = Path.Combine(workspace, "step4-game");
        var step4Root = Path.Combine(workspace, "step4-store");
        Directory.CreateDirectory(step4Game);
        File.WriteAllText(Path.Combine(step4Game, "Overwatch.exe"), "stable executable");
        File.WriteAllText(Path.Combine(step4Game, "foo.dat"), "BBB");
        File.WriteAllText(Path.Combine(step4Game, "unstable.dat"), "B2 changed");
        var foo = new RegionDifference
        {
            RelativePath = "foo.dat", Kind = RegionDifferenceKind.Different,
            China = Entry("foo.dat", "AAA"), International = Entry("foo.dat", "BBB"),
        };
        var unstable = new RegionDifference
        {
            RelativePath = "unstable.dat", Kind = RegionDifferenceKind.Different,
            China = Entry("unstable.dat", "A stable"), International = Entry("unstable.dat", "B1 stable"),
        };
        SeedGeneration(step4Root, "legacy-roundtrip", new[] { foo, unstable }, GameRegion.International);
        var step4Store = new OverwatchRegionBackupStore(step4Root);
        WriteBackup(step4Store, "legacy-roundtrip", GameRegion.China, "foo.dat", "AAA");
        WriteBackup(step4Store, "legacy-roundtrip", GameRegion.International, "foo.dat", "BBB");
        WriteBackup(step4Store, "legacy-roundtrip", GameRegion.China, "unstable.dat", "A stable");
        WriteBackup(step4Store, "legacy-roundtrip", GameRegion.International, "unstable.dat", "B1 stable");
        var legacyPair = step4Store.GenerationFile("legacy-roundtrip");
        File.WriteAllLines(legacyPair, File.ReadAllLines(legacyPair)
            .Where(line => !line.Contains("\"VerificationLevel\"", StringComparison.Ordinal)));
        var step4Manager = new OverwatchRegionManager(step4Root, () => false, 0);
        var legacyStatus = await step4Manager.GetStatusAsync(step4Game, verifyFiles: false);
        Assert(legacyStatus.State == RegionBackupState.Ready && legacyStatus.Step4Pending,
            "legacy three-step Ready defaults to RoundTrip and Step4Pending");
        var pointerBefore = File.ReadAllText(step4Store.ActiveGenerationFile);
        using (var cancelled = new CancellationTokenSource())
        {
            cancelled.Cancel();
            try { await step4Manager.VerifyFourthStepAsync(step4Game, cancellationToken: cancelled.Token); }
            catch (OperationCanceledException) { }
        }
        Assert(File.ReadAllText(step4Store.ActiveGenerationFile) == pointerBefore,
            "cancelled Step4 leaves old Active unchanged");
        var step4 = await step4Manager.VerifyFourthStepAsync(step4Game);
        var upgraded = step4Store.LoadGeneration(step4.GenerationId)!;
        Assert(step4.DoubleVerified == 1 && step4.Rejected == 1 && step4.Unverified == 0 &&
               upgraded.State == RegionBackupState.Ready &&
               upgraded.VerificationLevel == RegionVerificationLevel.DoubleRoundTrip &&
               upgraded.Differences.Count == 1 && upgraded.Differences[0].RelativePath == "foo.dat" &&
               step4Store.LoadGeneration("legacy-roundtrip")?.State == RegionBackupState.Ready,
            "legacy Step4 derives Ready G2, retains B2=B1, rejects B2!=B1, and preserves G1");
        report.AppendLine("CORE TEST 1 legacy Ready -> Step4: PASS");

        // Test 2: checking is read-only and cleanup only deletes a high-confidence temporary candidate.
        var checkGame = Path.Combine(workspace, "check-game");
        var checkRoot = Path.Combine(workspace, "check-store");
        Directory.CreateDirectory(checkGame);
        File.WriteAllText(Path.Combine(checkGame, "Overwatch.exe"), "stable executable");
        CreateLargeFile(Path.Combine(checkGame, "runtime.tmp"), 5L * 1024 * 1024, 0x54);
        var permanent = new RegionFileEntry
        {
            RelativePath = "permanent.dat", Size = 10L * 1024 * 1024,
            Sha256 = new string('A', 64),
        };
        var reference = new OverwatchRegionManifest { Region = GameRegion.China };
        reference.Files[permanent.RelativePath] = permanent;
        SeedGeneration(checkRoot, "check", Array.Empty<RegionDifference>(), GameRegion.China,
            chinaReference: reference);
        var checkManager = new OverwatchRegionManager(checkRoot, () => false, 0);
        var check = await checkManager.CheckCurrentRegionFilesAsync(checkGame, GameRegion.China);
        Assert(check.MissingCount == 1 && check.MissingBytes == 10L * 1024 * 1024 &&
               check.TemporaryCount == 1 && check.TemporaryBytes == 5L * 1024 * 1024 &&
               File.Exists(Path.Combine(checkGame, "runtime.tmp")) &&
               !File.Exists(Path.Combine(checkGame, "permanent.dat")),
            "status check reports missing permanent size and temporary candidate without modifying files");
        var cleanup = await checkManager.ClearTemporaryFilesAsync(checkGame, check);
        Assert(cleanup.Deleted == 1 && cleanup.DeletedBytes == 5L * 1024 * 1024 &&
               !File.Exists(Path.Combine(checkGame, "runtime.tmp")) &&
               !File.Exists(Path.Combine(checkGame, "permanent.dat")),
            "explicit cleanup deletes only runtime.tmp and never touches permanent.dat");
        report.AppendLine("CORE TEST 2 status check + temporary cleanup: PASS");

        // Test 3: reset updates only the current side and a degraded current entry does not block the other side.
        var resetGame = Path.Combine(workspace, "reset-game");
        var resetRoot = Path.Combine(workspace, "reset-store");
        Directory.CreateDirectory(resetGame);
        File.WriteAllText(Path.Combine(resetGame, "Overwatch.exe"), "stable executable");
        File.WriteAllText(Path.Combine(resetGame, "foo.dat"), "CCC");
        var resetFoo = new RegionDifference
        {
            RelativePath = "foo.dat", Kind = RegionDifferenceKind.Different,
            China = Entry("foo.dat", "AAA"), International = Entry("foo.dat", "BBB"),
        };
        var resetMissing = new RegionDifference
        {
            RelativePath = "missing.dat", Kind = RegionDifferenceKind.Different,
            China = Entry("missing.dat", "China missing"), International = Entry("missing.dat", "International kept"),
        };
        SeedGeneration(resetRoot, "reset-old", new[] { resetFoo, resetMissing }, GameRegion.China);
        var resetStore = new OverwatchRegionBackupStore(resetRoot);
        WriteBackup(resetStore, "reset-old", GameRegion.China, "foo.dat", "AAA");
        WriteBackup(resetStore, "reset-old", GameRegion.International, "foo.dat", "BBB");
        WriteBackup(resetStore, "reset-old", GameRegion.China, "missing.dat", "China missing");
        WriteBackup(resetStore, "reset-old", GameRegion.International, "missing.dat", "International kept");
        var resetManager = new OverwatchRegionManager(resetRoot, () => false, 0);
        var reset = await resetManager.ResetCurrentRegionStateAsync(resetGame);
        var resetGeneration = resetStore.LoadGeneration(reset.GenerationId)!;
        var resetFooAfter = resetGeneration.Differences.Single(item => item.RelativePath == "foo.dat");
        var resetMissingAfter = resetGeneration.Differences.Single(item => item.RelativePath == "missing.dat");
        Assert(resetFooAfter.China?.Sha256 == Entry("foo.dat", "CCC").Sha256 &&
               resetFooAfter.International?.Sha256 == Entry("foo.dat", "BBB").Sha256 &&
               resetMissingAfter.ChinaAvailable == false && resetMissingAfter.InternationalAvailable != false,
            "reset updates China, keeps International metadata, and directionally degrades missing China file");
        var toInternational = await resetManager.NormalizeToRegionAsync(resetGame, GameRegion.International);
        Assert(toInternational.Outcome == RegionSwitchOutcome.Success &&
               File.ReadAllText(Path.Combine(resetGame, "foo.dat")) == "BBB" &&
               File.ReadAllText(Path.Combine(resetGame, "missing.dat")) == "International kept",
            "degraded current side still permits direct restore to intact other side");
        report.AppendLine("CORE TEST 3 reset current side + switch other side: PASS");
    }

    private static async Task RunBestEffortRegionTest(string workspace, StringBuilder report)
    {
        var game = Path.Combine(workspace, "best-effort-game");
        var store = Path.Combine(workspace, "best-effort-store");
        Directory.CreateDirectory(Path.Combine(game, "_retail_"));
        Directory.CreateDirectory(Path.Combine(game, "data", "casc", "data"));
        File.WriteAllText(Path.Combine(game, "Overwatch.exe"), "stable executable");
        File.WriteAllText(Path.Combine(game, "_retail_", "Overwatch_loader.dll"), "stable common loader");
        File.WriteAllText(Path.Combine(game, ".build.info"), "China metadata");
        File.WriteAllText(Path.Combine(game, "same-untracked.txt"), "same and not a Difference");
        File.WriteAllText(Path.Combine(game, "same-modified.txt"), "same before runtime");
        File.WriteAllText(Path.Combine(game, "china-only.txt"), "china only");
        File.WriteAllText(Path.Combine(game, "different-1.txt"), "China one");
        File.WriteAllText(Path.Combine(game, "different-2.txt"), "China two");
        File.WriteAllText(Path.Combine(game, "different-3.txt"), "China three");
        File.WriteAllText(Path.Combine(game, "data", "casc", "data", "data.001"), "China CASC data");

        var manager = new OverwatchRegionManager(store, () => false, 0);
        Assert(await manager.StartPreparationAsync(game, GameRegion.China) == RegionBackupState.Preparing,
            "BestEffort fixture source staging is complete");

        File.Delete(Path.Combine(game, "china-only.txt"));
        File.WriteAllText(Path.Combine(game, "international-only-1.txt"), "international one");
        File.WriteAllText(Path.Combine(game, "international-only-2.txt"), "international two");
        File.WriteAllText(Path.Combine(game, "different-1.txt"), "International one");
        File.WriteAllText(Path.Combine(game, "different-2.txt"), "International two");
        File.WriteAllText(Path.Combine(game, "different-3.txt"), "International three");
        File.WriteAllText(Path.Combine(game, "data", "casc", "data", "data.001"), "International CASC data");
        File.WriteAllText(Path.Combine(game, ".build.info"), "International metadata");
        WriteRuntimeFiles(game);

        // A normal second-stage failure must retain the fully copied source staging for retry.
        try
        {
            await using var changing = new FileStream(Path.Combine(game, "different-1.txt"), FileMode.Open,
                FileAccess.ReadWrite, FileShare.None);
            await manager.ContinuePreparationAsync(game);
            throw new InvalidOperationException("locked target file should fail the second-stage scan");
        }
        catch (IOException) { }
        Assert(Directory.EnumerateFiles(Path.Combine(store, "staging"), "pending.json", SearchOption.AllDirectories).Any() &&
               Directory.EnumerateFiles(Path.Combine(store, "staging"), "china-manifest.json", SearchOption.AllDirectories).Any(),
            "ordinary second-stage failure preserves source staging and pending metadata");
        Assert(await manager.ContinuePreparationAsync(game) == RegionBackupState.Ready,
            "second-stage retry reuses source staging and completes");

        var status = await manager.GetStatusAsync(game);
        var generationRoot = Path.Combine(store, "generations", status.ActiveGenerationId!);
        var generation = OverwatchRegionBackupStore.ReadJson<OverwatchRegionGeneration>(
            Path.Combine(generationRoot, "pair.json"))!;
        Assert(generation.Differences.Any(item => item.RelativePath == "data/casc/data/data.001"),
            "real CASC data remains a known region Difference");
        Assert(!generation.Differences.Any(item => OverwatchRegionScanner.IsIgnoredRelativePath(item.RelativePath)),
            "runtime files created after playing are absent from long-lived Differences");

        var unknownFiles = Enumerable.Range(1, 10)
            .Select(index => Path.Combine(game, "unknown", $"extra-{index}.dat")).ToList();
        foreach (var path in unknownFiles)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, "runtime extra " + Path.GetFileName(path));
        }
        File.Delete(Path.Combine(game, "same-untracked.txt"));
        File.WriteAllText(Path.Combine(game, "same-modified.txt"), "runtime modified but not a Difference");
        File.Delete(Path.Combine(game, "international-only-1.txt"));
        File.Delete(Path.Combine(game, "different-1.txt"));
        File.Delete(Path.Combine(game, "different-2.txt"));

        await using (var unreadableCore = new FileStream(Path.Combine(game, "_retail_", "Overwatch_loader.dll"),
                         FileMode.Open, FileAccess.Read, FileShare.None))
        {
            var unknownStatus = await manager.GetStatusAsync(game);
            Assert(unknownStatus.GenerationCompatibility == GenerationCompatibility.Unknown &&
                   unknownStatus.SwitchEligibility == RegionSwitchEligibility.BestEffort &&
                   unknownStatus.CurrentRegion == CurrentGameRegion.Unknown,
                "unconfirmable current version is distinct from complete, usable backups");

            var international = await manager.NormalizeToRegionAsync(game, GameRegion.International);
            Assert(international.Verified && international.Eligibility == RegionSwitchEligibility.BestEffort,
                "Unknown compatibility normalizes directly to International in BestEffort mode");
            Assert(File.Exists(Path.Combine(game, "international-only-1.txt")) &&
                   File.ReadAllText(Path.Combine(game, "different-1.txt")) == "International one" &&
                   File.ReadAllText(Path.Combine(game, "different-2.txt")) == "International two",
                "missing known target files are restored from the target backup");
            Assert(unknownFiles.All(File.Exists) && !File.Exists(Path.Combine(game, "same-untracked.txt")) &&
                   File.ReadAllText(Path.Combine(game, "same-modified.txt")) == "runtime modified but not a Difference",
                "extra, missing, and modified files outside generation.Differences remain untouched");

            var china = await manager.NormalizeToRegionAsync(game, GameRegion.China);
            Assert(china.Verified && china.Eligibility == RegionSwitchEligibility.BestEffort &&
                   File.Exists(Path.Combine(game, "china-only.txt")) &&
                   !File.Exists(Path.Combine(game, "international-only-1.txt")),
                "Unknown current region normalizes directly to China using only known Differences");

            var accountCalls = new List<string>();
            await AccountSwitchPipeline.ExecuteAsync(
                () => { accountCalls.Add("Quit BattleNet"); return Task.CompletedTask; },
                async () =>
                {
                    accountCalls.Add("Normalize Game -> International");
                    var result = await manager.NormalizeToRegionAsync(game, GameRegion.International);
                    Assert(result.Eligibility == RegionSwitchEligibility.BestEffort && result.Verified,
                        "account-linked Unknown normalize verifies known International Differences");
                },
                () => { accountCalls.Add("Restore Target Account"); return Task.CompletedTask; },
                () => { accountCalls.Add("Launch BattleNet"); return Task.CompletedTask; });
            Assert(accountCalls.SequenceEqual(new[] { "Quit BattleNet", "Normalize Game -> International",
                    "Restore Target Account", "Launch BattleNet" }),
                "account-linked BestEffort normalize happens before account restore and Battle.net launch");
        }

        var corruptBackup = Path.Combine(generationRoot, "backups", "international", "different-1.txt");
        File.WriteAllText(corruptBackup, "xxxxxxxxxxxxxxxxx");
        Assert(new FileInfo(corruptBackup).Length == new FileInfo(Path.Combine(game, "different-1.txt")).Length,
            "corrupt target backup fixture preserves size so rejection depends on Hash");
        var beforeRejected = File.ReadAllText(Path.Combine(game, "different-1.txt"));
        try
        {
            await manager.NormalizeToRegionAsync(game, GameRegion.International);
            throw new InvalidOperationException("corrupt known target backup should reject normalize");
        }
        catch (InvalidDataException) { }
        Assert(File.ReadAllText(Path.Combine(game, "different-1.txt")) == beforeRejected,
            "corrupt target backup rejects before any known live file is changed");

        File.WriteAllText(Path.Combine(game, "_retail_", "Overwatch_loader.dll"), "updated common loader");
        var updated = await manager.GetStatusAsync(game);
        Assert(updated.GenerationCompatibility == GenerationCompatibility.Updated &&
               updated.SwitchEligibility == RegionSwitchEligibility.BestEffort,
            "updated FullSnapshot remains visible as BestEffort until strict target hash validation");

        var log = File.ReadAllText(RegionSwitchLog.FileOverride!);
        Assert(log.Contains("GenerationCompatibility=Unknown", StringComparison.Ordinal) &&
               log.Contains("SwitchMode=BestEffort", StringComparison.Ordinal) &&
               log.Contains("IgnoredUnknownFiles=未枚举，未参与处理", StringComparison.Ordinal) &&
               log.Contains("Verification=passed", StringComparison.Ordinal),
            "BestEffort logs record Unknown reason, mode, known-only handling, and verification");
        report.AppendLine("TEST 7 BestEffort/volatile/staging/account: PASS (A-H: unknown-file preservation, known-file repair, strict FullSnapshot backup hash, Unknown direct normalize, account order, volatile filtering, source staging reuse)");
    }

    private static async Task RunAccountSwitchOrderTest(StringBuilder report)
    {
        var calls = new List<string>();
        static Task Done() => Task.CompletedTask;
        await AccountSwitchPipeline.ExecuteAsync(
            () => { calls.Add("Quit BattleNet"); return Done(); },
            () => { calls.Add("Normalize Game -> International"); return Done(); },
            () => { calls.Add("Restore Target Account"); return Done(); },
            () => { calls.Add("Launch BattleNet"); return Done(); });
        Assert(string.Join(" > ", calls) ==
               "Quit BattleNet > Normalize Game -> International > Restore Target Account > Launch BattleNet",
            "account switch ordering has no backup write step");

        calls.Clear();
        try
        {
            await AccountSwitchPipeline.ExecuteAsync(
                () => { calls.Add("Quit BattleNet"); return Done(); },
                () => { calls.Add("Normalize Failed"); throw new IOException("simulated failure"); },
                () => { calls.Add("Restore Target Account"); return Done(); },
                () => { calls.Add("Launch BattleNet"); return Done(); });
        }
        catch (IOException) { }
        Assert(!calls.Contains("Restore Target Account") && !calls.Contains("Launch BattleNet"),
            "normalize failure prevents target restore and Battle.net launch");
        report.AppendLine("TEST 8 account switch pipeline: PASS (strict read-only-backup order/fail-fast/no launch after region failure)");
    }

    private static void RunAccountPreferenceTest(string workspace, StringBuilder report)
    {
        var settingsPath = Path.Combine(workspace, "settings.json");
        var settings = new AppSettings { RegionStoragePath = @"D:\Region Data", SkippedUpdateVersion = "1.0.1" };
        var preference = settings.PreferenceFor(123456);
        preference.CustomName = "主号";
        preference.Remark = "常用账号";
        preference.Region = AccountRegionOverride.China;
        settings.SaveTo(settingsPath);
        var loaded = AppSettings.LoadFrom(settingsPath);
        Assert(loaded.RegionStoragePath == @"D:\Region Data", "custom region storage persisted");
        Assert(loaded.SkippedUpdateVersion == "1.0.1", "skipped update version persists in settings.json");
        Assert(loaded.PreferenceFor(123456).Region == AccountRegionOverride.China, "account preference persisted");
        var legacyNavigationPath = Path.Combine(workspace, "legacy-navigation-settings.json");
        File.WriteAllText(legacyNavigationPath, "{\"LastMainSection\":\"overview\"}");
        var legacyNavigation = AppSettings.LoadFrom(legacyNavigationPath);
        Assert(legacyNavigation.LastMainSection == "accounts" &&
               AppSettings.NormalizeMainSection("not-a-page") == "accounts",
            "removed overview and unknown saved sections safely fall back to accounts");
        var current = new AccountRow { AccountId = 123456, BattleTag = "CloudLight#1234", IsActive = true, HasProfile = true };
        Assert(MainViewModel.SelectSavedAccounts(new[] { current }).Single() == current, "active saved account remains listed");
        report.AppendLine("TEST 9 settings/account list: PASS");
    }

    private static void RunAppPathsMigrationTest(string workspace, StringBuilder report)
    {
        var oldRoot = Path.Combine(workspace, "legacy-app");
        var newRoot = Path.Combine(workspace, "documents-app");
        Directory.CreateDirectory(Path.Combine(oldRoot, "accounts", "42"));
        Directory.CreateDirectory(Path.Combine(oldRoot, "logs"));
        Directory.CreateDirectory(Path.Combine(oldRoot, "region-switch", "generations", "g1"));
        File.WriteAllText(Path.Combine(oldRoot, "settings.json"), "{\"DarkMode\":true}");
        File.WriteAllText(Path.Combine(oldRoot, "accounts", "42", "meta.json"), "{}");
        File.WriteAllText(Path.Combine(oldRoot, "logs", "account-switch.log"), "ok");
        File.WriteAllText(Path.Combine(oldRoot, "region-switch", "active-generation.json"), "{}");
        File.WriteAllText(Path.Combine(oldRoot, "region-switch", "generations", "g1", "pair.json"), "{}");

        var paths = new AppPaths(newRoot, oldRoot);
        var result = paths.MigrateLegacyData();
        Assert(File.Exists(paths.SettingsFile), "settings migrated");
        Assert(File.Exists(Path.Combine(paths.AccountsDir, "42", "meta.json")), "accounts migrated");
        Assert(File.Exists(Path.Combine(paths.LogsDir, "account-switch.log")), "logs migrated");
        Assert(result.DefaultRegionMoved && File.Exists(Path.Combine(paths.DefaultRegionStorageDir, "active-generation.json")),
            "default region store moved without copy");

        var customOld = Path.Combine(workspace, "legacy-custom");
        var customNew = Path.Combine(workspace, "documents-custom");
        var externalRegion = Path.Combine(workspace, "external-region");
        Directory.CreateDirectory(Path.Combine(customOld, "region-switch"));
        Directory.CreateDirectory(externalRegion);
        File.WriteAllText(Path.Combine(customOld, "settings.json"),
            System.Text.Json.JsonSerializer.Serialize(new AppSettings { RegionStoragePath = externalRegion }));
        File.WriteAllText(Path.Combine(customOld, "region-switch", "sentinel.bin"), "legacy-default");
        File.WriteAllText(Path.Combine(externalRegion, "custom.bin"), "do-not-move");
        var customResult = new AppPaths(customNew, customOld).MigrateLegacyData();
        Assert(!customResult.DefaultRegionMoved, "custom region setting prevents default region move");
        Assert(File.Exists(Path.Combine(externalRegion, "custom.bin")), "custom region store remains in place");
        Assert(File.Exists(Path.Combine(customOld, "region-switch", "sentinel.bin")), "legacy default remains untouched when custom path is set");
        report.AppendLine("TEST 10 app paths migration: PASS (settings/accounts/logs/default move/custom preserved)");
    }

    private static async Task RunProductizationTests(string workspace, StringBuilder report)
    {
        await RunUpdaterResilienceTests(workspace, report);
        await RunSnapshotAndSwitchPlanTests(workspace, report);
        await RunDiagnosticsAndNotificationTests(workspace, report);
    }

    private static async Task RunUpdaterResilienceTests(string workspace, StringBuilder report)
    {
        var bytes = new byte[4096];
        bytes[0] = (byte)'M';
        bytes[1] = (byte)'Z';
        for (var i = 2; i < bytes.Length; i++) bytes[i] = (byte)(i % 251);

        var retryHandler = new ScriptedDownloadHandler((call, _) => call < 3
            ? new HttpResponseMessage(HttpStatusCode.ServiceUnavailable)
            : FullDownloadResponse(bytes));
        var retryStates = new List<UpdaterState>();
        var retryResult = CreateDownloadResult("2.0.91", bytes);
        using (var clients = CreateDownloadClients(workspace, retryHandler))
        {
            var downloader = new UpdateDownloadService(clients, [
                TimeSpan.FromMilliseconds(1), TimeSpan.FromMilliseconds(1), TimeSpan.FromMilliseconds(1)]);
            downloader.StateChanged += retryStates.Add;
            var path = await downloader.DownloadInstallerAsync(retryResult);
            Assert(File.Exists(path) && retryHandler.Calls == 3 && retryStates.Contains(UpdaterState.WaitingRetry),
                "updater retries temporary HTTP 503 failures and exposes WaitingRetry");
            Assert(downloader.State == UpdaterState.ReadyToInstall, "updater reaches ReadyToInstall after digest verification");
            TryDeleteDirectory(Path.GetDirectoryName(path)!);
        }

        var exhaustedHandler = new ScriptedDownloadHandler((_, _) =>
            new HttpResponseMessage(HttpStatusCode.BadGateway));
        var exhaustedResult = CreateDownloadResult("2.0.92", bytes);
        using (var clients = CreateDownloadClients(workspace, exhaustedHandler))
        {
            var downloader = new UpdateDownloadService(clients, [
                TimeSpan.FromMilliseconds(1), TimeSpan.FromMilliseconds(1), TimeSpan.FromMilliseconds(1)]);
            try
            {
                await downloader.DownloadInstallerAsync(exhaustedResult);
                throw new InvalidOperationException("retry exhaustion unexpectedly succeeded");
            }
            catch (HttpRequestException) { }
            Assert(exhaustedHandler.Calls == 4 && downloader.State == UpdaterState.Failed,
                "updater stops after three retries and reports Failed");
            TryDeleteDirectory(Path.Combine(Path.GetTempPath(), "CloudLight Blizzard", "updates", "2.0.92"));
        }

        var notFoundHandler = new ScriptedDownloadHandler((_, _) =>
            new HttpResponseMessage(HttpStatusCode.NotFound));
        var notFoundResult = CreateDownloadResult("2.0.93", bytes);
        using (var clients = CreateDownloadClients(workspace, notFoundHandler))
        {
            var downloader = new UpdateDownloadService(clients, [
                TimeSpan.FromMilliseconds(1), TimeSpan.FromMilliseconds(1), TimeSpan.FromMilliseconds(1)]);
            try { await downloader.DownloadInstallerAsync(notFoundResult); throw new InvalidOperationException("404 unexpectedly succeeded"); }
            catch (HttpRequestException) { }
            Assert(notFoundHandler.Calls == 1, "updater does not retry a permanent 404");
            TryDeleteDirectory(Path.Combine(Path.GetTempPath(), "CloudLight Blizzard", "updates", "2.0.93"));
        }

        await RunRangeResumeCase(workspace, bytes, "2.0.94", changeEtag: false, ignoreRange: false,
            expectedDescription: "updater resumes a matching partial file with Content-Range and ETag");
        await RunRangeResumeCase(workspace, bytes, "2.0.95", changeEtag: false, ignoreRange: true,
            expectedDescription: "updater restarts cleanly when the server ignores Range");
        await RunRangeResumeCase(workspace, bytes, "2.0.96", changeEtag: true, ignoreRange: false,
            expectedDescription: "updater restarts cleanly when the ETag changes");
        await RunRangeResumeCase(workspace, bytes, "2.0.961", changeEtag: false, ignoreRange: false,
            expectedDescription: "updater restarts cleanly when Last-Modified changes", changeLastModified: true);
        await RunRangeResumeCase(workspace, bytes, "2.0.962", changeEtag: false, ignoreRange: false,
            expectedDescription: "updater restarts cleanly after HTTP 416", responseMode: RangeResponseMode.NotSatisfiable);
        await RunRangeResumeCase(workspace, bytes, "2.0.963", changeEtag: false, ignoreRange: false,
            expectedDescription: "updater restarts after a wrong Content-Range start", responseMode: RangeResponseMode.WrongStart);
        await RunRangeResumeCase(workspace, bytes, "2.0.964", changeEtag: false, ignoreRange: false,
            expectedDescription: "updater restarts after a Content-Length mismatch", responseMode: RangeResponseMode.WrongLength);

        await RunCompletedPartialCase(workspace, bytes);
        await RunOversizedPartialCase(workspace, bytes);
        await RunZeroPartialCase(workspace, bytes);

        Assert(UpdateService.IsValidSha256Digest("sha256:" + new string('a', 64)) &&
               UpdateService.IsValidSha256Digest("SHA256:" + new string('A', 64)) &&
               !UpdateService.IsValidSha256Digest(null) &&
               !UpdateService.IsValidSha256Digest("sha256:" + new string('g', 64)) &&
               !UpdateService.IsValidSha256Digest("sha256:" + new string('a', 63)),
            "updater accepts uppercase/lowercase valid SHA-256 and rejects missing, malformed, and wrong-length digests");

        var mismatchResult = CreateDownloadResult("2.0.97", bytes);
        var mismatchRoot = Path.Combine(Path.GetTempPath(), "CloudLight Blizzard", "updates", "2.0.97");
        TryDeleteDirectory(mismatchRoot);
        Directory.CreateDirectory(mismatchRoot);
        var mismatchPartial = Path.Combine(mismatchRoot, mismatchResult.InstallerName! + ".partial");
        File.WriteAllBytes(mismatchPartial, bytes[..100]);
        File.WriteAllText(Path.Combine(mismatchRoot, "update-download.json"), JsonSerializer.Serialize(new
        {
            Version = mismatchResult.LatestVersion,
            DownloadUrl = mismatchResult.InstallerDownloadUrl,
            ExpectedSize = mismatchResult.InstallerSize,
            Digest = mismatchResult.InstallerDigest,
            DownloadedBytes = 99,
            ETag = "\"old\"",
        }));
        var mismatchHandler = new ScriptedDownloadHandler((_, _) => FullDownloadResponse(bytes));
        using (var clients = CreateDownloadClients(workspace, mismatchHandler))
        {
            var path = await new UpdateDownloadService(clients).DownloadInstallerAsync(mismatchResult);
            Assert(mismatchHandler.Ranges.Single() is null && File.ReadAllBytes(path).AsSpan().SequenceEqual(bytes),
                "updater discards partial metadata mismatches instead of appending another asset");
            TryDeleteDirectory(Path.GetDirectoryName(path)!);
        }

        var badDigestResult = CreateDownloadResult("2.0.98", bytes, "sha256:" + new string('1', 64));
        var badDigestHandler = new ScriptedDownloadHandler((_, _) => FullDownloadResponse(bytes));
        using (var clients = CreateDownloadClients(workspace, badDigestHandler))
        {
            var downloader = new UpdateDownloadService(clients);
            try
            {
                await downloader.DownloadInstallerAsync(badDigestResult);
                throw new InvalidOperationException("digest mismatch unexpectedly succeeded");
            }
            catch (InvalidDataException) { }
            Assert(downloader.State == UpdaterState.Failed, "updater rejects a SHA-256 mismatch");
            TryDeleteDirectory(Path.Combine(Path.GetTempPath(), "CloudLight Blizzard", "updates", "2.0.98"));
        }

        report.AppendLine("TEST 11 updater resilience: PASS (retry/exhaustion/404/Range/ignored Range/ETag/metadata mismatch/digest)");
    }

    private enum RangeResponseMode { Normal, NotSatisfiable, WrongStart, WrongLength }

    private static async Task RunRangeResumeCase(string workspace, byte[] bytes, string version,
        bool changeEtag, bool ignoreRange, string expectedDescription,
        bool changeLastModified = false, RangeResponseMode responseMode = RangeResponseMode.Normal)
    {
        var result = CreateDownloadResult(version, bytes);
        var root = Path.Combine(Path.GetTempPath(), "CloudLight Blizzard", "updates", version);
        TryDeleteDirectory(root);
        Directory.CreateDirectory(root);
        var prefixLength = 700;
        File.WriteAllBytes(Path.Combine(root, result.InstallerName! + ".partial"), bytes[..prefixLength]);
        File.WriteAllText(Path.Combine(root, "update-download.json"), JsonSerializer.Serialize(new
        {
            Version = result.LatestVersion,
            DownloadUrl = result.InstallerDownloadUrl,
            ExpectedSize = result.InstallerSize,
            Digest = result.InstallerDigest,
            DownloadedBytes = prefixLength,
            ETag = "\"old\"",
            LastModified = "Mon, 01 Jan 2024 00:00:00 GMT",
        }));
        var handler = new ScriptedDownloadHandler((call, request) =>
        {
            if (call == 1 && !ignoreRange)
            {
                if (responseMode == RangeResponseMode.NotSatisfiable)
                    return new HttpResponseMessage(HttpStatusCode.RequestedRangeNotSatisfiable);
                var etag = changeEtag ? "\"new\"" : "\"old\"";
                var from = responseMode == RangeResponseMode.WrongStart ? prefixLength + 1 : prefixLength;
                var response = new HttpResponseMessage(HttpStatusCode.PartialContent)
                {
                    Content = new ByteArrayContent(bytes[prefixLength..]),
                };
                response.Content.Headers.ContentRange = new ContentRangeHeaderValue(from,
                    bytes.Length - 1, bytes.Length);
                if (responseMode == RangeResponseMode.WrongLength)
                    response.Content.Headers.ContentLength = bytes.Length - prefixLength - 1;
                response.Headers.ETag = new EntityTagHeaderValue(etag);
                response.Content.Headers.LastModified = DateTimeOffset.Parse(
                    changeLastModified ? "Tue, 02 Jan 2024 00:00:00 GMT" : "Mon, 01 Jan 2024 00:00:00 GMT");
                return response;
            }
            return FullDownloadResponse(bytes);
        });
        using (var clients = CreateDownloadClients(workspace, handler))
        {
            var path = await new UpdateDownloadService(clients).DownloadInstallerAsync(result);
            Assert(File.ReadAllBytes(path).AsSpan().SequenceEqual(bytes), expectedDescription + " (content)");
            var expectedCalls = !ignoreRange && !changeEtag && !changeLastModified &&
                                responseMode == RangeResponseMode.Normal ? 1 : 2;
            Assert(handler.Calls == expectedCalls && handler.Ranges[0] == $"bytes={prefixLength}-" &&
                   (expectedCalls == 1 || handler.Ranges[1] is null), expectedDescription + " (request validation)");
            TryDeleteDirectory(Path.GetDirectoryName(path)!);
        }
    }

    private static async Task RunCompletedPartialCase(string workspace, byte[] bytes)
    {
        const string version = "2.0.965";
        var result = CreateDownloadResult(version, bytes);
        var root = Path.Combine(Path.GetTempPath(), "CloudLight Blizzard", "updates", version);
        TryDeleteDirectory(root);
        Directory.CreateDirectory(root);
        var partial = Path.Combine(root, result.InstallerName! + ".partial");
        File.WriteAllBytes(partial, bytes);
        File.WriteAllText(Path.Combine(root, "update-download.json"), JsonSerializer.Serialize(new
        {
            Version = result.LatestVersion, DownloadUrl = result.InstallerDownloadUrl,
            ExpectedSize = result.InstallerSize, Digest = result.InstallerDigest,
            DownloadedBytes = bytes.Length, ETag = "\"old\"", LastModified = "",
        }));
        var handler = new ScriptedDownloadHandler((_, _) =>
            throw new InvalidOperationException("a complete partial must not issue a Range request"));
        using var clients = CreateDownloadClients(workspace, handler);
        var path = await new UpdateDownloadService(clients).DownloadInstallerAsync(result);
        Assert(handler.Calls == 0 && File.ReadAllBytes(path).AsSpan().SequenceEqual(bytes),
            "updater verifies and promotes a partial whose size already equals the expected size");
        TryDeleteDirectory(root);
    }

    private static async Task RunOversizedPartialCase(string workspace, byte[] bytes)
    {
        const string version = "2.0.966";
        var result = CreateDownloadResult(version, bytes);
        var root = Path.Combine(Path.GetTempPath(), "CloudLight Blizzard", "updates", version);
        TryDeleteDirectory(root);
        Directory.CreateDirectory(root);
        File.WriteAllBytes(Path.Combine(root, result.InstallerName! + ".partial"), bytes.Concat(new byte[3]).ToArray());
        File.WriteAllText(Path.Combine(root, "update-download.json"), JsonSerializer.Serialize(new
        {
            Version = result.LatestVersion, DownloadUrl = result.InstallerDownloadUrl,
            ExpectedSize = result.InstallerSize, Digest = result.InstallerDigest,
            DownloadedBytes = bytes.Length + 3, ETag = "\"old\"", LastModified = "",
        }));
        var handler = new ScriptedDownloadHandler((_, request) =>
        {
            if (request.Headers.Range is not null) throw new InvalidOperationException("oversized partial must restart without Range");
            return FullDownloadResponse(bytes);
        });
        using var clients = CreateDownloadClients(workspace, handler);
        var path = await new UpdateDownloadService(clients).DownloadInstallerAsync(result);
        Assert(handler.Calls == 1 && File.ReadAllBytes(path).AsSpan().SequenceEqual(bytes),
            "updater discards a partial larger than the expected size before downloading");
        TryDeleteDirectory(root);
    }

    private static async Task RunZeroPartialCase(string workspace, byte[] bytes)
    {
        const string version = "2.0.967";
        var result = CreateDownloadResult(version, bytes);
        var root = Path.Combine(Path.GetTempPath(), "CloudLight Blizzard", "updates", version);
        TryDeleteDirectory(root);
        Directory.CreateDirectory(root);
        File.WriteAllBytes(Path.Combine(root, result.InstallerName! + ".partial"), Array.Empty<byte>());
        File.WriteAllText(Path.Combine(root, "update-download.json"), JsonSerializer.Serialize(new
        {
            Version = "2.0.966", DownloadUrl = "https://old.example.invalid/installer.exe",
            ExpectedSize = bytes.Length, Digest = "sha256:" + new string('0', 64),
            DownloadedBytes = 0, ETag = "\"old\"", LastModified = "",
        }));
        var handler = new ScriptedDownloadHandler((_, request) =>
        {
            if (request.Headers.Range is not null)
                throw new InvalidOperationException("zero-byte partial must restart without Range");
            return FullDownloadResponse(bytes);
        });
        using var clients = CreateDownloadClients(workspace, handler);
        var path = await new UpdateDownloadService(clients).DownloadInstallerAsync(result);
        Assert(handler.Calls == 1 && File.ReadAllBytes(path).AsSpan().SequenceEqual(bytes),
            "updater discards zero-byte partial metadata and starts a clean full download");
        TryDeleteDirectory(root);
    }

    private static async Task RunSnapshotAndSwitchPlanTests(string workspace, StringBuilder report)
    {
        var game = Path.Combine(workspace, "productization-plan-game");
        var storeRoot = Path.Combine(workspace, "productization-plan-store");
        Directory.CreateDirectory(game);
        File.WriteAllText(Path.Combine(game, "Overwatch.exe"), "stable executable");
        File.WriteAllText(Path.Combine(game, "region.dat"), "CN");
        File.WriteAllText(Path.Combine(game, ".build.info"), "build-1");
        var manager = new OverwatchRegionManager(storeRoot, () => false, 0);
        Assert(await manager.StartPreparationAsync(game, GameRegion.China) == RegionBackupState.Preparing,
            "productization fixture starts source capture");
        File.WriteAllText(Path.Combine(game, "region.dat"), "INT");
        File.WriteAllText(Path.Combine(game, ".build.info"), "build-2");
        Assert(await manager.ContinuePreparationAsync(game) == RegionBackupState.Ready,
            "productization fixture completes a two-region snapshot");

        var status = await manager.GetStatusAsync(game, verifyFiles: false);
        Assert(status.ActiveGenerationId is { Length: > 0 }, "snapshot fixture has an active generation");
        var generationId = status.ActiveGenerationId!;
        var pointerBefore = File.ReadAllText(Path.Combine(storeRoot, "active-generation.json"));
        var gameBefore = Directory.EnumerateFiles(game, "*", SearchOption.AllDirectories).ToDictionary(
            path => Path.GetRelativePath(game, path), File.ReadAllBytes, StringComparer.OrdinalIgnoreCase);
        var plan = await manager.CreateSwitchPlanAsync(game, GameRegion.China);
        Assert(plan.CanExecute && plan.FilesToRestore.Any(item => item.RelativePath == "region.dat"),
            "switch preview contains the same restore operation the executor will use");
        Assert(File.ReadAllText(Path.Combine(storeRoot, "active-generation.json")) == pointerBefore &&
               Directory.EnumerateFiles(game, "*", SearchOption.AllDirectories).ToDictionary(
                   path => Path.GetRelativePath(game, path), File.ReadAllBytes, StringComparer.OrdinalIgnoreCase)
                   .All(pair => gameBefore.TryGetValue(pair.Key, out var before) && before.AsSpan().SequenceEqual(pair.Value)),
            "switch preview is read-only and leaves game files and active pointer unchanged");
        plan.RequiredDiskSpace = long.MaxValue;
        plan.Blockers.Add("测试：磁盘空间不足");
        Assert(!plan.CanExecute, "switch preview blocks insufficient disk space instead of offering continue");
        plan.Blockers.RemoveAt(plan.Blockers.Count - 1);
        plan.RequiredDiskSpace = plan.EstimatedBytes + 256L * 1024 * 1024;

        var snapshots = new SnapshotManagerService(manager);
        var verificationFile = Path.Combine(storeRoot, "snapshot-verification.json");
        File.WriteAllText(verificationFile, "legacy snapshot verification data");
        var verificationFileBeforeList = File.ReadAllText(verificationFile);
        var listed = snapshots.List();
        Assert(listed.Count == 1 && listed[0].GenerationId == generationId && listed[0].FileCount > 0 &&
               listed[0].State == SnapshotDisplayState.Normal && string.IsNullOrEmpty(listed[0].StateReason) &&
               File.ReadAllText(verificationFile) == verificationFileBeforeList,
            "snapshot manager lists a ready generation as normal and ignores legacy verification data");
        var normalItem = new SnapshotItemViewModel(listed[0]);
        Assert(normalItem.StateText == "正常" && !normalItem.StateText.Contains("验证", StringComparison.Ordinal) &&
               typeof(SnapshotManagerService).GetMethod("VerifyAsync") is null &&
               typeof(SnapshotsViewModel).GetMethod("VerifyAsync") is null &&
               typeof(SnapshotItemViewModel).GetProperty("IsVerifying") is null &&
               typeof(SnapshotDescriptor).GetProperty("LastVerifiedAtUtc") is null,
            "snapshot UI and page services expose no manual verification workflow or unverified state");
        var snapshotStore = new OverwatchRegionBackupStore(storeRoot);
        var chinaBackup = snapshotStore.BackupFile(generationId, GameRegion.China, "region.dat");
        var originalChina = File.ReadAllBytes(chinaBackup);
        File.Delete(chinaBackup);
        var missing = snapshots.List().Single(item => item.GenerationId == generationId);
        Assert(missing.State == SnapshotDisplayState.Missing,
            "snapshot manager marks a generation with a missing backup file as missing");
        File.WriteAllBytes(chinaBackup, originalChina);
        var pairFile = Path.Combine(snapshotStore.GenerationRoot(generationId), "pair.json");
        var originalPair = File.ReadAllBytes(pairFile);
        File.Delete(pairFile);
        var missingPair = snapshots.List().Single(item => item.GenerationId == generationId);
        Assert(missingPair.State == SnapshotDisplayState.Missing,
            "snapshot manager keeps a generation with a missing pair.json visible as missing");
        File.WriteAllBytes(pairFile, originalPair);

        File.WriteAllBytes(chinaBackup, originalChina.Select(value => (byte)(value ^ 0x1)).ToArray());
        var corruptStatus = await manager.GetStatusAsync(game, verifyFiles: true, verifyBackupHashes: true);
        Assert(corruptStatus.SwitchEligibility == RegionSwitchEligibility.BackupUnavailable &&
               corruptStatus.BackupFileIssueCount > 0,
            $"region switch safety detects a corrupted backup through size/hash checks (eligibility={corruptStatus.SwitchEligibility}, issues={corruptStatus.BackupFileIssueCount})");
        File.WriteAllBytes(chinaBackup, originalChina);

        var generation = snapshotStore.LoadGeneration(generationId) ??
                         throw new InvalidDataException("snapshot fixture generation disappeared");
        generation.State = RegionBackupState.Stale;
        snapshotStore.SaveGeneration(generation);
        var expired = snapshots.List().Single(item => item.GenerationId == generationId);
        Assert(expired.State == SnapshotDisplayState.Expired,
            "snapshot manager maps a stale generation to expired");
        generation.State = RegionBackupState.Error;
        snapshotStore.SaveGeneration(generation);
        var corrupt = snapshots.List().Single(item => item.GenerationId == generationId);
        Assert(corrupt.State == SnapshotDisplayState.Corrupt,
            "snapshot manager maps an errored generation to corrupt");
        generation.State = RegionBackupState.Ready;
        snapshotStore.SaveGeneration(generation);
        var normal = snapshots.List().Single(item => item.GenerationId == generationId);
        Assert(normal.State == SnapshotDisplayState.Normal,
            "snapshot manager returns a ready, complete generation to normal");

        var activeDeleteBlocked = false;
        try { snapshots.Delete(generationId); }
        catch (InvalidOperationException) { activeDeleteBlocked = true; }
        Assert(activeDeleteBlocked, "active snapshot deletion is blocked");
        var unsafeDeleteBlocked = false;
        try { snapshots.Delete("../outside"); }
        catch (InvalidDataException) { unsafeDeleteBlocked = true; }
        Assert(unsafeDeleteBlocked, "path traversal snapshot deletion is blocked");

        foreach (var unsafePath in new[] { "....\\file", @"C:\\outside", @"\\server\\share", "..\\outside" })
        {
            var rejected = false;
            try { _ = OverwatchRegionBackupStore.SafeCombine(game, unsafePath); }
            catch (InvalidDataException) { rejected = true; }
            Assert(rejected, $"unsafe managed path is rejected: {unsafePath}");
        }
        var driveRoot = Path.GetPathRoot(Path.GetFullPath(game))!;
        var rootCandidate = OverwatchRegionBackupStore.SafeCombine(driveRoot, "CloudLight-Blizzard-root-test.txt");
        Assert(string.Equals(rootCandidate, Path.Combine(driveRoot, "CloudLight-Blizzard-root-test.txt"),
                   StringComparison.OrdinalIgnoreCase),
            "managed path normalization preserves a filesystem root drive");

        var outsideRoot = Path.Combine(workspace, "snapshot-junction-target");
        var outsideSentinel = Path.Combine(outsideRoot, "sentinel.txt");
        Directory.CreateDirectory(outsideRoot);
        File.WriteAllText(outsideSentinel, "must survive");
        var linkRoot = Path.Combine(storeRoot, "generations", "forged-junction");
        var junctionTested = false;
        try
        {
            Directory.CreateSymbolicLink(linkRoot, outsideRoot);
            junctionTested = true;
            var linkDeleteBlocked = false;
            try { snapshots.Delete("forged-junction"); }
            catch (InvalidDataException) { linkDeleteBlocked = true; }
            Assert(linkDeleteBlocked && File.Exists(outsideSentinel) && File.ReadAllText(outsideSentinel) == "must survive",
                "snapshot deletion rejects a reparse-point generation and preserves its outside target");
        }
        catch (UnauthorizedAccessException) { }
        catch (PlatformNotSupportedException) { }
        catch (IOException) { }
        Assert(!junctionTested || !listed.Any(item => item.GenerationId == "forged-junction"),
            "snapshot list never treats a reparse-point directory as a managed generation");

        var malformedRoot = Path.Combine(storeRoot, "generations", "forged-pair");
        Directory.CreateDirectory(malformedRoot);
        File.WriteAllText(Path.Combine(malformedRoot, "pair.json"), "{\"SchemaVersion\":999}");
        var malformedDeleteBlocked = false;
        try { snapshots.Delete("forged-pair"); }
        catch (InvalidOperationException) { malformedDeleteBlocked = true; }
        Assert(malformedDeleteBlocked && Directory.Exists(malformedRoot),
            "malformed or forged pair.json cannot authorize snapshot deletion");

        var emptyManager = new OverwatchRegionManager(Path.Combine(workspace, "empty-productization-store"), () => false, 0);
        var invalidPlan = await emptyManager.CreateSwitchPlanAsync(game, GameRegion.International);
        Assert(!invalidPlan.CanExecute && invalidPlan.Blockers.Count > 0,
            "switch preview blocks when no valid snapshot exists");

        var pointerBeforeStalePlan = File.ReadAllText(Path.Combine(storeRoot, "active-generation.json"));
        File.WriteAllText(Path.Combine(game, "region.dat"), "STALE");
        var stalePlanBlocked = false;
        try { await manager.ExecuteSwitchPlanAsync(game, plan); }
        catch (InvalidDataException) { stalePlanBlocked = true; }
        Assert(stalePlanBlocked && File.ReadAllText(Path.Combine(storeRoot, "active-generation.json")) == pointerBeforeStalePlan &&
               File.ReadAllText(Path.Combine(game, "region.dat")) == "STALE",
            "stale switch plan is revalidated and stops before changing files or active generation");
        File.WriteAllText(Path.Combine(game, "region.dat"), "INT");

        var executed = await manager.ExecuteSwitchPlanAsync(game, plan);
        Assert(executed.Outcome == RegionSwitchOutcome.Success && File.ReadAllText(Path.Combine(game, "region.dat")) == "CN",
            "executor applies the exact preview plan after confirmation");

        Assert(await manager.StartPreparationAsync(game, GameRegion.China) == RegionBackupState.Preparing,
            "snapshot fixture can start a second generation while retaining the first");
        File.WriteAllText(Path.Combine(game, "region.dat"), "INT");
        File.WriteAllText(Path.Combine(game, ".build.info"), "build-3");
        Assert(await manager.ContinuePreparationAsync(game) == RegionBackupState.Ready,
            "snapshot fixture can complete a second generation");
        var withHistory = snapshots.List();
        var historical = withHistory.Single(item => item.GenerationId == generationId);
        var current = withHistory.Single(item => item.IsActive);
        Assert(!historical.IsActive && current.IsActive,
            "only the newest generation is marked as current");
        Assert(snapshots.Delete(generationId) && !snapshots.List().Any(item => item.GenerationId == generationId),
            "historical snapshot can be deleted while current snapshot remains protected");

        report.AppendLine("TEST 12 snapshots and switch preview: PASS (direct state/listing, missing/corrupt/expired, legacy-file ignore, delete safety, historical deletion, read-only preview, shared plan, disk and invalid snapshot blockers)");
    }

    private static async Task RunDiagnosticsAndNotificationTests(string workspace, StringBuilder report)
    {
        const string secretDetails = "Authorization: Bearer abcdef\n" +
            "ProxyUrl=http://user:password@127.0.0.1:7897\n" +
            "token=abcdef\naccess_token=abcdef\nrefresh_token=abcdef\n" +
            "cookie=session-cookie-value\nset-cookie=session-set-cookie-value\n" +
            "SESSDATA=fake-sessdata\nbili_jct=fake-bili-csrf\n" +
            "DedeUserID=42\nDedeUserID__ckMd5=fake-ckmd5\nbuvid3=fake-buvid3\n" +
            "buvid4=fake-buvid4\nb_nut=fake-bnut\nsid=fake-sid\n" +
            "LIVE_BUVID=fake-live-buvid\ncsrf=fake-csrf\ncsrf_token=fake-csrf-token\n" +
            "GITHUB_TOKEN=abcdef\nCLOUDFLARE_API_TOKEN=abcdef\n" +
            "password=abcdef\npasswd=abcdef\nsecret=abcdef";
        var reportModel = new DiagnosticRunReport
        {
            AppVersion = "2.1.1",
            StartedAt = DateTimeOffset.Now.AddSeconds(-1),
            CompletedAt = DateTimeOffset.Now,
            Checks = [
                new DiagnosticCheck { Id = "healthy", Category = "测试", Name = "正常", Status = DiagnosticSeverity.Healthy, Summary = "正常" },
                new DiagnosticCheck { Id = "warning", Category = "测试", Name = "警告", Status = DiagnosticSeverity.Warning, Summary = "警告" },
                new DiagnosticCheck { Id = "error", Category = "测试", Name = "错误", Status = DiagnosticSeverity.Error, Summary = "错误", Details = secretDetails },
            ],
        };
        Assert(reportModel.HealthyCount == 1 && reportModel.WarningCount == 1 && reportModel.ErrorCount == 1 &&
               reportModel.OverallText == "需要处理错误", "diagnostics model exposes Healthy/Warning/Error summaries");
        var unsafeText = DiagnosticSanitizer.Sanitize(
            "ProxyUrl=http://user:password@127.0.0.1:7897; GITHUB_TOKEN=diagnostic-secret; " +
            "Authorization: Bearer diagnostic-bearer; SESSDATA=fake-sessdata; " +
            "bili_jct=fake-bili-csrf; DedeUserID__ckMd5=fake-ckmd5; csrf_token=fake-csrf-token");
        Assert(!unsafeText.Contains("diagnostic-secret", StringComparison.Ordinal) &&
               !unsafeText.Contains("diagnostic-bearer", StringComparison.Ordinal) &&
               !unsafeText.Contains("fake-sessdata", StringComparison.Ordinal) &&
               !unsafeText.Contains("fake-bili-csrf", StringComparison.Ordinal) &&
               !unsafeText.Contains("fake-ckmd5", StringComparison.Ordinal) &&
               !unsafeText.Contains("fake-csrf-token", StringComparison.Ordinal) &&
               unsafeText.Contains("***:***", StringComparison.Ordinal), "diagnostic sanitizer removes credentials and bearer tokens");

        if (OperatingSystem.IsWindows())
        {
            const string bilibiliCredential = "SESSDATA=fake-sessdata; bili_jct=fake-bili-csrf; DedeUserID=42";
            var encrypted = DpapiCredentialStore.Protect(bilibiliCredential);
            Assert(!encrypted.Contains(bilibiliCredential, StringComparison.Ordinal) &&
                   DpapiCredentialStore.Unprotect(encrypted) == bilibiliCredential,
                "Bilibili credentials round-trip through Windows CurrentUser DPAPI without plaintext blob");
        }

        var dropsHost = new DropsHostService();
        using (var dropsVm = new DropsViewModel(dropsHost, TimeSpan.FromSeconds(1),
                   TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(2), null))
        {
            using var bilibiliState = JsonDocument.Parse("""
                {"platform":"bilibili","networkMode":"DIRECT","account":{"loggedIn":true,"uid":42,"userName":"Cloudlight"},
                 "rooms":[{"id":101,"name":"OWCS","enabled":true,"liveStatus":1}],
                 "settings":{"watchMode":"multi","sessionsPerRoom":80,"reconnectEnabled":true},
                 "tasks":[{"id":"task-a","name":"观看 300 分钟","current":204,"limit":300,"percent":68,"status":"进行中"},
                           {"id":"task-b","name":"观看 120 分钟","current":120,"limit":120,"percent":100,"completed":true,"claimable":true,"status":"可领取"}],
                 "sessions":{"configuredSessions":80,"activeSessions":76,"connectingSessions":2,"retryingSessions":2,"failedSessions":0,"sessions":[]}}
                """);
            dropsVm.ApplyState(DropsPlatform.Bilibili, bilibiliState.RootElement);
            Assert(dropsVm.Platforms.Count == 4 && dropsVm.Platforms.Any(item => item.Platform == DropsPlatform.Bilibili) &&
                   dropsVm.BilibiliDetails.LoggedIn && dropsVm.BilibiliDetails.Tasks.Count == 2 &&
                   dropsVm.BilibiliDetails.ConfiguredSessions == 80 && dropsVm.BilibiliDetails.ActiveSessions == 76 &&
                   dropsVm.BilibiliDetails.NetworkMode == "DIRECT",
                "Bilibili is projected through the shared four-platform Drops state and JSONL fields");
            var bilibili = dropsVm.BilibiliDetails;
            Assert(!typeof(DropsPlatformViewModel).GetProperties().Any(property =>
                           property.Name is "RecoveryStageText" or "LastHeartbeatText" or
                           "LastProgressText" or "LastReconnectText" or "NextRetryText") &&
                   typeof(DropsViewModel).GetMethod("RestartWorkerAsync") is null &&
                   typeof(DropsHostService).GetMethod("RestartAsync") is not null &&
                   Enum.GetNames<DropsConnectionState>().Contains("WaitingRetry") &&
                   Enum.GetNames<DropsConnectionState>().Contains("Recovering"),
                "Drops 页面移除独立自愈展示/UI-only 重启入口，但保留 Worker 重启与恢复状态模型");
            using var emptyBilibiliState = JsonDocument.Parse("""
                {"running":false,"networkMode":"DIRECT","account":{"loggedIn":false,"uid":0,"userName":""},
                 "rooms":[],"settings":{"watchMode":"standard","sessionsPerRoom":1},
                 "sessions":{"configuredSessions":0,"activeSessions":0,"connectingSessions":0,"retryingSessions":0,"failedSessions":0,"sessions":[]}}
                """);
            dropsVm.ApplyState(DropsPlatform.Bilibili, emptyBilibiliState.RootElement);
            Assert(!dropsVm.BilibiliQuickStart.Steps[0].Satisfied &&
                   !dropsVm.BilibiliQuickStart.Steps[1].Satisfied,
                "Bilibili quick-start uses live account/room state for the incomplete login and room steps");
            bilibili.HandleEvent("account", JsonSerializer.SerializeToElement(new
            {
                loggedIn = true, uid = 42, userName = "Cloudlight",
            }));
            Assert(dropsVm.BilibiliQuickStart.Steps[0].Satisfied,
                "Bilibili quick-start step 1 completes immediately after the account event");
            dropsVm.ApplyState(DropsPlatform.Bilibili, bilibiliState.RootElement);
            Assert(dropsVm.BilibiliQuickStart.Steps[1].Satisfied &&
                   dropsVm.BilibiliQuickStart.Steps[2].Satisfied &&
                   dropsVm.BilibiliQuickStart.Steps[2].StateText.Contains("多 Session", StringComparison.Ordinal) &&
                   !dropsVm.BilibiliQuickStart.Steps[3].Satisfied,
                "Bilibili quick-start derives room, multi-Session and stopped state from the live projection");
            bilibili.SessionsPerRoomText = "8";
            Assert(!dropsVm.BilibiliQuickStart.Steps[2].Satisfied &&
                   dropsVm.BilibiliQuickStart.Steps[2].StateText.Contains("待保存", StringComparison.Ordinal),
                "Bilibili quick-start marks an unsaved Session change as pending");
            dropsVm.ApplyState(DropsPlatform.Bilibili, bilibiliState.RootElement);
            dropsVm.Bilibili.Running = true;
            Assert(dropsVm.BilibiliQuickStart.Steps[3].StateKind == "progress" &&
                   dropsVm.BilibiliQuickStart.Steps[3].StateText.Contains("76 / 80 Session", StringComparison.Ordinal),
                "Bilibili quick-start step 4 shows active/target Sessions while Drops is running");
            dropsVm.Bilibili.Running = false;
            Assert(!dropsVm.BilibiliQuickStart.Steps[3].Satisfied &&
                   dropsVm.BilibiliQuickStart.Steps[3].StateText.Contains("未开始", StringComparison.Ordinal),
                "Bilibili quick-start step 4 returns to not-started after Drops stops");
            dropsVm.UpdateProxySettings(true, "http://127.0.0.1:7897", true, true);
            Assert(bilibili.BilibiliUseProxy && bilibili.GlobalProxyEnabled &&
                   bilibili.NetworkMode == "PROXY" && bilibili.ProxyEndpointText == "127.0.0.1:7897" &&
                   bilibili.NetworkPolicyText.Contains("全局代理", StringComparison.Ordinal),
                "Bilibili VM projects the explicit global proxy policy without exposing credentials");
            dropsVm.UpdateProxySettings(false, "", false, false);
            Assert(bilibili.ScanQrLoginCommand is not null && bilibili.CancelQrCommand is not null &&
                   bilibili.LogoutCommand is not null && bilibili.DiscoverCommand is not null &&
                   bilibili.AddRoomCommand is not null && bilibili.RemoveRoomCommand is not null &&
                   bilibili.StartCommand is not null && bilibili.StopCommand is not null &&
                   bilibili.RefreshCommand is not null && bilibili.ClaimRewardCommand is not null,
                "Bilibili page operations are exposed as VM commands");
            Assert(bilibili.Rooms.Count == 1 && bilibili.Tasks.Count == 2 && bilibili.Sessions is not null &&
                   bilibili.ConfiguredSessions == 80 && bilibili.ActiveSessions == 76 &&
                   bilibili.ConnectingSessions == 2 && bilibili.RetryingSessions == 2 &&
                   bilibili.FailedSessions == 0 && bilibili.NetworkMode == "DIRECT" &&
                   bilibili.AutoTaskProgress,
                "Bilibili rooms, official tasks and session aggregate properties are bindable");
            dropsVm.SelectPlatform(DropsPlatform.Bilibili);
            Assert(dropsVm.SelectedPlatform == DropsPlatform.Bilibili &&
                   dropsVm.BilibiliPanelVisibility == Visibility.Visible &&
                   dropsVm.SoopPanelVisibility == Visibility.Collapsed &&
                   dropsVm.YouTubePanelVisibility == Visibility.Collapsed &&
                   dropsVm.TwitchPanelVisibility == Visibility.Collapsed,
                "Bilibili platform selection drives the bound content visibility");
            var changedProperties = new HashSet<string>(StringComparer.Ordinal);
            bilibili.PropertyChanged += (_, args) =>
            {
                if (!string.IsNullOrWhiteSpace(args.PropertyName)) changedProperties.Add(args.PropertyName);
            };
            bilibili.SessionsPerRoomText = "8";
            using var waitingQr = JsonDocument.Parse("{\"state\":\"waiting_scan\",\"message\":\"等待扫码\"}");
            bilibili.HandleEvent("qr_login", waitingQr.RootElement);
            var qrVisibleWhileWaiting = bilibili.QrAreaVisibility == Visibility.Visible;
            using var successQr = JsonDocument.Parse("{\"state\":\"success\",\"message\":\"登录成功\"}");
            bilibili.HandleEvent("qr_login", successQr.RootElement);
            Assert(qrVisibleWhileWaiting && changedProperties.Contains(nameof(BilibiliDropsViewModel.SessionsPerRoom)) &&
                   bilibili.SessionsPerRoom == 8 && bilibili.QrState == "success" &&
                   bilibili.QrAreaVisibility == Visibility.Collapsed,
                "Bilibili VM raises PropertyChanged for settings and closes the QR area after success");
        }
        dropsHost.DisposeAsync().AsTask().GetAwaiter().GetResult();

        var main = new MainViewModel();
        string? secretLog = null;
        string? zip = null;
        try
        {
            main.Settings.EnableProxy = true;
            main.Settings.ProxyUrl = "http://user:password@127.0.0.1:7897";
            var diagnostic = new DiagnosticService(main);
            var readOnlySettings = new AppSettings
            {
                LastUpdateCheckAt = DateTimeOffset.UtcNow.AddHours(-2),
                LastUpdateFailure = "previous failure",
                UpdateChannel = UpdateChannel.Stable,
            };
            var readOnlyAt = readOnlySettings.LastUpdateCheckAt;
            var readOnlyFailure = readOnlySettings.LastUpdateFailure;
            var readOnlyCoordinator = new UpdateCheckCoordinator(
                new StubUpdateService(UpdateResult("2.1.0", hasUpdate: false)), readOnlySettings);
            var readOnlyResult = await readOnlyCoordinator.CheckReadOnlyAsync();
            Assert(readOnlyResult.Status == UpdateCheckResultStatus.Success &&
                   readOnlyCoordinator.LastResult is null && readOnlyCoordinator.LastCheckAt is null &&
                   readOnlySettings.LastUpdateCheckAt == readOnlyAt &&
                   readOnlySettings.LastUpdateFailure == readOnlyFailure,
                "diagnostic-style update metadata probing is read-only and does not update persisted check state");
            using var cancelled = new CancellationTokenSource();
            cancelled.Cancel();
            var cancelledReport = await diagnostic.RunAsync(cancelled.Token);
            Assert(cancelledReport.Cancelled, "diagnostics returns a partial cancelled report without throwing");

            Directory.CreateDirectory(AppPaths.Current.LogsDir);
            secretLog = Path.Combine(AppPaths.Current.LogsDir, $"diagnostic-selftest-{Guid.NewGuid():N}.log");
            await File.WriteAllTextAsync(secretLog,
                secretDetails + "\nGITHUB_TOKEN=diagnostic-secret\nAuthorization: Bearer diagnostic-bearer\n");
            zip = await diagnostic.ExportBundleAsync(reportModel);
            using var archive = ZipFile.OpenRead(zip);
            var names = archive.Entries.Select(entry => entry.FullName).ToHashSet(StringComparer.OrdinalIgnoreCase);
            Assert(names.Contains("diagnostics.json") && names.Contains("diagnostics.txt") &&
                   names.Contains("environment.txt") && names.Contains("snapshot-summary.json") &&
                   names.Contains("update-summary.json") && names.Contains("drops-summary.json"),
                "diagnostics export contains the required summary files");
            var requiredEntries = new[] { "diagnostics.json", "diagnostics.txt", "environment.txt" };
            foreach (var required in requiredEntries)
            {
                var entry = archive.GetEntry(required);
                Assert(entry is not null, $"diagnostics ZIP includes {required}");
                using var reader = new StreamReader(entry!.Open());
                var text = reader.ReadToEnd();
                Assert(!text.Contains("abcdef", StringComparison.Ordinal) &&
                       !text.Contains("user:password", StringComparison.Ordinal) &&
                       !text.Contains("session-cookie-value", StringComparison.Ordinal) &&
                       !text.Contains("session-set-cookie-value", StringComparison.Ordinal) &&
                       !text.Contains("fake-sessdata", StringComparison.Ordinal) &&
                       !text.Contains("fake-bili-csrf", StringComparison.Ordinal) &&
                       !text.Contains("fake-ckmd5", StringComparison.Ordinal) &&
                       !text.Contains("fake-csrf-token", StringComparison.Ordinal),
                    $"{required} redacts all injected secret values");
            }
            foreach (var entry in archive.Entries.Where(item => item.FullName.StartsWith("logs/", StringComparison.OrdinalIgnoreCase)))
            {
                using var reader = new StreamReader(entry.Open());
                var text = reader.ReadToEnd();
                Assert(!text.Contains("abcdef", StringComparison.Ordinal) &&
                       !text.Contains("diagnostic-secret", StringComparison.Ordinal) &&
                       !text.Contains("diagnostic-bearer", StringComparison.Ordinal) &&
                       !text.Contains("user:password", StringComparison.Ordinal) &&
                       !text.Contains("fake-sessdata", StringComparison.Ordinal) &&
                       !text.Contains("fake-bili-csrf", StringComparison.Ordinal) &&
                       !text.Contains("fake-ckmd5", StringComparison.Ordinal) &&
                       !text.Contains("fake-csrf-token", StringComparison.Ordinal),
                    "diagnostic log entries redact injected secret values");
            }
            Assert(DiagnosticService.TryNormalizeZipEntryName("logs\\app.log", out var normalized) &&
                   normalized == "logs/app.log" &&
                   !DiagnosticService.TryNormalizeZipEntryName("....\\file", out _) &&
                   !DiagnosticService.TryNormalizeZipEntryName(@"C:\\Users\\someone\\log.txt", out _) &&
                   !DiagnosticService.TryNormalizeZipEntryName(@"\\server\\share\\log.txt", out _) &&
                   !DiagnosticService.TryNormalizeZipEntryName("../file", out _),
                "diagnostic ZIP entry names normalize safely and reject traversal/absolute paths");
            Assert(!File.Exists(Path.Combine(AppPaths.Current.Root, ".diagnostic-write-test")),
                "diagnostics never leaves a write-probe file behind");
        }
        finally
        {
            if (secretLog is not null) TryDeleteFile(secretLog);
            if (zip is not null) TryDeleteFile(zip);
            main.CloudHttpClients.Dispose();
        }

        INotificationService notification = new RecordingNotificationService();
        notification.Initialize();
        Assert(notification.TryNotify(new NotificationRequest("测试通知", "不会真正发送 Toast",
                   NotificationCategory.Updates, "updates")) &&
               ((RecordingNotificationService)notification).Requests.Single().Action == "updates",
            "notification abstraction records actions without sending a real Toast in tests");
        notification.Dispose();
        using (var toast = new WindowsToastNotificationService(new AppSettings
                   { EnableWindowsNotifications = false }))
        {
            toast.Initialize();
            toast.Initialize();
            Assert(!toast.TryNotify(new NotificationRequest("禁用通知", "不会显示",
                       NotificationCategory.Drops)),
                "Toast initialization is idempotent and disabled notifications never enter the OS channel");
        }
        var notificationRequests = new List<NotificationRequest>();
        using (var gate = new DropsNotificationGate(notificationRequests.Add, TimeSpan.FromMilliseconds(10)))
        {
            for (var i = 0; i < 30; i++)
                gate.ReportFailure(DropsPlatform.Twitch, "Twitch 连接中断", "正在自动重试。");
            gate.ReportRecovery(DropsPlatform.Twitch, "Twitch 已恢复连接", "自动恢复流程已完成。");
            await gate.FlushForSelfTestAsync();
            Assert(notificationRequests.Count == 2 &&
                   notificationRequests.Count(item => item.Message.Contains("自动重试", StringComparison.Ordinal)) == 1 &&
                   notificationRequests.Count(item => item.Message.Contains("恢复", StringComparison.Ordinal)) == 1,
                "Drops toast gate collapses repeated failures and emits one debounced recovery notification");
        }
        using (var gate = new DropsNotificationGate(_ => throw new InvalidOperationException("toast unavailable"),
                   TimeSpan.FromMilliseconds(1)))
        {
            gate.ReportFailure(DropsPlatform.Soop, "SOOP 连接中断", "正在自动重试。");
            gate.ReportRecovery(DropsPlatform.Soop, "SOOP 已恢复连接", "自动恢复流程已完成。");
            await gate.FlushForSelfTestAsync();
            Assert(true, "Toast notification exceptions are swallowed outside business flow");
        }
        var duplicateError = new InvalidOperationException($"selftest-error-{Guid.NewGuid():N}");
        Assert(App.ShouldShowUnhandledDialog(duplicateError) &&
               !App.ShouldShowUnhandledDialog(new InvalidOperationException(duplicateError.Message)),
            "identical unhandled errors are throttled to one dialog while each occurrence remains loggable");
        report.AppendLine("TEST 13 diagnostics and notifications: PASS (severity/cancel/sanitizer/ZIP required entries/no secrets/read-only/notification gate/error-dialog throttle)");
    }

    private static void WriteRuntimeFiles(string gameRoot)
    {
        var files = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["cache/session.bin"] = "cache",
            ["logs/game.log"] = "log",
            ["_retail_/shadercache/compiled.bin"] = "shader cache",
            ["_retail_/temp/runtime-state.dat"] = "runtime temp",
            ["crashdumps/overwatch.dmp"] = "crash dump",
            ["data/casc/ecache/data.000"] = "CASC encoding cache",
            ["data/casc/ecache/0000000001.idx"] = "CASC encoding cache index",
            ["data/casc/data/shmem"] = "CASC shared memory state",
            ["data/casc/pro/shmem"] = "CASC product shared memory state",
        };
        foreach (var (relative, content) in files)
        {
            var path = Path.Combine(gameRoot, relative.Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, content);
        }
    }

    private static void CreateLargeFile(string path, long length, byte firstByte)
    {
        using var stream = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None);
        stream.SetLength(length);
        stream.Position = 0;
        stream.WriteByte(firstByte);
    }

    private static byte FirstByte(string path)
    {
        using var stream = File.OpenRead(path);
        return (byte)stream.ReadByte();
    }

    private sealed record BackupFileState(byte[] Content, DateTime LastWriteTimeUtc);

    private static Dictionary<string, BackupFileState> CaptureBackupFiles(string root) =>
        Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories).ToDictionary(
            file => Path.GetRelativePath(root, file).Replace('\\', '/'),
            file => new BackupFileState(File.ReadAllBytes(file), File.GetLastWriteTimeUtc(file)),
            StringComparer.OrdinalIgnoreCase);

    private static void AssertBackupFilesUnchanged(
        string root, IReadOnlyDictionary<string, BackupFileState> expected, string message)
    {
        var actual = CaptureBackupFiles(root);
        Assert(actual.Keys.OrderBy(path => path, StringComparer.OrdinalIgnoreCase).SequenceEqual(
                expected.Keys.OrderBy(path => path, StringComparer.OrdinalIgnoreCase), StringComparer.OrdinalIgnoreCase),
            message + " file list");
        foreach (var (path, before) in expected)
        {
            Assert(actual[path].Content.AsSpan().SequenceEqual(before.Content), message + " content: " + path);
            Assert(actual[path].LastWriteTimeUtc == before.LastWriteTimeUtc, message + " timestamp: " + path);
        }
    }

    private static void AssertActions(RegionPreparationGuide guide, params RegionPreparationAction[] expected)
    {
        Assert(guide.VisibleActions.SequenceEqual(expected),
            $"{guide.State} actions: expected {string.Join(",", expected)}, actual {string.Join(",", guide.VisibleActions)}");
    }

    private static HttpResponseMessage JsonResponse(string json) => new(HttpStatusCode.OK)
    {
        Content = new StringContent(json, Encoding.UTF8, "application/json"),
    };

    private static UpdateCheckResult UpdateResult(string latestVersion, bool hasUpdate) => new()
    {
        Status = UpdateCheckResultStatus.Success,
        CurrentVersion = "1.0.0",
        LatestVersion = latestVersion,
        HasUpdate = hasUpdate,
        ReleaseUrl = $"https://github.com/Cloud-Light125/CloudLight-Blizzard/releases/tag/v{latestVersion}",
    };

    private static UpdateCheckResult CreateDownloadResult(string version, byte[] bytes, string? digest = null)
    {
        var installerName = $"CloudLight-Blizzard-{version}-win-x64-Setup.exe";
        return new UpdateCheckResult
        {
            Status = UpdateCheckResultStatus.Success,
            CurrentVersion = "2.1.1",
            LatestVersion = version,
            HasUpdate = true,
            Tag = $"v{version}",
            ReleaseUrl = $"https://github.com/Cloud-Light125/CloudLight-Blizzard/releases/tag/v{version}",
            InstallerDownloadUrl =
                $"https://github.com/Cloud-Light125/CloudLight-Blizzard/releases/download/v{version}/{installerName}",
            InstallerName = installerName,
            InstallerSize = bytes.Length,
            InstallerDigest = digest ?? "sha256:" + Convert.ToHexString(SHA256.HashData(bytes)),
        };
    }

    private static CloudHttpClientFactory CreateDownloadClients(string workspace, HttpMessageHandler handler) =>
        new(new AppSettings { EnableProxy = false },
            Path.Combine(workspace, $"update-resilience-{Guid.NewGuid():N}.log"),
            _ => new HttpClient(handler));

    private static HttpResponseMessage FullDownloadResponse(byte[] bytes) => new(HttpStatusCode.OK)
    {
        Content = new ByteArrayContent(bytes),
    };

    private static void TryDeleteFile(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); } catch { }
    }

    private static void TryDeleteDirectory(string path)
    {
        try { if (Directory.Exists(path)) Directory.Delete(path, true); } catch { }
    }

    private sealed class InlineProgress<T> : IProgress<T>
    {
        private readonly Action<T> _report;
        public InlineProgress(Action<T> report) => _report = report;
        public void Report(T value) => _report(value);
    }

    private sealed class DelegateInstallerLauncher(Func<string, Process?> start) : IInstallerLauncher
    {
        public Process? Start(string installerPath) => start(installerPath);
    }

    private sealed class BlockingUpdateHandler : HttpMessageHandler
    {
        public TaskCompletionSource<bool> Started { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Started.TrySetResult(true);
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            throw new OperationCanceledException(cancellationToken);
        }
    }

    private sealed class StubHttpHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, HttpResponseMessage> _response;
        public StubHttpHandler(Func<HttpRequestMessage, HttpResponseMessage> response) => _response = response;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(_response(request));
    }

    private sealed class ScriptedDownloadHandler : HttpMessageHandler
    {
        private readonly Func<int, HttpRequestMessage, HttpResponseMessage> _response;
        public ScriptedDownloadHandler(Func<int, HttpRequestMessage, HttpResponseMessage> response) => _response = response;
        public int Calls { get; private set; }
        public List<string?> Ranges { get; } = new();

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            Ranges.Add(request.Headers.Range?.ToString());
            var call = ++Calls;
            return Task.FromResult(_response(call, request));
        }
    }

    private sealed class ThrowingHttpHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromException<HttpResponseMessage>(new HttpRequestException("simulated network failure"));
    }

    private sealed class StubUpdateService : IUpdateService
    {
        private readonly UpdateCheckResult _result;
        public StubUpdateService(UpdateCheckResult result) => _result = result;
        public int Calls { get; private set; }
        public string CurrentVersion => _result.CurrentVersion;
        public Task<UpdateCheckResult> CheckAsync(CancellationToken cancellationToken = default)
        {
            Calls++;
            return Task.FromResult(_result);
        }
    }

    private sealed class GatedUpdateService : IUpdateService
    {
        private readonly UpdateCheckResult _result;
        private readonly TaskCompletionSource _release = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public GatedUpdateService(UpdateCheckResult result) => _result = result;
        public int Calls { get; private set; }
        public string CurrentVersion => _result.CurrentVersion;
        public void Release() => _release.TrySetResult();
        public async Task<UpdateCheckResult> CheckAsync(CancellationToken cancellationToken = default)
        {
            Calls++;
            await _release.Task.WaitAsync(cancellationToken);
            return _result;
        }
    }

    private static void Assert(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException("Assertion failed: " + message);
    }
}
