using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace CloudLightBlizzard.Services;

/// <summary>
/// AppDataStore 的离线安全回归测试。调用方应传入专用临时 workspace；
/// 本测试只使用模拟的 BattleNetPaths，不启动 Battle.net，也不接触真实用户目录。
/// </summary>
internal static class AccountStoreSafetySelfTest
{
    public static void Run(string workspace, StringBuilder report)
    {
        var root = Path.Combine(Path.GetFullPath(workspace), "account-store-safety-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            RunValidNewManifest(root);
            RunValidLegacyManifest(root);
            RunInvalidManifests(root);
            RunMutationRollback(root);
            RunPreparationFailures(root, report);
            RunReadFailure(root);
            RunFailedRollbackJournal(root);
            RunStagingReadFailure(root);
            RunReparseRejection(root, report);
            RunPointerClear(root);
            RunSaveFailures(root);
            RunDiscoveryAndMigration(root);
            report.AppendLine("TEST issue #3 save/discovery/migration/UI state: PASS (empty source, locks, staging/manifest/meta/validation/promotion failures, old snapshot preserved, cleanup pending, legacy/current isolation, journals retained)");
            report.AppendLine("TEST account store safety: PASS (strict manifest preflight, legacy compatibility, "
                              + "path/type/size/hash/managed/excluded validation, write-failure rollback)");
        }
        finally
        {
            AppDataStore.RestoreMutationFailureForSelfTest = null;
            AppDataStore.RestorePreparationFailureForSelfTest = null;
            AppDataStore.SaveFailureForSelfTest = null;
            try { if (Directory.Exists(root)) Directory.Delete(root, true); } catch { }
        }
    }

    private static void RunValidNewManifest(string root)
    {
        var test = CreateSandbox(root, "new");
        File.WriteAllText(Path.Combine(test.Roaming, "common.config"), "new-source");
        Directory.CreateDirectory(Path.Combine(test.Roaming, "nested"));
        File.WriteAllText(Path.Combine(test.Roaming, "nested", "settings.json"), "new-nested");
        Directory.CreateDirectory(Path.Combine(test.Roaming, "Overlay", "logs"));
        File.WriteAllText(Path.Combine(test.Roaming, "Overlay", "logs", "runtime.log"), "live-only");
        test.Store.Save(1, "New#1");
        // A historical v1 writer recorded Overlay but did not create empty
        // backup directories. This must remain compatible.
        Directory.Delete(Path.Combine(test.Profiles, "1", "BattleNet", "Overlay"));

        File.WriteAllText(Path.Combine(test.Roaming, "common.config"), "changed");
        File.WriteAllText(Path.Combine(test.Roaming, "nested", "settings.json"), "changed-nested");
        test.Store.ValidateProfile(1);
        test.Store.Restore(1);

        Assert(File.ReadAllText(Path.Combine(test.Roaming, "common.config")) == "new-source",
            "valid new manifest restores file");
        Assert(File.ReadAllText(Path.Combine(test.Roaming, "nested", "settings.json")) == "new-nested",
            "valid new manifest restores nested file");
        Assert(File.ReadAllText(Path.Combine(test.Roaming, "Overlay", "logs", "runtime.log")) == "live-only",
            "excluded live file remains untouched");
        Assert(File.Exists(Path.Combine(test.Profiles, "1", "manifest.json")), "new manifest remains present");
        var legacyFields = Path.Combine(test.Profiles, "1", "manifest.json");
        var json = System.Text.Json.Nodes.JsonNode.Parse(File.ReadAllText(legacyFields))!;
        json.AsObject().Remove("ManagedPaths");
        json.AsObject().Remove("ExcludedPaths");
        File.WriteAllText(legacyFields, json.ToJsonString());
        test.Store.Restore(1);
    }

