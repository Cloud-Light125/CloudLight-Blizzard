using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.Json;
using CloudLightBlizzard.ViewModels;

namespace CloudLightBlizzard.Services.Drops;

/// <summary>Real Host/JSONL/VM fault injection; accounts and network business are fixtures.</summary>
internal static class SoopTimeoutSelfTest
{
    internal static async Task RunAsync(string outputRoot, StringBuilder report)
    {
        var repo = new DirectoryInfo(AppContext.BaseDirectory);
        while (repo is not null && !File.Exists(Path.Combine(repo.FullName, ".worker-build-venv", "Scripts", "python.exe")))
            repo = repo.Parent;
        if (repo is null) throw new InvalidOperationException("本地故障注入需要 Worker 构建环境。");
        var results = new List<object>();
        foreach (var scenario in new[] { "normal", "stop_timeout", "start_timeout", "refresh_timeout", "cancel_timeout", "crash", "crash_all_running" })
        {
            var root = Path.Combine(outputRoot, "soop-timeout", scenario);
            Directory.CreateDirectory(root);
            var script = Path.Combine(root, "fixture.py");
            File.WriteAllText(script, """
import sys, time, os, asyncio, threading
from pathlib import Path
from concurrent.futures import Future
sys.path.insert(0, sys.argv[1])
from Shared.protocol import WorkerBase, event
sys.path.insert(0, str(Path(sys.argv[1])/'soop'))
from worker import SoopWorker
class Worker(WorkerBase):
    platform = 'soop'
    needs_https = False
    def __init__(self):
        super().__init__(Path(sys.argv[2]), Path(sys.argv[2])/'worker.log')
        self.accounts = {'A': False, 'B': False}
        self.mode = ''
        self._loop = asyncio.new_event_loop()
        threading.Thread(target=self._loop.run_forever,daemon=True).start()
        self.commands.update(start_account=self.start_account, stop_account=self.stop_account, fixture_mode=self.fixture_mode, crash=self.crash)
    def fixture_mode(self, payload):
        self.mode = payload['mode']
        return {}
    def load_state(self, payload):
        return {'running':any(self.accounts.values()), 'accounts':[{'uid':k,'running':v,'status':'正在观看' if v else '已停止'} for k,v in self.accounts.items()],
                'tasks':[{'uid':'B','id':'mission','name':'fixture','viewMinutes':10}], 'inventory':[], 'refreshCompleted':True}
    def publish(self):
        state=self.load_state({}); event('state',state); self.running=state['running']; self.status('模拟观看')
        return state
    def start_account(self, payload):
        uid=payload['uid']
        if uid=='B' and self.mode=='start_timeout':
            event('fixture_waiting',{'command':'start_account'}); time.sleep(60)
        self.accounts[uid]=True; event('fixture_started',{'uid':uid})
        return self.publish()
    def stop_account(self, payload):
        uid=payload['uid']
        if uid=='B' and self.mode in ('stop_timeout','cancel_timeout'):
            event('fixture_waiting',{'command':'stop_account'}); time.sleep(60)
        self.accounts[uid]=False
        return self.publish()
    def refresh(self, payload):
        if self.mode=='refresh_timeout':
            self.mode=''; event('fixture_waiting',{'command':'refresh'})
            async def pending():
                await asyncio.sleep(60)
            SoopWorker._submit(self, pending(), timeout=60)
        return self.publish()
    def stop(self, payload):
        self.accounts={'A':False,'B':False}; return self.publish()
    def crash(self, payload):
        os._exit(23)
raise SystemExit(Worker().run())
""");
            var pids = new List<int>();
            var starts = new List<string>();
            var traceLock = new object();
            void Trace(string text) { lock (traceLock) File.AppendAllText(Path.Combine(root, "host-trace.txt"), text + "\n"); }
            var waiting = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            await using var host = new DropsHostService();
            host.ProtocolErrorForSelfTest = ex => Trace(ex.ToString());
            host.ConfigureStartInfoForSelfTest(_ =>
            {
                var info = new ProcessStartInfo(Path.Combine(repo.FullName, ".worker-build-venv", "Scripts", "python.exe"))
                {
                    UseShellExecute = false, CreateNoWindow = true,
                    RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true,
                    StandardInputEncoding = new UTF8Encoding(false), StandardOutputEncoding = new UTF8Encoding(false),
                    StandardErrorEncoding = new UTF8Encoding(false),
                };
                foreach (var arg in new[] { script, Path.Combine(repo.FullName, "Integrations", "Drops"), root }) info.ArgumentList.Add(arg);
                return info;
            });
            host.SnapshotChanged += (_, e) => { Trace($"snapshot {e.Lifecycle} {e.LastError}"); if (e.ProcessId is { } pid) lock (pids) if (!pids.Contains(pid)) pids.Add(pid); };
            host.EventReceived += (_, e) =>
            {
                Trace($"event {e.Name}");
                if (e.Name == "fixture_waiting") waiting.TrySetResult();
                if (e.Name == "fixture_started") lock (starts) starts.Add(e.Payload.GetProperty("uid").GetString()!);
            };
            using var vm = new DropsViewModel(host, TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(1),
                TimeSpan.FromSeconds(10), null, TimeSpan.FromMilliseconds(150), null);
            vm.SelectPlatform(DropsPlatform.Soop);
            using var budget = new CancellationTokenSource(TimeSpan.FromSeconds(18));
            vm.BeginSoopStart("A", false);
            await host.RequestAsync(DropsPlatform.Soop, "start_account", new { uid = "A" }, budget.Token);
            vm.BeginSoopStart("B", false);
            var before = await host.RequestAsync(DropsPlatform.Soop, "start_account", new { uid = "B" }, budget.Token);
            vm.ApplyState(DropsPlatform.Soop, before);
            var generationBefore = before.GetProperty("_workerGeneration").GetInt64();
            var aStartsBefore = starts.Count(x => x == "A");
            var bStartsBefore = starts.Count(x => x == "B");
            await host.RequestAsync(DropsPlatform.Soop, "fixture_mode", new { mode = scenario }, budget.Token);
            host.ConfigureTimeoutForSelfTest((_, command) => command is "start_account" or "stop_account" or "refresh"
                ? TimeSpan.FromMilliseconds(1500) : null);
            string? failure = null;
            try
            {
                if (scenario == "start_timeout")
                    await host.RequestAsync(DropsPlatform.Soop, "stop_account", new { uid = "B" }, budget.Token);
                if (scenario is "normal" or "stop_timeout" or "crash") vm.StopSoopByUser("B");
                if (scenario is "crash" or "crash_all_running")
                    await host.RequestAsync(DropsPlatform.Soop, "crash", cancellationToken: budget.Token);
                else if (scenario == "cancel_timeout")
                {
                    using var caller = new CancellationTokenSource();
                    vm.SetSoopFailure("network timeout", uid: "B");
                    await waiting.Task.WaitAsync(budget.Token);
                    vm.StopSoopByUser("B");
                    var stopping = host.RequestAsync(DropsPlatform.Soop, "stop_account", new { uid = "B" }, caller.Token);
                    caller.CancelAfter(TimeSpan.FromMilliseconds(1400));
                    await stopping;
                }
                else
                    await host.RequestAsync(DropsPlatform.Soop, scenario == "start_timeout" ? "start_account" :
                        scenario == "refresh_timeout" ? "refresh" : "stop_account", new { uid = "B" }, budget.Token);
            }
            catch (Exception ex) { failure = ex.GetType().Name; }
            JsonElement after = default;
            for (var attempt = 0; attempt < 40; attempt++)
            {
                await Task.Delay(100, budget.Token);
                after = await host.RequestAsync(DropsPlatform.Soop, "load_state", cancellationToken: budget.Token);
                if (Running(after, "A") && (scenario is "refresh_timeout" or "crash_all_running" ? Running(after, "B") : !Running(after, "B"))) break;
            }
            // A late retry is not allowed to resurrect B after the visible recovery.
            await Task.Delay(500, budget.Token);
            after = await host.RequestAsync(DropsPlatform.Soop, "load_state", cancellationToken: budget.Token);
            vm.ApplyState(DropsPlatform.Soop, after);
            var generationAfter = after.GetProperty("_workerGeneration").GetInt64();
            var refreshAgain = await host.RequestAsync(DropsPlatform.Soop, "refresh", new { uid = "B" }, budget.Token);
            var aRunning = Running(after, "A");
            var bRunning = Running(after, "B");
            var isolated = scenario is "normal" or "refresh_timeout";
            var pass = aRunning && bRunning == (scenario is "refresh_timeout" or "crash_all_running") &&
                (isolated ? generationBefore == generationAfter : generationAfter > generationBefore) &&
                starts.Count(x => x == "B") == bStartsBefore + (scenario == "crash_all_running" ? 1 : 0) &&
                starts.Count(x => x == "A") == aStartsBefore + (isolated ? 0 : 1) &&
                refreshAgain.GetProperty("refreshCompleted").GetBoolean() && vm.Soop.Running;
            var uiRunning = vm.Soop.Running;
            var uiStatus = vm.Soop.Status;
            await host.DisposeAsync();
            bool orphan;
            lock (pids) orphan = pids.Any(pid => { try { return !Process.GetProcessById(pid).HasExited; } catch { return false; } });
            pass &= !orphan;
            results.Add(new { scenario, pass, failure, aRunning, bRunning, generationBefore, generationAfter,
                aStarts = starts.Count(x => x == "A"), bStarts = starts.Count(x => x == "B"), orphan,
                uiRunning, uiStatus,
                snapshotMinutes = after.GetProperty("tasks")[0].GetProperty("viewMinutes").GetInt32() });
            report.AppendLine($"{(pass ? "PASS" : "FAIL")} SOOP {scenario}: A={aRunning}, B={bRunning}, generation={generationBefore}->{generationAfter}, orphan={orphan}");
        }
        File.WriteAllText(Path.Combine(outputRoot, "soop-timeout-results.json"), JsonSerializer.Serialize(results, new JsonSerializerOptions { WriteIndented = true }));
        if (report.ToString().Contains("FAIL SOOP")) throw new InvalidOperationException("SOOP 超时故障注入存在失败项。");
    }

    private static bool Running(JsonElement state, string uid) => state.GetProperty("accounts").EnumerateArray()
        .Any(a => a.GetProperty("uid").GetString() == uid && a.GetProperty("running").GetBoolean());
}
