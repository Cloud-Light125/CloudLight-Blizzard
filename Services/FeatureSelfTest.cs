using System.IO;
using System.Net;
using System.Net.Http;
using System.Text;
using CloudLightBlizzard.Models;
using CloudLightBlizzard.Services.Drops;
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
            if (string.Equals(test, "region-verified", StringComparison.OrdinalIgnoreCase))
            {
                RunVerifiedDifferenceRegionTest(workspace, report).GetAwaiter().GetResult();
                report.AppendLine("OVERALL: PASS");
                return;
            }
            if (string.Equals(test, "region-guide", StringComparison.OrdinalIgnoreCase))
            {
                RunRegionPreparationGuideTest(report);
                report.AppendLine("OVERALL: PASS");
                return;
            }
            RunAccountSnapshotTest(workspace, report);
            RunLoginVerificationTest(report);
            RunTwitchConnectionStateTest(report);
            RunPlatformLogTailSessionTest(workspace, report).GetAwaiter().GetResult();
            RunUpdateCheckTest(workspace, report).GetAwaiter().GetResult();
            RunRegionPreparationGuideTest(report);
            RunRegionGenerationTest(workspace, report).GetAwaiter().GetResult();
            RunBestEffortRegionTest(workspace, report).GetAwaiter().GetResult();
            RunAccountSwitchOrderTest(report).GetAwaiter().GetResult();
            RunAccountPreferenceTest(workspace, report);
            RunAppPathsMigrationTest(workspace, report);
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
            Assert(!vm.CanTwitchLogin, "Twitch auth-required blocks duplicate device-code requests");
        }
        report.AppendLine("Twitch connection state: PASS");
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

        HttpRequestMessage? capturedRequest = null;
        var releaseJson = """
            {
              "tag_name": "v1.0.1",
              "name": "CloudLight Blizzard 1.0.1",
              "body": "修复与改进",
              "html_url": "https://github.com/yundan125/CloudLight-Blizzard/releases/tag/v1.0.1",
              "published_at": "2026-08-14T08:00:00Z",
              "draft": false,
              "prerelease": false,
              "assets": [
                {
                  "name": "CloudLight-Blizzard-1.0.1-win-x64-Setup.exe",
                  "browser_download_url": "https://github.com/yundan125/CloudLight-Blizzard/releases/download/v1.0.1/CloudLight-Blizzard-1.0.1-win-x64-Setup.exe"
                }
              ]
            }
            """;
        using (var client = new HttpClient(new StubHttpHandler(request =>
               {
                   capturedRequest = request;
                   return JsonResponse(releaseJson);
               })))
        using (var service = new UpdateService(client, "1.0.0"))
        {
            var result = await service.CheckAsync();
            Assert(result.Status == UpdateCheckResultStatus.Success && result.HasUpdate &&
                   result.LatestVersion == "1.0.1", "formal GitHub release is parsed and compared");
            Assert(result.ReleaseUrl.EndsWith("/releases/tag/v1.0.1", StringComparison.Ordinal),
                "release html_url is retained");
            Assert(result.ReleaseNotes == "修复与改进" && result.PublishedAt.HasValue,
                "release notes and publish time are retained");
            Assert(result.InstallerDownloadUrl?.EndsWith("Setup.exe", StringComparison.Ordinal) == true,
                "conventional installer asset is parsed without downloading");
            Assert(capturedRequest?.RequestUri?.AbsoluteUri == UpdateService.LatestReleaseApiUrl,
                "only the fixed latest-release API is requested");
            Assert(capturedRequest?.Headers.UserAgent.ToString().Contains("CloudLight-Blizzard", StringComparison.Ordinal) == true &&
                   capturedRequest.Headers.Accept.Any(value => value.MediaType == "application/vnd.github+json"),
                "GitHub request headers are present");
        }

        var prereleaseJson = releaseJson.Replace("\"prerelease\": false", "\"prerelease\": true");
        using (var client = new HttpClient(new StubHttpHandler(_ => JsonResponse(prereleaseJson))))
        using (var service = new UpdateService(client, "1.0.0"))
            Assert((await service.CheckAsync()).Status == UpdateCheckResultStatus.NoRelease,
                "prerelease response is ignored");
        var draftJson = releaseJson.Replace("\"draft\": false", "\"draft\": true");
        using (var client = new HttpClient(new StubHttpHandler(_ => JsonResponse(draftJson))))
        using (var service = new UpdateService(client, "1.0.0"))
            Assert((await service.CheckAsync()).Status == UpdateCheckResultStatus.NoRelease,
                "draft response is ignored");

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

        report.AppendLine("TEST 4 GitHub updates: PASS (semantic versions/stable release/assets/skip/manual/failure/delay/single-flight)");
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
        Assert(notPrepared.State == RegionPreparationState.NotPrepared, "empty maps to NotPrepared");
        AssertActions(notPrepared, RegionPreparationAction.ChooseChina, RegionPreparationAction.ChooseInternational);

        var preparingCurrent = RegionPreparationGuide.Create(empty, RegionOperationPhase.PreparingCurrentRegion,
            false, true, new RegionProgress("copying", 1, 2), backupRoot);
        Assert(preparingCurrent.State == RegionPreparationState.PreparingCurrentRegion && preparingCurrent.CanCancel,
            "PreparingCurrentRegion only allows cancellation");
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
        Assert(waiting.State == RegionPreparationState.WaitingForOtherRegion &&
               waiting.ContinueButtonText == "我已完成国际服更新", "waiting names the target region");
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
            ActiveGenerationId = "existing-active",
        };
        var outdated = RegionPreparationGuide.Create(outdatedStatus, RegionOperationPhase.None, false, false, null, backupRoot);
        Assert(outdated.State == RegionPreparationState.Outdated, "updated generation maps to Outdated");
        AssertActions(outdated, RegionPreparationAction.Restart);

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
               updatedStatus.State == RegionBackupState.Stale, "changed common Same core marks generation updated");
        var differentBefore = File.ReadAllText(Path.Combine(game, "different.txt"));
        try
        {
            await manager.NormalizeToRegionAsync(game, GameRegion.China);
            throw new InvalidOperationException("updated generation should have been rejected");
        }
        catch (InvalidDataException) { }
        Assert(File.ReadAllText(Path.Combine(game, "different.txt")) == differentBefore,
            "updated common baseline is rejected before region files are overwritten");

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
        report.AppendLine("TEST 6 Generation/Staging: PASS (drift-tolerant China/International detection, strong correction/conflict, successful/failed normalize state, update rejection, strict restore hash, 256MB copies)");
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
               updated.SwitchEligibility == RegionSwitchEligibility.GameUpdated,
            "Updated remains blocked independently from BestEffort Unknown");

        var log = File.ReadAllText(RegionSwitchLog.FileOverride!);
        Assert(log.Contains("GenerationCompatibility=Unknown", StringComparison.Ordinal) &&
               log.Contains("SwitchMode=BestEffort", StringComparison.Ordinal) &&
               log.Contains("IgnoredUnknownFiles=未枚举，未参与处理", StringComparison.Ordinal) &&
               log.Contains("Verification=passed", StringComparison.Ordinal),
            "BestEffort logs record Unknown reason, mode, known-only handling, and verification");
        report.AppendLine("TEST 7 BestEffort/volatile/staging/account: PASS (A-H: unknown-file preservation, known-file repair, strict backup hash, Updated block, Unknown direct normalize, account order, volatile filtering, source staging reuse)");
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
        ReleaseUrl = $"https://github.com/yundan125/CloudLight-Blizzard/releases/tag/v{latestVersion}",
    };

    private sealed class StubHttpHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, HttpResponseMessage> _response;
        public StubHttpHandler(Func<HttpRequestMessage, HttpResponseMessage> response) => _response = response;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(_response(request));
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
