using System.IO;
using System.Text;
using BnetSwitch.Models;
using BnetSwitch.Services.OverwatchRegion;
using BnetSwitch.ViewModels;
using BnetSwitch.Views.Pages;
using GameRegion = BnetSwitch.Services.OverwatchRegion.OverwatchRegion;

namespace BnetSwitch.Services;

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
        report.AppendLine("TEST 1 account controlled mirror: PASS (recursive/manifest/A-B cleanup/exclusions)");
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
        report.AppendLine("TEST 3 Generation/Staging: PASS (Mixed->International/China, full hash, .build.info tolerance, common update rejection, 256MB copies)");
    }

    private static async Task RunAccountSwitchOrderTest(StringBuilder report)
    {
        var calls = new List<string>();
        static Task Done() => Task.CompletedTask;
        await AccountSwitchPipeline.ExecuteAsync(
            () => { calls.Add("Quit BattleNet"); return Done(); },
            () => { calls.Add("Save Source Account"); return Done(); },
            () => { calls.Add("Normalize Game -> International"); return Done(); },
            () => { calls.Add("Restore Target Account"); return Done(); },
            () => { calls.Add("Launch BattleNet"); return Done(); });
        Assert(string.Join(" > ", calls) ==
               "Quit BattleNet > Save Source Account > Normalize Game -> International > Restore Target Account > Launch BattleNet",
            "account switch ordering");

        calls.Clear();
        try
        {
            await AccountSwitchPipeline.ExecuteAsync(
                () => { calls.Add("Quit BattleNet"); return Done(); },
                () => { calls.Add("Save Source Account"); return Done(); },
                () => { calls.Add("Normalize Failed"); throw new IOException("simulated failure"); },
                () => { calls.Add("Restore Target Account"); return Done(); },
                () => { calls.Add("Launch BattleNet"); return Done(); });
        }
        catch (IOException) { }
        Assert(!calls.Contains("Restore Target Account") && !calls.Contains("Launch BattleNet"),
            "normalize failure prevents target restore and Battle.net launch");
        report.AppendLine("TEST 4 account switch pipeline: PASS (strict order/fail-fast/no launch after region failure)");
    }

    private static void RunAccountPreferenceTest(string workspace, StringBuilder report)
    {
        var settingsPath = Path.Combine(workspace, "settings.json");
        var settings = new AppSettings { RegionStoragePath = @"D:\Region Data" };
        var preference = settings.PreferenceFor(123456);
        preference.CustomName = "主号";
        preference.Remark = "常用账号";
        preference.Region = AccountRegionOverride.China;
        settings.SaveTo(settingsPath);
        var loaded = AppSettings.LoadFrom(settingsPath);
        Assert(loaded.RegionStoragePath == @"D:\Region Data", "custom region storage persisted");
        Assert(loaded.PreferenceFor(123456).Region == AccountRegionOverride.China, "account preference persisted");
        var current = new AccountRow { AccountId = 123456, BattleTag = "CloudLight#1234", IsActive = true, HasProfile = true };
        Assert(MainViewModel.SelectSavedAccounts(new[] { current }).Single() == current, "active saved account remains listed");
        var china = new AccountRow { BattleTag = "China#1", RegionOverride = AccountRegionOverride.China };
        var international = new AccountRow { BattleTag = "Global#1", RegionOverride = AccountRegionOverride.International };
        Assert(StatsPage.DataSourceFor(china) == "ChinaStats", "china account routes to china stats");
        Assert(StatsPage.DataSourceFor(international) == "BlizzardCareer", "international account routes to career");
        report.AppendLine("TEST 5 settings/account list: PASS");
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
        report.AppendLine("TEST 6 app paths migration: PASS (settings/accounts/logs/ow/default move/custom preserved)");
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

    private static void Assert(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException("Assertion failed: " + message);
    }
}
