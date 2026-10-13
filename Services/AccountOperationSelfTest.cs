using System.IO;
using System.Text;
using CloudLightBlizzard.Services.OverwatchRegion;

using GameRegion = CloudLightBlizzard.Services.OverwatchRegion.OverwatchRegion;

namespace CloudLightBlizzard.Services;

internal static class AccountOperationSelfTest
{
    internal static async Task RunAsync(string root, StringBuilder report)
    {
        Directory.CreateDirectory(root);
        void Check(bool value, string text) { if (!value) throw new InvalidOperationException(text); report.AppendLine("PASS " + text); }
        async Task Rejected(Task task, string label)
        {
            try { await task; } catch (Exception) { report.AppendLine("PASS " + label); return; }
            throw new InvalidOperationException(label + " unexpectedly accepted");
        }
        var calls = new System.Collections.Concurrent.ConcurrentQueue<string>();
        TaskCompletionSource? precheck = null;
        TaskCompletionSource? restore = null;
        var failure = "";
        var game = false;
        var unavailable = false;
        var partial = false;
        var lockPath = Path.Combine(root, "operation.lock");
        AccountOperationService Service() => new(new AccountOperationGate(lockPath), new AccountOperationBackend
        {
            ResolveTarget = id => id == 99 ? throw new InvalidDataException("missing target") : new(id, "fixture", GameRegion.International),
            ValidateProfile = _ => failure == "manifest" ? Task.FromException(new InvalidDataException("corrupt manifest")) : Task.CompletedTask,
            ReadActive = async () => { if (precheck is not null) await precheck.Task; return 1; },
            GameRunning = () => game,
            RegionStatus = () => failure == "precheck" ? Task.FromException<RegionSnapshotStatus>(new IOException("fixture")) :
                Task.FromResult(new RegionSnapshotStatus { GamePathValid = !unavailable, SwitchEligibility = RegionSwitchEligibility.Normal }),
            Quit = () => { calls.Enqueue("quit"); return Task.FromResult(failure != "quit"); },
            Normalize = (_, _) =>
            {
                calls.Enqueue("region");
                return Task.FromResult(new RegionSwitchResult(1, 0, Outcome: failure == "region" ? RegionSwitchOutcome.Failed :
                    partial ? RegionSwitchOutcome.PartialSuccess : RegionSwitchOutcome.Success, FailedCount: failure == "region" || partial ? 1 : 0));
            },
            Restore = async _ => { calls.Enqueue("restore"); if (restore is not null) await restore.Task; if (failure == "restore") throw new IOException("fixture"); },
            ClearPointer = () => { calls.Enqueue("clear"); return Task.CompletedTask; },
            Launch = () => { calls.Enqueue("launch"); return Task.CompletedTask; },
        }, new AccountSwitchLog(Path.Combine(root, "switch.log")));

        var gui = Service();
        var cli = Service(); // Independent gate instance, same cross-process file lease path.
        precheck = new(TaskCreationOptions.RunContinuationsAsynchronously);
        var first = gui.SwitchAsync(2);
        await Rejected(gui.SwitchAsync(2), "same-account rapid second request rejected during precheck");
        await Rejected(gui.SwitchAsync(3), "different-account rapid request rejected during precheck");
        await Rejected(cli.SwitchAsync(3), "CLI/GUI independent gates cannot overlap");
        await Rejected(cli.AddAccountAsync(), "CLI addaccount cannot bypass GUI lease");
        await Rejected(gui.RunExclusiveAsync(_ => { calls.Enqueue("save"); return Task.CompletedTask; }), "save/delete/relogin share lease");
        Check(calls.Count == 0, "no destructive call during held precheck");
        Check(await ProbeGateAsync(lockPath) == 2, "separate OS process cannot acquire held account lease");
        precheck.SetResult();
        await first;
        Check(await ProbeGateAsync(lockPath) == 0, "separate OS process acquires released account lease");
        precheck = null;
        Check(calls.SequenceEqual(new[] { "quit", "region", "restore", "launch" }), "only first switch reaches mutation pipeline, in order");

        foreach (var stage in new[] { "manifest", "quit", "region", "restore" })
        {
            failure = stage;
            calls.Clear();
            await Rejected(gui.SwitchAsync(2), stage + " failure aborts pipeline");
            Check(!calls.Contains("launch") && (stage != "manifest" || calls.Count == 0), stage + " blocks downstream launch/writes");
            failure = "";
            await gui.SwitchAsync(2);
            Check(calls.Last() == "launch", "lease released after " + stage + " failure");
        }
        calls.Clear(); game = true;
        await Rejected(gui.SwitchAsync(2), "running game rejects switch");
        await Rejected(cli.AddAccountAsync(), "running game rejects addaccount");
        Check(calls.Count == 0, "running game remains untouched");
        game = false;
        await Rejected(gui.SwitchAsync(99), "unknown backup ID rejected");
        foreach (var mode in new[] { "unavailable", "precheck", "partial" })
        {
            calls.Clear(); unavailable = mode == "unavailable"; partial = mode == "partial"; failure = mode == "precheck" ? mode : "";
            var result = await gui.SwitchAsync(2);
            Check(result.Warning is not null && calls.Contains("restore") && (mode == "partial" || !calls.Contains("region")),
                mode + " retains explicit GUI/CLI account-only or partial-region diagnosis");
        }
        failure = ""; unavailable = partial = false; calls.Clear();
        restore = new(TaskCreationOptions.RunContinuationsAsynchronously);
        var inFlight = gui.SwitchAsync(2);
        while (!calls.Contains("restore")) await Task.Delay(5);
        var shutdown = gui.BeginShutdownAsync();
        Check(!shutdown.IsCompleted, "shutdown waits in-flight restore rather than abandoning transaction");
        await Rejected(gui.SwitchAsync(3), "shutdown rejects new operation");
        restore.SetResult();
        await Rejected(inFlight, "shutdown cancels subsequent launch");
        await shutdown;
        Check(!calls.Contains("launch"), "no Battle.net restart after shutdown intent");
        restore = null;
        async Task<AccountCommandResult> Command(string[] args, AccountOperationService? service = null,
            AccountSwitchVerificationState state = AccountSwitchVerificationState.LoggedIn) =>
            await AccountCommandRunner.ExecuteAsync(args, service ?? Service(), (_, _, _) => Task.FromResult(state), TimeSpan.Zero);
        calls.Clear();
        Check((await Command(new[] { "--switch", "2" })).ExitCode == 0 && calls.SequenceEqual(new[] { "quit", "region", "restore", "launch" }),
            "no-primary CLI core executes same protected pipeline and reports confirmed success");
        calls.Clear();
        Check((await Command(new[] { "--addaccount" })).Status == "login_page_requested" && calls.SequenceEqual(new[] { "quit", "clear", "launch" }),
            "CLI addaccount uses protected clear/login pipeline and does not claim authentication");
        foreach (var stage in new[] { "manifest", "quit", "region", "restore" })
        {
            calls.Clear(); failure = stage;
            Check((await Command(new[] { "--switch", "2" })).ExitCode == 1 && !calls.Contains("launch"),
                "CLI reports " + stage + " failure without false success");
        }
        failure = ""; game = true; calls.Clear();
        Check((await Command(new[] { "--switch", "2" })).ExitCode == 1 &&
              (await Command(new[] { "--addaccount" })).ExitCode == 1 && calls.IsEmpty,
            "CLI core rejects running game for switch and addaccount");
        game = false;
        Check((await Command(new[] { "--switch", "99" })).ExitCode == 1, "CLI missing target rejected");
        Check((await Command(new[] { "--switch", "2" }, state: AccountSwitchVerificationState.Unconfirmed)).ExitCode == 3,
            "CLI unconfirmed login has nonzero exit status");
        calls.Clear(); precheck = new(TaskCreationOptions.RunContinuationsAsynchronously);
        var cliFirst = Command(new[] { "--switch", "2" });
        Check((await Command(new[] { "--switch", "3" })).ExitCode == 1 &&
              (await Command(new[] { "--addaccount" })).ExitCode == 1, "parallel CLI commands cannot bypass shared file lease");
        precheck.SetResult();
        await cliFirst;
        precheck = null;
        Check((await Command(new[] { "--switch", "2" }, gui)).ExitCode == 1, "CLI core rejects exiting operation gate");
        Check(!AccountCommandRunner.TryParse(new[] { "--switch", "../2" }, out _, out _) &&
              !AccountCommandRunner.TryParse(new[] { "--switch", "0" }, out _, out _) &&
              !AccountCommandRunner.TryParse(new[] { "--addaccount", "extra" }, out _, out _) &&
              AccountCommandRunner.TryParse(new[] { "--switch", "2" }, out var parsed, out _) && parsed == 2,
            "CLI validates command shape and positive account ID before data access");
        var name = @"Local\CloudLight.SafetyFixture." + Guid.NewGuid().ToString("N");
        using var primary = new Mutex(true, name, out var created);
        using var secondary = new Mutex(true, name, out var secondCreated);
        Check(created && !secondCreated, "existing primary (idle/busy/exiting/tray) explicitly rejects independent CLI lifetime");
        primary.ReleaseMutex();
    }

    private static async Task<int> ProbeGateAsync(string path)
    {
        var start = new System.Diagnostics.ProcessStartInfo("dotnet")
        {
            UseShellExecute = false, CreateNoWindow = true,
            RedirectStandardOutput = true, RedirectStandardError = true,
        };
        start.ArgumentList.Add(typeof(App).Assembly.Location);
        start.ArgumentList.Add("--account-gate-probe");
        start.ArgumentList.Add(path);
        using var process = System.Diagnostics.Process.Start(start)!;
        try { await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(15)); }
        catch { if (!process.HasExited) process.Kill(entireProcessTree: true); throw; }
        return process.ExitCode;
    }
}