    private static void RunValidLegacyManifest(string root)
    {
        var test = CreateSandbox(root, "legacy");
        File.WriteAllText(Path.Combine(test.Roaming, "legacy.config"), "legacy-source");
        test.Store.Save(2, "Legacy#2");
        File.Delete(Path.Combine(test.Profiles, "2", "manifest.json"));
        File.WriteAllText(Path.Combine(test.Roaming, "legacy.config"), "changed");
        var protectedFixture = Path.Combine(test.Profiles, "2", "BattleNet", "UnifiedAuth", "fixture.txt");
        Directory.CreateDirectory(Path.GetDirectoryName(protectedFixture)!);
        File.WriteAllText(protectedFixture, "excluded fixture");
        using var protectedHeld = new FileStream(protectedFixture, FileMode.Open, FileAccess.Read, FileShare.None);

        test.Store.ValidateProfile(2);
        test.Store.Restore(2);
        Assert(File.ReadAllText(Path.Combine(test.Roaming, "legacy.config")) == "legacy-source",
            "manifest absence alone permits legacy restore");
        Assert(!Directory.Exists(Path.Combine(test.Roaming, "UnifiedAuth")), "legacy validation/restoration never reads or restores excluded authentication leftovers");
    }

    private static void RunInvalidManifests(string root)
    {
        ExpectRejected(root, "truncated", "{\"Version\":1,\"Entries\":[");
        ExpectRejected(root, "null", "null");
        ExpectRejected(root, "missing-entries", "{\"Version\":1,\"ManagedPaths\":[]}");
        ExpectRejected(root, "illegal-structure", "{\"Version\":1,\"Entries\":[null],\"ManagedPaths\":[]}");
        ExpectRejected(root, "unsupported-version", CreateManifestJson(version: 2));
        ExpectRejected(root, "unsafe-path", CreateManifestJson(relativePath: "../good.config"));
        ExpectRejected(root, "duplicate-case", CreateManifestJson(
            entries: new[]
            {
                EntryJson("good.config", 6, Hash("backup")),
                EntryJson("GOOD.CONFIG", 6, Hash("backup")),
            },
            managedPaths: new[] { "good.config", "GOOD.CONFIG" }));
        ExpectRejected(root, "file-dir-conflict", CreateManifestJson(
            entries: new[]
            {
                EntryJson("good.config", 6, null, isDirectory: false),
                EntryJson("good.config", 0, null, isDirectory: true),
            },
            managedPaths: new[] { "good.config" }));
        ExpectRejected(root, "missing-file", CreateManifestJson(relativePath: "missing.config", size: 0, hash: null,
            managedPaths: new[] { "missing.config" }));
        ExpectRejected(root, "size-mismatch", CreateManifestJson(size: 999));
        ExpectRejected(root, "negative-size", CreateManifestJson(size: -1));
        ExpectRejected(root, "entries-null", "{\"Entries\":null}");
        ExpectRejected(root, "entries-object", "{\"Entries\":{}}");
        ExpectRejected(root, "absolute-path", CreateManifestJson(relativePath: "C:/escape"));
        ExpectRejected(root, "normalization-path", CreateManifestJson(relativePath: "nested/../good.config"));
        ExpectRejected(root, "hash-mismatch", CreateManifestJson(hash: new string('0', 64)));
        ExpectRejected(root, "managed-mismatch", CreateManifestJson(managedPaths: Array.Empty<string>()));
        ExpectRejected(root, "excluded-mismatch", CreateManifestJson(excludedPaths: new[] { "good.config" }));
    }

    private static void ExpectRejected(string root, string name, string manifestText)
    {
        var test = CreateSandbox(root, "invalid-" + name);
        File.WriteAllText(Path.Combine(test.Roaming, "good.config"), "backup");
        test.Store.Save(3, "Invalid#3");
        File.WriteAllText(Path.Combine(test.Roaming, "good.config"), "target-before");
        File.WriteAllText(Path.Combine(test.Roaming, "unmanaged.txt"), "must-remain");
        var before = CaptureFiles(test.Roaming);
        File.WriteAllText(Path.Combine(test.Profiles, "3", "manifest.json"), manifestText);

        var rejected = false;
        try { test.Store.Restore(3); }
        catch (Exception) { rejected = true; }
        Assert(rejected, name + " manifest is rejected");
        AssertFilesEqual(before, CaptureFiles(test.Roaming), name + " rejection leaves target bytes unchanged");
    }

