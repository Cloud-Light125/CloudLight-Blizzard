using System.IO;
using System.Text;

namespace CloudLightBlizzard.Services.OverwatchRegion;

internal static class SnapshotDeletionSelfTest
{
    internal static void Run(string workspace,StringBuilder report)
    {
        void Check(bool condition,string label)
        {
            if(!condition)throw new InvalidOperationException(label);
            report.AppendLine("PASS snapshots "+label);
        }
        var root=Path.Combine(workspace,"snapshot-deletion-isolated");
        var manager=new OverwatchRegionManager(root,()=>false,1,_=>false);
        var store=manager.BackupStore;
        var service=new SnapshotManagerService(manager);
        void Create(string id,RegionBackupState state=RegionBackupState.Ready)
        {
            store.SaveGeneration(new OverwatchRegionGeneration{GenerationId=id,State=state,BackupMode=RegionBackupMode.VerifiedDifference});
            var backups=store.BackupRoot(id,OverwatchRegion.China);Directory.CreateDirectory(backups);
            File.WriteAllText(Path.Combine(backups,"fixture.bin"),"isolated snapshot payload");
        }
        Create("active");Create("old");Create("expired",RegionBackupState.Stale);Create("cancelled");
        Directory.CreateDirectory(store.GenerationRoot("damaged"));
        File.WriteAllText(store.GenerationFile("damaged"),"{damaged");
        store.Activate("active",OverwatchRegion.China);
        var pointer=File.ReadAllBytes(store.ActiveGenerationFile);
        var sentinel=Path.Combine(workspace,"unrelated-game-sentinel.bin");File.WriteAllText(sentinel,"must survive");
        var blocked=false;try{store.DeleteGeneration("active");}catch(InvalidOperationException){blocked=true;}
        Check(blocked&&Directory.Exists(store.GenerationRoot("active"))&&pointer.SequenceEqual(File.ReadAllBytes(store.ActiveGenerationFile)),"active generation protected at storage boundary; pointer unchanged");
        var cancelBefore=File.ReadAllBytes(Path.Combine(store.BackupRoot("cancelled",OverwatchRegion.China),"fixture.bin"));
        Check(service.List().Any(x=>x.GenerationId=="cancelled")&&cancelBefore.SequenceEqual(File.ReadAllBytes(Path.Combine(store.BackupRoot("cancelled",OverwatchRegion.China),"fixture.bin"))),"cancelled confirmation performs no mutation");
        Check(service.Delete("old")&&!Directory.Exists(store.GenerationRoot("old"))&&!service.List().Any(x=>x.GenerationId=="old"),"ordinary snapshot removed on disk and absent after fresh List");
        Check(service.Delete("expired")&&!Directory.Exists(store.GenerationRoot("expired")),"expired non-active snapshot deleted safely");
        Check(!service.Delete("missing"),"missing directory has explicit false result");
        blocked=false;try{service.Delete("damaged");}catch(InvalidOperationException){blocked=true;}
        Check(blocked&&Directory.Exists(store.GenerationRoot("damaged")),"damaged unverified record rejected with actionable exception");
        Create("locked");
        var locked=Path.Combine(store.BackupRoot("locked",OverwatchRegion.China),"fixture.bin");
        using(var handle=new FileStream(locked,FileMode.Open,FileAccess.Read,FileShare.None))
        {
            blocked=false;try{service.Delete("locked");}catch(IOException){blocked=true;}
            Check(blocked&&Directory.Exists(store.GenerationRoot("locked")),"file lock raises deletion failure and never reports success");
        }
        foreach(var unsafeId in new[]{"../outside",@"..\outside",@"C:\game",".","..","bad/name","bad:name"})
        {
            blocked=false;try{service.Delete(unsafeId);}catch(InvalidDataException){blocked=true;}
            Check(blocked,"path traversal or absolute ID rejected: "+unsafeId);
        }
        // A real junction is created only within the isolated test tree.
        var external=Path.Combine(workspace,"isolated-link-target");Directory.CreateDirectory(external);
        var keep=Path.Combine(external,"keep.txt");File.WriteAllText(keep,"keep");
        var link=store.GenerationRoot("junction");
        using(var process=System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
        {
            FileName="cmd.exe",Arguments=$"/d /c mklink /J \"{link}\" \"{external}\"",
            CreateNoWindow=true,UseShellExecute=false,RedirectStandardOutput=true,RedirectStandardError=true
        }))
        {
            process!.WaitForExit();
            Check(process.ExitCode==0,"isolated junction fixture created");
        }
        blocked=false;try{service.Delete("junction");}catch(InvalidDataException){blocked=true;}
        Check(blocked&&File.ReadAllText(keep)=="keep","real junction deletion refused and target preserved");
        // Delete only the link itself, never follow it recursively.
        Directory.Delete(link);
        File.WriteAllText(store.ActiveGenerationFile,"{unreadable");
        blocked=false;try{service.Delete("cancelled");}catch(InvalidDataException){blocked=true;}
        Check(blocked&&Directory.Exists(store.GenerationRoot("cancelled")),"unreadable active pointer fails closed");
        File.WriteAllBytes(store.ActiveGenerationFile,pointer);
        Check(File.ReadAllText(sentinel)=="must survive"&&Directory.Exists(store.GenerationRoot("active")),"game sentinel, active and unrelated snapshots preserved");
        RunRetentionAsync(workspace, report).GetAwaiter().GetResult();
    }

    private static async Task RunRetentionAsync(string workspace, StringBuilder report)
    {
        void Check(bool condition, string label)
        {
            if (!condition) throw new InvalidOperationException(label);
            report.AppendLine("PASS snapshot retention " + label);
        }

        var directory = Path.Combine(workspace, "snapshot-retention");
        var game = Path.Combine(directory, "synthetic-game");
        Directory.CreateDirectory(game);
        var executable = Path.Combine(game, "Overwatch.exe");
        var difference = Path.Combine(game, "fixture.bin");
        File.WriteAllText(executable, "synthetic game executable");
        var disabled = new SnapshotCleanupPolicy(false, 2);
        var baseline = new OverwatchRegionManager(Path.Combine(directory, "baseline"), () => false, 0,
            cleanupPolicy: () => disabled);
        var policy = new SnapshotCleanupPolicy(true, 2);
        var manager = new OverwatchRegionManager(Path.Combine(directory, "managed"), () => false, 0,
            cleanupPolicy: () => policy);
        var store = manager.BackupStore;
        var service = new SnapshotManagerService(manager);

        async Task<string> Generate(OverwatchRegionManager target)
        {
            File.WriteAllBytes(difference, Enumerable.Repeat((byte)0x43, 512 * 1024).ToArray());
            await target.StartPreparationAsync(game, OverwatchRegion.China);
            File.WriteAllBytes(difference, Enumerable.Repeat((byte)0x49, 512 * 1024).ToArray());
            Check(await target.ContinuePreparationAsync(game) == RegionBackupState.Ready, "activated verified fixture");
            return target.BackupStore.LoadPointer()!.GenerationId;
        }
        static long Bytes(OverwatchRegionBackupStore target) =>
            OverwatchRegionBackupStore.EnumerateFilesWithoutReparse(target.GenerationsRoot).Sum(file => new FileInfo(file).Length);

        var produced = new List<string>();
        for (var index = 0; index < 6; index++)
        {
            await Generate(baseline);
            produced.Add(await Generate(manager));
        }
        var baselineStore = baseline.BackupStore;
        var beforeBytes = Bytes(baselineStore);
        var afterBytes = Bytes(store);
        Check(baselineStore.EnumerateGenerationIds().Count == 6 && store.EnumerateGenerationIds().Count == 4 &&
              afterBytes < beforeBytes, $"six runs disabled=6/{beforeBytes} bytes; enabled=4/{afterBytes} bytes; reclaimed={beforeBytes - afterBytes} bytes");
        Check(store.EnumerateGenerationIds().ToHashSet().SetEquals(produced.Skip(2)), "default keeps active, previous and two eligible history entries");

        var stable = store.EnumerateGenerationIds().ToHashSet();
        await manager.StartPreparationAsync(game, OverwatchRegion.China);
        var waiting = service.ScanCleanup(0);
        Check(waiting.BlockedReason?.Contains("未完成") == true && !waiting.ToDelete.Any(), "pending recovery protects all snapshots");
        File.Delete(executable);
        var failed = false;
        try { await manager.ContinuePreparationAsync(game); }
        catch (DirectoryNotFoundException) { failed = true; }
        Check(failed && stable.SetEquals(store.EnumerateGenerationIds()), "failed preparation never prunes existing generations");
        File.WriteAllText(executable, "synthetic game executable");
        manager.CancelPreparation();

        policy = new SnapshotCleanupPolicy(true, 0);
        File.WriteAllText(store.ActiveGenerationFile, "{broken");
        Check(service.ScanCleanup(0).BlockedReason?.Contains("指针") == true, "corrupt current pointer blocks cleanup before activation");
        var active = await Generate(manager);
        Check(store.EnumerateGenerationIds().Count == 5 && stable.All(id => store.EnumerateGenerationIds().Contains(id)),
            "repairing a corrupt pointer by activation does not prune the uncertain history");

        policy = new SnapshotCleanupPolicy(false, 0);
        var previous = active;
        active = await Generate(manager);
        Check(store.EnumerateGenerationIds().Count == 6, "disabling automatic cleanup keeps all prior snapshots");
        policy = new SnapshotCleanupPolicy(true, -1);
        previous = active;
        active = await Generate(manager);
        Check(store.EnumerateGenerationIds().Count == 7 && !service.ScanCleanup(-1).ToDelete.Any(), "unlimited policy leaves every generation and plans no removals");

        var pointer = store.LoadPointer()!;
        pointer.LastSuccessfulGenerationId = produced[^1];
        OverwatchRegionBackupStore.WriteJson(store.ActiveGenerationFile, pointer);
        var preview = service.ScanCleanup(0);
        Check(preview.ProtectedCount == 3 && preview.ToDelete.Count() == 4 &&
              !preview.ToDelete.Any(item => item.GenerationId == active || item.GenerationId == previous ||
                                            item.GenerationId == pointer.LastSuccessfulGenerationId),
            "active, previous and separate last-success pointer are protected outside the quota");
        using (manager.BackupStore.BeginRegionOperation())
        {
            var busy = false;
            try { service.Clean(preview, 0); }
            catch (InvalidOperationException) { busy = true; }
            Check(busy, "cleanup refuses a concurrent preparation or switch lease");
        }

        var savedPrevious = pointer.PreviousGenerationId;
        pointer.PreviousGenerationId = preview.ToDelete.First().GenerationId;
        OverwatchRegionBackupStore.WriteJson(store.ActiveGenerationFile, pointer);
        var stale = false;
        try { service.Clean(preview, 0); }
        catch (InvalidOperationException) { stale = true; }
        Check(stale && store.EnumerateGenerationIds().Count == 7, "confirmation cannot delete when protection changed after scanning");
        pointer.PreviousGenerationId = savedPrevious;
        OverwatchRegionBackupStore.WriteJson(store.ActiveGenerationFile, pointer);

        var beforeManual = Bytes(store);
        var removed = service.Clean(preview, 0);
        Check(removed.Removed == 4 && removed.Failures.Count == 0 && removed.Bytes == preview.ReclaimableBytes &&
              beforeManual - Bytes(store) == preview.ReclaimableBytes &&
              preview.ToDelete.All(item => !Directory.Exists(store.GenerationRoot(item.GenerationId))),
            $"manual preview matches four actual removals and {removed.Bytes} bytes reclaimed");

        var reopened = new OverwatchRegionManager(store.Root, () => false, 0, cleanupPolicy: () => policy);
        var switched = await reopened.SwitchGameRegionAsync(game, OverwatchRegion.China);
        Check(switched.Verified && File.ReadAllBytes(difference)[0] == 0x43 && reopened.HasActiveGeneration,
            "reopened store switches the synthetic game using the remaining verified snapshot");
        policy = new SnapshotCleanupPolicy(true, 1);
        var expectedPrevious = reopened.BackupStore.LoadPointer()!.GenerationId;
        var newActive = await Generate(reopened);
        var updated = reopened.BackupStore.EnumerateGenerationIds();
        Check(updated.Count == 3 && updated.Contains(newActive) && updated.Contains(expectedPrevious),
            "changed retention becomes active on the next successful preparation");
    }
}
