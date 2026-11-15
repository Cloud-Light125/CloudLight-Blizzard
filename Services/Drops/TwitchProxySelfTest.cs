using System.Diagnostics;
using System.IO;
using System.Text;
using CloudLightBlizzard.ViewModels;

namespace CloudLightBlizzard.Services.Drops;

internal static class TwitchProxySelfTest
{
    internal static async Task RunAsync(string outputRoot, StringBuilder report)
    {
        var repo = new DirectoryInfo(AppContext.BaseDirectory);
        while (repo is not null && !File.Exists(Path.Combine(repo.FullName, ".worker-build-venv", "Scripts", "python.exe"))) repo = repo.Parent;
        if (repo is null) throw new InvalidOperationException("Worker test runtime missing.");
        var root = Path.Combine(outputRoot, "twitch-proxy");
        Directory.CreateDirectory(root);
        var script = Path.Combine(root, "fixture.py");
        File.WriteAllText(script, """
import sys,time,os
from pathlib import Path
sys.path.insert(0,sys.argv[1])
from Shared.protocol import WorkerBase
class Worker(WorkerBase):
    platform='twitch'
    needs_https=False
    def __init__(self):
        super().__init__(Path(sys.argv[2]),Path(sys.argv[2])/'worker.log')
        self.commands.update(echo=self.load_state,crash=self.crash)
    def set_proxy(self,payload):
        time.sleep(.08)
        return super().set_proxy(payload)
    def load_state(self,payload):
        return {'proxy':self.proxy,'running':self.running,'pid':os.getpid()}
    def crash(self,payload): os._exit(23)
raise SystemExit(Worker().run())
""");
        var owned = new List<Process>();
        await using var host = new DropsHostService();
        host.ConfigureStartInfoForSelfTest(platform =>
        {
            if (platform != DropsPlatform.Twitch) throw new InvalidOperationException("Unexpected platform.");
            var start = new ProcessStartInfo(Path.Combine(repo.FullName, ".worker-build-venv", "Scripts", "python.exe"))
                { UseShellExecute = false, RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true, CreateNoWindow = true };
            foreach (var arg in new[] { script, Path.Combine(repo.FullName,"Integrations","Drops"), root }) start.ArgumentList.Add(arg);
            return start;
        });
        void Check(bool value, string text)
        {
            if (!value) throw new InvalidOperationException(text);
            report.AppendLine("PASS " + text);
        }
        DropsProxySettings Policy(bool global, bool twitch, int port = 7897) => MainViewModel.CreateDropsProxySettings(new AppSettings
            { EnableProxy=global, TwitchUseProxy=twitch, ProxyUrl=$"http://127.0.0.1:{port}", FallbackDirect=true });
        foreach (var flags in new[] { (true,true),(true,false),(false,true) })
        {
            await host.ApplyProxyAsync(Policy(flags.Item1,flags.Item2));
            var state = await host.RequestAsync(DropsPlatform.Twitch,"echo");
            Check(state.GetProperty("proxy").GetProperty("enableProxy").GetBoolean() == (flags.Item1 && flags.Item2),
                $"WPF AppSettings → Main → Host → JSONL 实际开关 global={flags.Item1}, Twitch={flags.Item2}");
        }
        var first = host.ApplyProxyAsync(Policy(true,true,7001));
        var second = host.ApplyProxyAsync(Policy(true,true,7897));
        await Task.WhenAll(first,second);
        var latest = await host.RequestAsync(DropsPlatform.Twitch,"echo");
        Check(latest.GetProperty("proxy").GetProperty("proxyUrl").GetString() == "http://127.0.0.1:7897", "快速两次范围/地址更新最终采用最新策略");
        await host.ApplyProxyAsync(new DropsProxySettings(true,"invalid-proxy",false));
        var rejected = false;
        try { await host.RequestAsync(DropsPlatform.Twitch,"echo"); } catch { rejected = true; }
        Check(rejected,"无效代理不能被当作已应用或静默切为直连");
        host.ConfigureProxy(Policy(true,true));
        var starts = await Task.WhenAll(host.RequestAsync(DropsPlatform.Twitch,"start"),host.RequestAsync(DropsPlatform.Twitch,"start"));
        Check(starts.All(s=>s.GetProperty("proxy").GetProperty("enableProxy").GetBoolean()),"立即启动先完成最新代理门禁");
        var running = await host.RequestAsync(DropsPlatform.Twitch,"echo");
        var pid = running.GetProperty("pid").GetInt32();
        Check(starts.All(s=>s.GetProperty("pid").GetInt32()==pid),"重复开始共用同一个实际 Worker 进程");
        var process = Process.GetProcessById(pid);
        owned.Add(process);
        try { await host.RequestAsync(DropsPlatform.Twitch,"crash"); } catch { }
        await process.WaitForExitAsync();
        var restarted = await host.RequestAsync(DropsPlatform.Twitch,"start");
        var newPid = restarted.GetProperty("pid").GetInt32();
        owned.Add(Process.GetProcessById(newPid));
        Check(newPid != pid && restarted.GetProperty("proxy").GetProperty("enableProxy").GetBoolean(),"真实崩溃重建后重新获取正确代理");
        await host.RequestAsync(DropsPlatform.Twitch,"stop");
        await Task.Delay(100);
        Check(!(await host.RequestAsync(DropsPlatform.Twitch,"echo")).GetProperty("running").GetBoolean(),"手动停止后不自动复活");
        await host.DisposeAsync();
        Check(owned.All(p=>p.HasExited),"Host退出清理所有本测试 Worker，无孤立进程");
        foreach (var p in owned) p.Dispose();
    }
}