    private static void RunMutationRollback(string root)
    {
        var test = CreateSandbox(root, "rollback");
        File.WriteAllText(Path.Combine(test.Roaming, "a.config"), "source-a");
        File.WriteAllText(Path.Combine(test.Roaming, "b.config"), "source-b");
        test.Store.Save(4, "Rollback#4");
        File.WriteAllText(Path.Combine(test.Roaming, "a.config"), "target-a");
        File.WriteAllText(Path.Combine(test.Roaming, "b.config"), "target-b");
        var before = CaptureFiles(test.Roaming);
        var injected = false;
        AppDataStore.RestoreMutationFailureForSelfTest = path =>
        {
            if (path.EndsWith("b.config", StringComparison.OrdinalIgnoreCase))
            {
                injected = true;
                return new IOException("self-test injected write failure");
            }
            return null;
        };
        try
        {
            var rejected = false;
            try { test.Store.Restore(4); }
            catch (IOException) { rejected = true; }
            Assert(rejected && injected, "write failure is surfaced");
        }
        finally
        {
            AppDataStore.RestoreMutationFailureForSelfTest = null;
        }
        AssertFilesEqual(before, CaptureFiles(test.Roaming), "write failure rolls back affected files");
    }

    private static void RunPreparationFailures(string root, StringBuilder report)
    {
        foreach (var stage in new[] { "create-directory", "snapshot-1", "snapshot-2", "journal", "staging", "applying-journal", "cleanup" })
        {
            var test = CreateSandbox(root, "preparation-" + stage);
            File.WriteAllText(Path.Combine(test.Roaming, "a.config"), "backup-a");
            File.WriteAllText(Path.Combine(test.Roaming, "b.config"), "backup-b");
            test.Store.Save(9, "Fixture#9");
            File.WriteAllText(Path.Combine(test.Roaming, "a.config"), "original-a");
            File.WriteAllText(Path.Combine(test.Roaming, "b.config"), "original-b");
            var before = CaptureFiles(test.Roaming);
            var backup = CaptureFiles(test.Profiles);
            var injected = false;
            AppDataStore.RestorePreparationFailureForSelfTest = current =>
            {
                if (current == stage || (stage == "cleanup" && current == "staging"))
                {
                    injected = true;
                    return new IOException("isolated preparation fault: " + current);
                }
                return null;
            };
            Exception? failure = null;
            try { test.Store.Restore(9); } catch (IOException ex) { failure = ex; }
            finally { AppDataStore.RestorePreparationFailureForSelfTest = null; }
            Assert(injected && failure is not null, stage + " failure surfaced");
            AssertFilesEqual(before, CaptureFiles(test.Roaming), stage + " target byte-for-byte unchanged");
            AssertFilesEqual(backup, CaptureFiles(test.Profiles), stage + " backup byte-for-byte unchanged");
            var recovery = test.Profiles + ".recovery";
            if (stage == "cleanup")
            {
                var own = Directory.GetDirectories(recovery).Single();
                var evidence = CaptureFiles(own);
                Assert(failure!.Message.Contains(own) && failure.InnerException is AggregateException,
                    "preparation cleanup failure exposes retained exact path and both errors");
                var rejected = false;
                try { test.Store.Restore(9); } catch (IOException ex) { rejected = ex.Message.Contains("未完成"); }
                Assert(rejected, "failed preparation cleanup blocks further restore");
                AssertFilesEqual(evidence, CaptureFiles(own), "old pending preparation never deleted");
                AssertFilesEqual(before, CaptureFiles(test.Roaming), "old pending preparation blocks before mutation");
            }
            else
            {
                Assert(!Directory.EnumerateFileSystemEntries(recovery).Any(), stage + " owned preparation directory removed");
                test.Store.Restore(9);
                Assert(File.ReadAllText(Path.Combine(test.Roaming, "a.config")) == "backup-a", stage + " subsequent legal restore works");
                Assert(!Directory.EnumerateFileSystemEntries(recovery).Any(), "successful restore cleans journal");
            }
            report.AppendLine("PASS journal " + stage + ": target/backup bytes preserved; preparation cleanup/retry or retained fence verified");
        }
    }

