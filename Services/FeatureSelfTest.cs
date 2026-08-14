using System.IO;
using System.Net;
using System.Net.Http;
using System.Text;
using CloudLightBlizzard.Models;
using CloudLightBlizzard.Stats;
using CloudLightBlizzard.Services.OverwatchRegion;
using CloudLightBlizzard.ViewModels;
using CloudLightBlizzard.Views.Pages;
using GameRegion = CloudLightBlizzard.Services.OverwatchRegion.OverwatchRegion;

namespace CloudLightBlizzard.Services;

public static class FeatureSelfTest
{
    public static void Run(string outputRoot)
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
            RunAccountSnapshotTest(workspace, report);
            RunLoginVerificationTest(report);
            RunStatsExplicitQueryWorkflowTest(report).GetAwaiter().GetResult();
            RunUpdateCheckTest(workspace, report).GetAwaiter().GetResult();
            RunRegionPreparationGuideTest(report);
            RunRegionGenerationTest(workspace, report).GetAwaiter().GetResult();
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

    private static async Task RunStatsExplicitQueryWorkflowTest(StringBuilder report)
    {
        var loginChecks = 0;
        var loginDialogs = 0;
        var chinaRequests = 0;
        var internationalRequests = 0;
        var loggedIn = false;
        var workflow = new StatsQueryWorkflow();

        workflow.PageOpened();
        Assert(loginChecks == 0 && loginDialogs == 0 && chinaRequests == 0 && internationalRequests == 0,
            "startup/page open performs no stats or login work");

        var china = new StatsAccountSelection("1:cn", true);
        workflow.SelectAccount(china);
        workflow.SelectAccount(new StatsAccountSelection("2:global", false));
        workflow.SelectAccount(china);
        Assert(workflow.State == StatsQueryState.Idle && loginChecks == 0 && chinaRequests == 0 && internationalRequests == 0,
            "account switch/dropdown selection performs no request");

        async Task<bool> CheckLogin()
        {
            loginChecks++;
            await Task.Yield();
            return loggedIn;
        }
        async Task<object> QueryChina()
        {
            chinaRequests++;
            await Task.Yield();
            return new object();
        }
        async Task<object> QueryInternational()
        {
            internationalRequests++;
            await Task.Yield();
            return new object();
        }

        await workflow.QueryAsync(CheckLogin, QueryChina, QueryInternational);
        Assert(workflow.State == StatsQueryState.LoginRequired && loginChecks == 1 &&
               loginDialogs == 0 && chinaRequests == 0,
            "China query without login only enters LoginRequired");

        await workflow.LoginAsync(async () =>
        {
            loginDialogs++;
            loggedIn = true;
            await Task.Yield();
            return true;
        });
        Assert(workflow.State == StatsQueryState.ReadyToQuery && loginDialogs == 1 && chinaRequests == 0,
            "explicit login succeeds without auto query");

        await workflow.QueryAsync(CheckLogin, QueryChina, QueryInternational);
        Assert(workflow.State == StatsQueryState.Loaded && chinaRequests == 1 && internationalRequests == 0,
            "explicit China query calls China service once");

        workflow.SelectAccount(new StatsAccountSelection("2:global", false));
        Assert(workflow.State == StatsQueryState.Idle && internationalRequests == 0,
            "international selection remains idle");
        await workflow.QueryAsync(CheckLogin, QueryChina, QueryInternational);
        Assert(workflow.State == StatsQueryState.Loaded && internationalRequests == 1 && loginChecks == 2,
            "explicit international query calls career service without China login check");

        workflow.SelectAccount(china);
        Assert(workflow.State == StatsQueryState.Loaded && chinaRequests == 1 && internationalRequests == 1,
            "returning to an account displays memory cache without refreshing");
        report.AppendLine("TEST 3 stats explicit workflow: PASS (startup/navigation/account switch/selection=0; login and China/global queries are button-only)");
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
        AssertActions(waiting, RegionPreparationAction.ContinueOtherRegion);
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
            ChinaBackupComplete = true,
            InternationalBackupComplete = true,
            ActiveGenerationId = "existing-active",
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
        var mixed = RegionPreparationGuide.Create(readyStatus, RegionOperationPhase.None, false, false, null, backupRoot);
        Assert(mixed.State == RegionPreparationState.Mixed && mixed.CanRestore, "Mixed offers local recovery");
        AssertActions(mixed, RegionPreparationAction.RestoreChina, RegionPreparationAction.RestoreInternational);

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
        Assert(await manager.ContinuePreparationAsync(game) == RegionBackupState.Ready,
            "one cross-region continuation makes both sides ready");

        var status = await manager.GetStatusAsync(game);
        Assert(status.State == RegionBackupState.Ready && status.ChinaBackupComplete && status.InternationalBackupComplete,
            "generation is ready for both regions");
        var generationRoot = Path.Combine(store, "generations", status.ActiveGenerationId!);
        var generation = OverwatchRegionBackupStore.ReadJson<OverwatchRegionGeneration>(Path.Combine(generationRoot, "pair.json"))!;
        var kinds = generation.Differences.ToDictionary(item => item.RelativePath, item => item.Kind, StringComparer.OrdinalIgnoreCase);
        Assert(!kinds.ContainsKey("same.txt"), "Same is not persisted as a switch operation");
        Assert(kinds["china-only.txt"] == RegionDifferenceKind.ChinaOnly, "ChinaOnly classification");
        Assert(kinds["international-only.txt"] == RegionDifferenceKind.InternationalOnly, "InternationalOnly classification");
        Assert(kinds["different.txt"] == RegionDifferenceKind.Different, "Different classification");
        Assert(kinds["large.bin"] == RegionDifferenceKind.Different, "large Different classification");
        Assert(new FileInfo(Path.Combine(generationRoot, "backups", "china", "large.bin")).Length == 256L * 1024 * 1024,
            "full China large file stored");
        Assert(new FileInfo(Path.Combine(generationRoot, "backups", "international", "large.bin")).Length == 256L * 1024 * 1024,
            "full International large file stored");

        // Build metadata may be partially changed by Battle.net and must not by itself stale the generation.
        File.WriteAllText(Path.Combine(game, ".build.info"), "partial Battle.net metadata");

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
        report.AppendLine("TEST 6 Generation/Staging: PASS (Mixed->International/China, full hash, .build.info tolerance, common update rejection, 256MB copies)");
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
        report.AppendLine("TEST 7 account switch pipeline: PASS (strict read-only-backup order/fail-fast/no launch after region failure)");
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
        var china = new AccountRow { BattleTag = "China#1", RegionOverride = AccountRegionOverride.China };
        var international = new AccountRow { BattleTag = "Global#1", RegionOverride = AccountRegionOverride.International };
        Assert(StatsPage.DataSourceFor(china) == "ChinaStats", "china account routes to china stats");
        Assert(StatsPage.DataSourceFor(international) == "BlizzardCareer", "international account routes to career");
        report.AppendLine("TEST 8 settings/account list: PASS");
    }

