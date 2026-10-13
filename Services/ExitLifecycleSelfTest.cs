using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Windows;
using System.Windows.Threading;
using CloudLightBlizzard.Services.Drops;
using CloudLightBlizzard.ViewModels;
using CloudLightBlizzard.Views.Pages;

namespace CloudLightBlizzard.Services;

/// <summary>Offline tests pump the real WPF Dispatcher and exercise the production QR page/Host.</summary>
public static class ExitLifecycleSelfTest
{
    public static void Run(StringBuilder report)
    {
        var dispatcher = Dispatcher.CurrentDispatcher;
        var previous = SynchronizationContext.Current;
        SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext(dispatcher));
        var frame = new DispatcherFrame();
        var task = RunAsync(report);
        _ = task.ContinueWith(_ => dispatcher.BeginInvoke(() => frame.Continue = false), TaskScheduler.Default);
        var watchdog = new DispatcherTimer { Interval = TimeSpan.FromSeconds(55) };
        watchdog.Tick += (_, _) => frame.Continue = false;
        watchdog.Start();
        Dispatcher.PushFrame(frame);
        watchdog.Stop();
        SynchronizationContext.SetSynchronizationContext(previous);
        if (!task.IsCompleted) throw new TimeoutException("Exit safety Dispatcher test exceeded budget.");
        task.GetAwaiter().GetResult();
    }

    private static async Task RunAsync(StringBuilder report)
    {
        foreach (var scenario in new[] { "generate", "waiting", "network", "queued-success", "cancel-race", "update" })
            await CheckPageAsync(scenario, report);
        await CheckHostAsync(report);
        await CheckWindowPolicyAsync(report);
        await CheckUpdateExitAsync(report);
        await CheckActualGuiOperationAsync(report);
        var exceptional = new ExitLifecycleCoordinator();
        await exceptional.BeginAsync(_ => Task.FromException(new IOException("fixture cleanup failure")));
        await exceptional.BeginAsync(_ => throw new InvalidOperationException("must not dispose twice"));
        Assert(exceptional.IsCompleted && exceptional.Error is IOException,
            "cleanup exception still completes second-close gate and retains original error", report);
    }

    private static async Task CheckPageAsync(string scenario, StringBuilder report)
    {
        await using var host = new DropsHostService();
        var vm = new DropsViewModel(host);
        var page = new DropsPage();
        typeof(DropsPage).GetField("_vm", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(page, vm);
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var response = new TaskCompletionSource<JsonElement>(TaskCreationOptions.RunContinuationsAsynchronously);
        var calls = new List<string>();
        host.ConfigureRequestOverrideForSelfTest(async (_, command, _, token) =>
        {
            calls.Add(command);
            if (command == "qr_generate" && scenario != "generate")
                return JsonSerializer.SerializeToElement(new { state = "waiting" });
            entered.TrySetResult();
            return await response.Task.WaitAsync(token);
        });
        var login = InvokeTask(page, "StartBilibiliQrLoginAsync");
        if (scenario == "waiting") await Task.Delay(30);
        else await entered.Task.WaitAsync(TimeSpan.FromSeconds(4));

        // Queue a success while the Dispatcher is busy with the synchronous exit intent.
        if (scenario == "queued-success")
            response.TrySetResult(JsonSerializer.SerializeToElement(new { state = "success" }));
        Task? cancelling = scenario == "cancel-race" ? InvokeTask(page, "CancelBilibiliQrAsync") : null;
        var coordinator = new ExitLifecycleCoordinator();
        page.BeginShutdown();
        host.BeginShutdown();
        var cleanups = 0;
        async Task Cleanup(CancellationToken _)
        {
            cleanups++;
            await page.DisposeAsync();
            await host.DisposeAsync();
            Assert(page.Dispatcher.CheckAccess(), "cleanup remains on owning Dispatcher", report);
        }
        var first = coordinator.BeginAsync(Cleanup);
        var second = coordinator.BeginAsync(Cleanup);
        await Task.WhenAll(first, second, login).WaitAsync(TimeSpan.FromSeconds(7));
        if (cancelling is not null)
        {
            response.TrySetResult(JsonSerializer.SerializeToElement(new { state = "cancelled" }));
            await cancelling;
        }
        await page.DisposeAsync();
        var before = calls.Count;
        await InvokeTask(page, "StartBilibiliQrLoginAsync");
        Assert(cleanups == 1 && coordinator.Error is null && calls.Count == before,
            scenario + ": real QR generate/poll cancelled, duplicate exit/dispose safe, no restart", report);
        if (scenario == "generate") Assert(!calls.Contains("qr_poll"), "late generate cannot start poller", report);
        Assert(!vm.BilibiliDetails.LoggedIn, scenario + ": late success not applied during exit", report);
    }

    private static async Task CheckWindowPolicyAsync(StringBuilder report)
    {
        // Construct but never Show/initialize: no real account reads, Worker starts or network refresh.
        var window = new MainWindow(settings: new AppSettings { CloseToTray = true, EnableWindowsNotifications = false },
            paths: new BattleNetPaths(Path.Combine(AppPaths.Current.Root, "fake-local"),
                Path.Combine(AppPaths.Current.Root, "fake-roaming")));
        var closing = typeof(MainWindow).GetMethod("OnClosing", BindingFlags.Instance | BindingFlags.NonPublic)!;
        var exiting = typeof(MainWindow).GetField("_isExiting", BindingFlags.Instance | BindingFlags.NonPublic)!;
        var tray = new System.ComponentModel.CancelEventArgs();
        closing.Invoke(window, new object[] { tray });
        Assert(tray.Cancel && !(bool)exiting.GetValue(window)!, "actual window close-to-tray preserves lifecycle", report);
        var cancel = new System.ComponentModel.CancelEventArgs();
        typeof(MainWindow).GetMethod("CancelClosing", BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(window, new object[] { cancel });
        Assert(cancel.Cancel && !(bool)exiting.GetValue(window)!, "cancel preserves exit state", report);
        ((MainViewModel)window.DataContext).Settings.CloseToTray = false;
        var first = new System.ComponentModel.CancelEventArgs();
        closing.Invoke(window, new object[] { first });
        var second = new System.ComponentModel.CancelEventArgs();
        closing.Invoke(window, new object[] { second });
        var cleanup = (Task)typeof(MainWindow).GetField("_exitCleanupTask", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(window)!;
        Assert(first.Cancel && second.Cancel, "actual Closing cancels both initial and repeated exit", report);
        await cleanup.WaitAsync(TimeSpan.FromSeconds(8));
        Assert(!window.IsEnabled, "actual window fences buttons before cleanup", report);
    }

    private static async Task CheckUpdateExitAsync(StringBuilder report)
    {
        var launcher = new FixtureInstaller();
        var window = new MainWindow(installerLauncher: launcher,
            settings: new AppSettings { EnableWindowsNotifications = false },
            paths: new BattleNetPaths(Path.Combine(AppPaths.Current.Root, "update-local"), Path.Combine(AppPaths.Current.Root, "update-roaming")));
        var vm = (MainViewModel)window.DataContext;
        var page = (DropsPage)typeof(MainWindow).GetField("_dropsPage", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(window)!;
        vm.DropsHost.ConfigureRequestOverrideForSelfTest(async (_, _, _, token) =>
        {
            await Task.Delay(Timeout.Infinite, token);
            return JsonSerializer.SerializeToElement(new { });
        });
        var qr = InvokeTask(page, "StartBilibiliQrLoginAsync");
        var operations = (AccountOperationService)typeof(MainViewModel).GetField("_accountOperations", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(vm)!;
        var lease = operations.EnterOperation();
        var installer = Path.Combine(AppPaths.Current.Root, "fixture-installer.exe");
        File.WriteAllText(installer, "fixture: never executed");
        var update = window.InstallDownloadedUpdate(installer);
        Assert(launcher.Calls == 0 && !update.IsCompleted, "installer waits for account transaction drain", report);
        Assert(!await window.InstallDownloadedUpdate(installer), "duplicate update exit rejected", report);
        lease.Dispose();
        Assert(await update.WaitAsync(TimeSpan.FromSeconds(8)), "actual update exit launches fake installer after cleanup", report);
        await qr;
        Assert(launcher.Calls == 1, "update exit cancels real page QR and launches exactly once", report);
    }

    private sealed class FixtureInstaller : IInstallerLauncher
    {
        internal int Calls;
        public Process? Start(string path) { Calls++; return Process.GetCurrentProcess(); }
    }

    private static async Task CheckActualGuiOperationAsync(StringBuilder report)
    {
        var root = Path.Combine(AppPaths.Current.Root, "gui-operation");
        Directory.CreateDirectory(root);
        var paths = new BattleNetPaths(Path.Combine(root, "local"), Path.Combine(root, "roaming"));
        var vm = new MainViewModel(settings: new AppSettings(), paths: paths);
        var held = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var mutations = 0;
        var backend = new AccountOperationBackend
        {
            ResolveTarget = id => new(id, "fixture", Services.OverwatchRegion.OverwatchRegion.International),
            ValidateProfile = _ => Task.CompletedTask,
            ReadActive = async () => { await held.Task; return 1; },
            GameRunning = () => false,
            RegionStatus = () => Task.FromResult(new Services.OverwatchRegion.RegionSnapshotStatus()),
            Normalize = (_, _) => throw new InvalidOperationException("no region fixture"),
            Quit = () => Task.FromResult(true),
            Restore = _ => { mutations++; return Task.CompletedTask; },
            ClearPointer = () => { mutations++; return Task.CompletedTask; },
            Launch = () => Task.CompletedTask,
        };
        var lockPath = Path.Combine(root, "operation.lock");
        var service = new AccountOperationService(new AccountOperationGate(lockPath), backend,
            new AccountSwitchLog(Path.Combine(root, "switch.log")));
        typeof(MainViewModel).GetField("_accountOperations", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(vm, service);
        var target = new AccountRow { AccountId = 2, BattleTag = "fixture", HasProfile = true };
        var first = vm.SwitchToAsync(target);
        Assert(vm.Busy && !first.IsCompleted, "actual GUI Busy and lease established before first await", report);
        await vm.SwitchToAsync(target);
        await vm.SwitchToAsync(new AccountRow { AccountId = 3, HasProfile = true });
        await vm.SaveCurrentAsync();
        await vm.AddAccountAsync();
        var cli = new AccountOperationService(new AccountOperationGate(lockPath), backend,
            new AccountSwitchLog(Path.Combine(root, "cli.log")));
        var rejected = await AccountCommandRunner.ExecuteAsync(new[] { "--switch", "3" }, cli,
            (_, _, _) => Task.FromResult(AccountSwitchVerificationState.LoggedIn));
        Assert(mutations == 0 && rejected.ExitCode != 0, "actual GUI double clicks/save/add and simultaneous CLI cannot enter files", report);
        held.SetResult();
        await first;
        Assert(!vm.Busy && mutations == 1, "actual GUI releases Busy after one switch pipeline", report);
        typeof(MainViewModel).GetField("_pendingSwitchUntil", BindingFlags.Instance | BindingFlags.NonPublic)!
            .SetValue(vm, DateTime.UtcNow.AddSeconds(-1));
        Assert(!vm.ClientRunning, "fixture client remains absent", report);
        var verify = (Task)typeof(MainViewModel).GetMethod("VerifySwitchAsync", BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(vm, new object[] { 2L })!;
        await verify;
        Assert(typeof(MainViewModel).GetField("_pendingSwitchId", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(vm) is null &&
            vm.StatusText.Contains("暂时没有确认"), "actual GUI consumes absent-client timeout and clears pending verification", report);
        typeof(MainViewModel).GetProperty("ClientRunning")!.SetValue(vm, true);
        typeof(MainViewModel).GetMethod("ResumeUnconfirmedVerificationIfClientStarted", BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(vm, new object[] { false });
        Assert((long?)typeof(MainViewModel).GetField("_pendingSwitchId", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(vm) == 2 &&
            (DateTime)typeof(MainViewModel).GetField("_pendingSwitchUntil", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(vm)! > DateTime.UtcNow,
            "actual GUI allows bounded revalidation after later client start", report);
        await vm.BeginShutdownAsync();
        await vm.DropsHost.DisposeAsync();
        vm.CloudHttpClients.Dispose();
    }

    private static async Task CheckHostAsync(StringBuilder report)
    {
        var repo = new DirectoryInfo(AppContext.BaseDirectory);
        while (repo is not null && !File.Exists(Path.Combine(repo.FullName, ".worker-build-venv", "Scripts", "python.exe")))
            repo = repo.Parent;
        if (repo is null) throw new InvalidOperationException("Isolated Worker fixture requires existing Python build environment.");
        await using var host = new DropsHostService();
        int? pid = null;
        host.SnapshotChanged += (_, snapshot) => { if (snapshot.ProcessId is int id) pid = id; };
        host.ConfigureTimeoutForSelfTest((_, command) => command == "shutdown" ? TimeSpan.FromMilliseconds(150) : null);
        host.ConfigureStartInfoForSelfTest(_ =>
        {
            var start = new ProcessStartInfo(Path.Combine(repo.FullName, ".worker-build-venv", "Scripts", "python.exe"))
            {
                UseShellExecute = false, CreateNoWindow = true,
                RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true,
                StandardInputEncoding = new UTF8Encoding(false), StandardOutputEncoding = new UTF8Encoding(false)
            };
            start.ArgumentList.Add("-u"); start.ArgumentList.Add("-c");
            start.ArgumentList.Add("import sys,json,time\nfor line in sys.stdin:\n r=json.loads(line)\n if r['command']=='shutdown': time.sleep(60)\n print(json.dumps({'type':'response','id':r['id'],'ok':True,'result':{}}),flush=True)");
            return start;
        });
        await host.RequestAsync(DropsPlatform.Bilibili, "load_state").WaitAsync(TimeSpan.FromSeconds(5));
        host.BeginShutdown();
        var rejected = false;
        try { await host.RequestAsync(DropsPlatform.Bilibili, "start"); } catch (ObjectDisposedException) { rejected = true; }
        var one = host.DisposeAsync().AsTask();
        await Task.WhenAll(one, host.DisposeAsync().AsTask()).WaitAsync(TimeSpan.FromSeconds(8));
        var alive = false;
        try { using var process = Process.GetProcessById(pid!.Value); alive = !process.HasExited; } catch (ArgumentException) { }
        Assert(pid.HasValue && !alive && rejected, "real unresponsive isolated Worker reaped; new work rejected; double Host dispose safe", report);
    }

    private static Task InvokeTask(object owner, string method) =>
        (Task)owner.GetType().GetMethod(method, BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(owner, null)!;
    private static void Assert(bool condition, string message, StringBuilder report)
    {
        report.AppendLine((condition ? "PASS " : "FAIL ") + message);
        if (!condition) throw new InvalidOperationException(message);
    }
}
