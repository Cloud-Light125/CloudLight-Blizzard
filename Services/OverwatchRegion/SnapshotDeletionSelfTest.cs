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
    }
}