    private static void RunAppPathsMigrationTest(string workspace, StringBuilder report)
    {
        var oldRoot = Path.Combine(workspace, "legacy-app");
        var newRoot = Path.Combine(workspace, "documents-app");
        Directory.CreateDirectory(Path.Combine(oldRoot, "accounts", "42"));
        Directory.CreateDirectory(Path.Combine(oldRoot, "logs"));
        Directory.CreateDirectory(Path.Combine(oldRoot, "ow", "img"));
        Directory.CreateDirectory(Path.Combine(oldRoot, "region-switch", "generations", "g1"));
        File.WriteAllText(Path.Combine(oldRoot, "settings.json"), "{\"DarkMode\":true}");
        File.WriteAllText(Path.Combine(oldRoot, "accounts", "42", "meta.json"), "{}");
        File.WriteAllText(Path.Combine(oldRoot, "logs", "account-switch.log"), "ok");
        File.WriteAllText(Path.Combine(oldRoot, "ow", "img", "cache.bin"), "cache");
        File.WriteAllText(Path.Combine(oldRoot, "region-switch", "active-generation.json"), "{}");
        File.WriteAllText(Path.Combine(oldRoot, "region-switch", "generations", "g1", "pair.json"), "{}");

        var paths = new AppPaths(newRoot, oldRoot);
        var result = paths.MigrateLegacyData();
        Assert(File.Exists(paths.SettingsFile), "settings migrated");
        Assert(File.Exists(Path.Combine(paths.AccountsDir, "42", "meta.json")), "accounts migrated");
        Assert(File.Exists(Path.Combine(paths.LogsDir, "account-switch.log")), "logs migrated");
        Assert(File.Exists(Path.Combine(paths.OverwatchCacheDir, "img", "cache.bin")), "ow cache migrated");
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
        report.AppendLine("TEST 9 app paths migration: PASS (settings/accounts/logs/ow/default move/custom preserved)");
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