    private static void RunReadFailure(string root)
    {
        var test = CreateSandbox(root, "locked-manifest");
        File.WriteAllText(Path.Combine(test.Roaming, "a.config"), "before");
        test.Store.Save(5, "Fixture#5");
        var before = CaptureFiles(test.Roaming);
        using var held = new FileStream(Path.Combine(test.Profiles, "5", "manifest.json"), FileMode.Open, FileAccess.Read, FileShare.None);
        var rejected = false;
        try { test.Store.Restore(5); } catch (IOException) { rejected = true; }
        Assert(rejected, "unreadable manifest rejected, never legacy");
        AssertFilesEqual(before, CaptureFiles(test.Roaming), "locked manifest leaves target unchanged");
    }

    private static void RunFailedRollbackJournal(string root)
    {
        var test = CreateSandbox(root, "failed-rollback");
        File.WriteAllText(Path.Combine(test.Roaming, "a.config"), "source-a");
        File.WriteAllText(Path.Combine(test.Roaming, "b.config"), "source-b");
        test.Store.Save(6, "Fixture#6");
        File.WriteAllText(Path.Combine(test.Roaming, "a.config"), "original-a");
        FileStream? held = null;
        AppDataStore.RestoreMutationFailureForSelfTest = path =>
        {
            if (!path.EndsWith("b.config", StringComparison.OrdinalIgnoreCase)) return null;
            held = new FileStream(Path.Combine(test.Roaming, "a.config"), FileMode.Open, FileAccess.Read, FileShare.Read);
            return new IOException("fixture IO error after first overwrite");
        };
        string error = "";
        try { test.Store.Restore(6); } catch (IOException ex) { error = ex.Message; }
        finally { held?.Dispose(); AppDataStore.RestoreMutationFailureForSelfTest = null; }
        Assert(error.Contains("回滚也失败"), "rollback failure explicitly reported");
        var journal = Directory.GetFiles(test.Profiles + ".recovery", "journal.json", SearchOption.AllDirectories).Single();
        using var document = JsonDocument.Parse(File.ReadAllText(journal));
        var original = document.RootElement.GetProperty("Files").EnumerateArray()
            .Single(item => item.GetProperty("RelativePath").GetString() == "a.config");
        var snapshot = Path.Combine(Path.GetDirectoryName(journal)!, "snapshots", original.GetProperty("SnapshotFile").GetString()!);
        Assert(File.ReadAllText(snapshot) == "original-a", "durable original bytes and mapping survive failed rollback");
        Assert(File.Exists(Path.Combine(Path.GetDirectoryName(journal)!, "failure.json")), "separate failure evidence preserved");
        var before = CaptureFiles(test.Roaming);
        var blocked = false;
        try { test.Store.Restore(6); } catch (IOException ex) { blocked = ex.Message.Contains("未完成"); }
        Assert(blocked, "pending recovery blocks subsequent restores");
        AssertFilesEqual(before, CaptureFiles(test.Roaming), "pending journal blocks writes");
    }

    private static void RunStagingReadFailure(string root)
    {
        var test = CreateSandbox(root, "staging-read");
        File.WriteAllText(Path.Combine(test.Roaming, "good.config"), "backup");
        test.Store.Save(7, "Fixture#7");
        File.WriteAllText(Path.Combine(test.Profiles, "7", "manifest.json"), CreateManifestJson()); // no hash
        File.WriteAllText(Path.Combine(test.Roaming, "good.config"), "original");
        var before = CaptureFiles(test.Roaming);
        using var held = new FileStream(Path.Combine(test.Profiles, "7", "BattleNet", "good.config"), FileMode.Open, FileAccess.Read, FileShare.None);
        var rejected = false;
        try { test.Store.Restore(7); } catch (IOException) { rejected = true; }
        Assert(rejected, "unhashed unreadable source rejected during staging");
        AssertFilesEqual(before, CaptureFiles(test.Roaming), "source read failure precedes target writes");
    }

    private static void RunReparseRejection(string root, StringBuilder report)
    {
        var test = CreateSandbox(root, "reparse");
        File.WriteAllText(Path.Combine(test.Roaming, "good.config"), "backup");
        test.Store.Save(8, "Fixture#8");
        File.WriteAllText(Path.Combine(test.Roaming, "good.config"), "original");
        var before = CaptureFiles(test.Roaming);
        var link = Path.Combine(test.Profiles, "8", "BattleNet", "redirect");
        try { Directory.CreateSymbolicLink(link, test.Roaming); }
        catch (UnauthorizedAccessException)
        {
            report.AppendLine("BLOCKED reparse fixture: Windows symbolic-link privilege unavailable");
            return;
        }
        try
        {
            var rejected = false;
            try { test.Store.Restore(8); } catch (IOException) { rejected = true; }
            Assert(rejected, "source reparse point rejected");
            AssertFilesEqual(before, CaptureFiles(test.Roaming), "reparse rejection leaves target unchanged");
            report.AppendLine("PASS source reparse point rejected without following target");
        }
        finally { Directory.Delete(link); }
    }

    private static void RunPointerClear(string root)
    {
        var test = CreateSandbox(root, "pointer-clear");
        var path = Path.Combine(test.Roaming, "Battle.net.config");
        var original = "{\"SavedAccountNames\":\"fixture\",\"Other\":\"preserve\"}";
        File.WriteAllText(path, original);
        using (var held = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
        {
            var rejected = false;
            try { test.Store.ClearCurrentPointer(); } catch (IOException) { rejected = true; }
            Assert(rejected && File.ReadAllText(path) == original, "locked pointer replacement leaves bytes intact");
        }
        test.Store.ClearCurrentPointer();
        Assert(File.ReadAllText(path) == "{\"SavedAccountNames\":\"\",\"Other\":\"preserve\"}", "atomic pointer clear preserves unrelated fields");
        Assert(!Directory.GetFiles(test.Roaming, "*.tmp").Any(), "pointer staging released");
    }

    private static Sandbox CreateSandbox(string root, string name)
    {
        var sandbox = Path.Combine(root, name + "-" + Guid.NewGuid().ToString("N"));
        var roaming = Path.Combine(sandbox, "roaming");
        var local = Path.Combine(sandbox, "local");
        var profiles = Path.Combine(sandbox, "profiles");
        Directory.CreateDirectory(roaming);
        Directory.CreateDirectory(local);
        var paths = new BattleNetPaths(local, roaming);
        return new Sandbox(roaming, profiles, new AppDataStore(paths, profiles));
    }

    private static void RunSaveFailures(string root)
    {
        var test = CreateSandbox(root, "save-failures");
        var source = Path.Combine(test.Roaming, "Battle.net.config");
        File.WriteAllText(source, "original-valid-backup");
        test.Store.Save(51, "Fixture#51");
        var before = CaptureFiles(Path.Combine(test.Profiles, "51"));
        foreach (var stage in new[] { "创建 staging", "复制账号配置", "写入 manifest", "写入 meta", "验证 staging", "提升正式快照", "移动 staging" })
        {
            AppDataStore.SaveFailureForSelfTest = (current, _) => current == stage
                ? new UnauthorizedAccessException("injected denied write") : null;
            try
            {
                var rejected = false;
                try { test.Store.Save(51, "Fixture#51"); }
                catch (IOException ex) { rejected = ex.Message.Contains(stage) && ex.Message.Contains(test.Roaming) && ex.Message.Contains(test.Profiles); }
                Assert(rejected, "save rejected with operation and paths: " + stage);
                AssertFilesEqual(before, CaptureFiles(Path.Combine(test.Profiles, "51")), "failed save preserves old snapshot");
                Assert(test.Store.InspectProfile(51).CanRestore, "previous snapshot still usable");
            }
            finally { AppDataStore.SaveFailureForSelfTest = null; }
        }
        foreach (var corrupt in new[] { "BattleNet/Battle.net.config", "manifest.json", "meta.json" })
        {
            AppDataStore.SaveFailureForSelfTest = (stage, staging) =>
            {
                if (stage == "验证 staging") File.WriteAllText(Path.Combine(staging, corrupt), "damaged");
                return null;
            };
            try
            {
                var rejected = false;
                try { test.Store.Save(51, "Fixture#51"); } catch (IOException) { rejected = true; }
                Assert(rejected, "corrupt staging cannot be committed: " + corrupt);
                AssertFilesEqual(before, CaptureFiles(Path.Combine(test.Profiles, "51")), "staging validation precedes old snapshot move");
            }
            finally { AppDataStore.SaveFailureForSelfTest = null; }
        }
        using (var held = new FileStream(source, FileMode.Open, FileAccess.Read, FileShare.None))
        {
            var rejected = false;
            try { test.Store.Save(51, "Fixture#51"); } catch (IOException) { rejected = true; }
            Assert(rejected, "locked source cannot report success");
        }
        File.Delete(source);
        Directory.CreateDirectory(Path.Combine(test.Roaming, "Logs"));
        File.WriteAllText(Path.Combine(test.Roaming, "Logs", "ignored.log"), "ignored");
        var emptyRejected = false;
        try { test.Store.Save(51, "Fixture#51"); } catch (IOException) { emptyRejected = true; }
        Assert(emptyRejected, "empty/only-excluded source rejected");
        AssertFilesEqual(before, CaptureFiles(Path.Combine(test.Profiles, "51")), "empty source preserves old backup");
        Directory.Delete(test.Roaming, true);
        var missingRejected = false;
        try { test.Store.Save(51, "Fixture#51"); } catch (IOException) { missingRejected = true; }
        Assert(missingRejected, "missing source rejected");
        Directory.CreateDirectory(test.Roaming);
        File.WriteAllText(source, "new-valid-backup");
        AppDataStore.SaveFailureForSelfTest = (stage, _) => stage == "清理旧快照" ? new IOException("cleanup locked") : null;
        try { test.Store.Save(51, "Fixture#51"); }
        finally { AppDataStore.SaveFailureForSelfTest = null; }
        Assert(test.Store.InspectProfile(51).CanRestore, "cleanup failure is still a committed valid save");
        Assert(Directory.GetDirectories(test.Profiles, "51.previous-*").Length == 1, "cleanup failure preserves previous snapshot");
        Assert(Directory.GetDirectories(test.Profiles, "*.staging-*").Length == 0, "failed staging cleaned");
        test.Store.Save(51, "Fixture#51", expired: true);
        Assert(test.Store.InspectProfile(51).Meta?.Expired == true, "expiry reminder survives ordinary snapshot persistence and refresh");
        var unavailable = CreateSandbox(root, "unwritable-destination");
        File.WriteAllText(Path.Combine(unavailable.Roaming, "Battle.net.config"), "fixture");
        File.WriteAllText(unavailable.Profiles, "path is a file");
        var targetRejected = false;
        try { unavailable.Store.Save(52, "Fixture#52"); } catch (IOException ex) { targetRejected = ex.Message.Contains(unavailable.Profiles); }
        Assert(targetRejected && unavailable.Store.InspectProfile(52).State == AccountProfileState.Unavailable,
            "unavailable target is actionable, never unsaved/success");
        var journal = Path.Combine(test.Profiles + ".recovery", "pending", "journal.json");
        Directory.CreateDirectory(Path.GetDirectoryName(journal)!);
        File.WriteAllText(journal, "retained recovery evidence");
        var validBeforeJournal = CaptureFiles(Path.Combine(test.Profiles, "51"));
        var journalRejected = false;
        try { test.Store.Save(51, "Fixture#51"); } catch (IOException) { journalRejected = true; }
        Assert(journalRejected && File.ReadAllText(journal) == "retained recovery evidence", "pending restore journal also fences new saves");
        AssertFilesEqual(validBeforeJournal, CaptureFiles(Path.Combine(test.Profiles, "51")), "pending journal leaves backup untouched");
    }

    private static void RunDiscoveryAndMigration(string root)
    {
        var missingRoot = CreateSandbox(root, "missing-root");
        var missingRootRejected = false;
        try { _ = missingRoot.Store.ReadProfileIds(); }
        catch (IOException ex) { missingRootRejected = ex.Message.Contains(missingRoot.Profiles); }
        Assert(missingRootRejected, "missing backup root reports failure instead of an empty profile list");
        var test = CreateSandbox(root, "discovery");
        File.WriteAllText(Path.Combine(test.Roaming, "Battle.net.config"), "fixture");
        test.Store.Save(61, "Fixture#61");
        test.Store.Save(62, "Fixture#62");
        Directory.Move(Path.Combine(test.Profiles, "62"), Path.Combine(test.Profiles, "62.staging-interrupted"));
        Assert(test.Store.ReadAllMeta().Select(meta => meta.AccountId).SequenceEqual(new long[] { 61 }), "staging metadata cannot invent a saved account");
        var profile = Path.Combine(test.Profiles, "61");
        File.WriteAllText(Path.Combine(profile, "manifest.json"), "broken");
        Assert(test.Store.InspectProfile(61).State == AccountProfileState.Corrupt && !test.Store.HasProfile(61), "corrupt manifest displayed, never legacy");
        Assert(test.Store.ReadProfileIds().Contains(61), "broken profile remains discoverable");
        File.Delete(Path.Combine(profile, "manifest.json"));
        Assert(test.Store.InspectProfile(61).State == AccountProfileState.Legacy, "valid legacy accepted");
        var metaFile = Path.Combine(profile, "meta.json");
        var correctMeta = File.ReadAllText(metaFile);
        File.WriteAllText(metaFile, JsonSerializer.Serialize(new AccountProfileMeta(999, "WrongFixture#999", DateTime.UtcNow)));
        Assert(test.Store.InspectProfile(61).State == AccountProfileState.Corrupt && !test.Store.HasProfile(61), "metadata ID mismatch never becomes a valid profile");
        File.WriteAllText(metaFile, correctMeta);
        test.Store.Save(63, "Fixture#63");
        var oldRoot = Path.Combine(root, "migration-old");
        var newRoot = Path.Combine(root, "migration-new");
        Directory.CreateDirectory(oldRoot);
        Directory.Move(test.Profiles, Path.Combine(oldRoot, "accounts"));
        var protectedFile = Path.Combine(oldRoot, "accounts", "63", "BattleNet", "UnifiedAuth", "fixture.txt");
        Directory.CreateDirectory(Path.GetDirectoryName(protectedFile)!);
        File.WriteAllText(protectedFile, "excluded fixture; never read or migrated");
        var paths = new AppPaths(newRoot, oldRoot);
        using (var held = new FileStream(protectedFile, FileMode.Open, FileAccess.Read, FileShare.None))
            paths.MigrateLegacyAccounts();
        Assert(Directory.Exists(Path.Combine(paths.AccountsDir, "63")) &&
               !File.Exists(Path.Combine(paths.AccountsDir, "63", "BattleNet", "UnifiedAuth", "fixture.txt")),
            "migration neither reads nor copies undeclared protected authentication leftovers");
        var destination = Path.Combine(paths.AccountsDir, "61");
        Assert(Directory.Exists(destination) && Directory.Exists(Path.Combine(oldRoot, "accounts", "61")), "legacy discovered and copied, source retained");
        Assert(!Directory.Exists(Path.Combine(paths.AccountsDir, "62.staging-interrupted")), "migration ignores staging");
        _ = AppDataStore.ValidateSnapshotDirectory(destination, 61);
        var before = CaptureFiles(destination);
        File.WriteAllText(Path.Combine(oldRoot, "accounts", "61", "BattleNet", "Battle.net.config"), "changed-old");
        paths.MigrateLegacyAccounts();
        AssertFilesEqual(before, CaptureFiles(destination), "existing destination never merged with legacy");
        File.WriteAllText(Path.Combine(destination, "manifest.json"), "broken");
        paths.MigrateLegacyAccounts();
        Assert(paths.AccountMigrationWarnings.Count > 0 && File.ReadAllText(Path.Combine(destination, "manifest.json")) == "broken", "corrupt destination preserved with warning");
        Directory.CreateDirectory(Path.Combine(oldRoot, "accounts.recovery", "pending"));
        var journal = Path.Combine(oldRoot, "accounts.recovery", "pending", "journal.json");
        File.WriteAllText(journal, "evidence");
        var blocked = new AppPaths(Path.Combine(root, "migration-blocked"), oldRoot);
        blocked.MigrateLegacyAccounts();
        Assert(blocked.AccountMigrationWarnings.Count > 0 && !Directory.Exists(blocked.AccountsDir) && File.ReadAllText(journal) == "evidence", "pending legacy journal blocks migration and remains intact");

        var defaultStore = new AppDataStore(new BattleNetPaths(Path.Combine(root, "local-default"), test.Roaming));
        var region = Path.Combine(root, "custom-region");
        var settings = new AppSettings { RegionStoragePath = region };
        defaultStore.Save(991, "Fixture#991");
        Assert(defaultStore.Root == AppPaths.Current.AccountsDir && !Directory.Exists(settings.RegionStoragePath), "custom region path never redirects default account snapshots");

        var missed = Path.Combine(AppPaths.Current.LegacyRoot, "accounts", "701");
        Directory.CreateDirectory(Path.Combine(missed, "BattleNet"));
        File.WriteAllText(Path.Combine(missed, "BattleNet", "Battle.net.config"), "missed old account");
        File.WriteAllText(Path.Combine(missed, "meta.json"), JsonSerializer.Serialize(new AccountProfileMeta(701, "Fixture#701", DateTime.UtcNow)));
        File.WriteAllText(AppPaths.Current.SettingsFile, JsonSerializer.Serialize(new AppSettings { MigrationCompleted = true }));
        _ = AppSettings.Load();
        Assert(defaultStore.InspectProfile(701).CanRestore && Directory.Exists(missed), "completed settings migration flag does not hide a missed legacy account");

        var missing = new CloudLightBlizzard.ViewModels.AccountRow { AccountId = 63, IsActive = true };
        Assert(missing.CanSwitch && missing.SwitchText == "重新登录", "missing profile has actionable relogin even if local pointer is current");
        var damaged = new CloudLightBlizzard.ViewModels.AccountRow { AccountId = 61, ProfileStatus = new(AccountProfileState.Corrupt, null, "fixture corruption") };
        Assert(damaged.HasSnapshot && !damaged.HasProfile && damaged.ProfileText == "备份损坏", "corrupt profile not mislabeled saved or missing");
        var row = new CloudLightBlizzard.ViewModels.AccountRow { HasProfile = true, IsActive = true };
        row.IsSaving = true;
        Assert(row.ProfileText == "正在保存…", "saving state visible");
        row.IsSaving = false;
        row.SaveError = "fixture write failure";
        Assert(row.ProfileText.StartsWith("保存失败") && row.HasProfile, "save failure retains previous valid profile state");
        row.SaveError = null;
        row.IsExpired = true;
        Assert(row.CanSwitch && row.SwitchText == "重新登录" && row.ProfileText.Contains("需要更新"), "expired current account has relogin/update action");
    }

    private static string CreateManifestJson(
        int version = 1,
        string relativePath = "good.config",
        long size = 6,
        string? hash = null,
        IReadOnlyList<string>? managedPaths = null,
        IReadOnlyList<string>? excludedPaths = null,
        IReadOnlyList<object>? entries = null)
    {
        var payload = new
        {
            Version = version,
            CreatedAtUtc = DateTime.UtcNow,
            Entries = entries ?? new[] { EntryJson(relativePath, size, hash) },
            ManagedPaths = managedPaths ?? new[] { relativePath },
            ExcludedPaths = excludedPaths ?? Array.Empty<string>(),
        };
        return JsonSerializer.Serialize(payload);
    }

    private static object EntryJson(string path, long size, string? hash, bool isDirectory = false) =>
        new
        {
            RelativePath = path,
            Size = size,
            LastWriteTimeUtc = DateTime.UtcNow,
            IsDirectory = isDirectory,
            Sha256 = hash,
        };

    private static string Hash(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));

    private static Dictionary<string, byte[]> CaptureFiles(string root) =>
        Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories).ToDictionary(
            path => Path.GetRelativePath(root, path).Replace('\\', '/'),
            File.ReadAllBytes,
            StringComparer.OrdinalIgnoreCase);

    private static void AssertFilesEqual(
        IReadOnlyDictionary<string, byte[]> expected, IReadOnlyDictionary<string, byte[]> actual, string message)
    {
        Assert(expected.Keys.OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
                  .SequenceEqual(actual.Keys.OrderBy(path => path, StringComparer.OrdinalIgnoreCase),
                      StringComparer.OrdinalIgnoreCase), message + " file list");
        foreach (var pair in expected)
            Assert(actual[pair.Key].AsSpan().SequenceEqual(pair.Value), message + ": " + pair.Key);
    }

    private static void Assert(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException("AccountStoreSafetySelfTest: " + message);
    }

    private sealed record Sandbox(string Roaming, string Profiles, AppDataStore Store);
}
