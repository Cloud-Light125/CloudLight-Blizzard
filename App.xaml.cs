using System.IO;
using System.Text;
using System.Threading;
using System.Windows;
using System.Windows.Threading;
using CloudLightBlizzard.Services;
using CloudLightBlizzard.Services.Drops;

namespace CloudLightBlizzard;

public partial class App : Application
{
    /// <summary>第二个实例用它唤起已运行的窗口。</summary>
    public const string ShowEventName = @"Local\CloudLight Blizzard.Show";
    internal static bool IsHeadlessSelfTest { get; private set; }
    private const string MutexName = @"Local\CloudLight Blizzard.SingleInstance";
    private static readonly object ErrorDialogSync = new();
    private static readonly TimeSpan ErrorDialogThrottle = TimeSpan.FromSeconds(3);
    private static string? _lastErrorDialogSignature;
    private static DateTimeOffset _lastErrorDialogAt;
    private Mutex? _singleInstance;

    protected override void OnStartup(StartupEventArgs e)
    {
        if (e.Args.Length == 2 && e.Args[0] == "--account-gate-probe")
        {
            try { using var lease = new AccountOperationGate(Path.GetFullPath(e.Args[1])).Enter(); Shutdown(0); }
            catch (InvalidOperationException) { Shutdown(2); }
            return;
        }
        string? dropsAcceptanceRoot = null;
        if (e.Args.Length >= 2 && e.Args[0] == "--drops-acceptance")
        {
            dropsAcceptanceRoot = Path.GetFullPath(e.Args[1]).TrimEnd(Path.DirectorySeparatorChar);
            var originalRoot = AppPaths.Current.Root.TrimEnd(Path.DirectorySeparatorChar);
            if (dropsAcceptanceRoot.Equals(originalRoot, StringComparison.OrdinalIgnoreCase) ||
                originalRoot.StartsWith(dropsAcceptanceRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) ||
                dropsAcceptanceRoot.StartsWith(originalRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                throw new ArgumentException("Drops 验收必须使用与原账号数据分离的目录。");
            AppPaths.UseSelfTestRoot(dropsAcceptanceRoot);
        }
        if (e.Args.Length >= 2 && e.Args[0] == "--cloud-services-live-selftest")
        {
            IsHeadlessSelfTest = true;
            Task.Run(() => CloudServicesSelfTest.RunLiveUpdateAsync(e.Args[1])).GetAwaiter().GetResult();
            Shutdown(0);
            return;
        }

        if (e.Args.Length >= 2 && e.Args[0] == "--cloud-services-selftest")
        {
            IsHeadlessSelfTest = true;
            Task.Run(() => CloudServicesSelfTest.RunAsync(e.Args[1])).GetAwaiter().GetResult();
            Shutdown(0);
            return;
        }

        if (e.Args.Length >= 2 && e.Args[0] == "--feature-selftest")
        {
            IsHeadlessSelfTest = true;
            AppPaths.UseSelfTestRoot(Path.Combine(Path.GetFullPath(e.Args[1]), "app-data"));
            if (e.Args.ElementAtOrDefault(2) == "exit-safety")
            {
                ShutdownMode = ShutdownMode.OnExplicitShutdown;
                FeatureSelfTest.Run(e.Args[1], "exit-safety");
            }
            else Task.Run(() => FeatureSelfTest.Run(e.Args[1], e.Args.ElementAtOrDefault(2))).GetAwaiter().GetResult();
            Shutdown(File.ReadAllText(Path.Combine(e.Args[1], "feature-selftest.txt")).Contains("OVERALL: FAIL") ? 1 : 0);
            return;
        }

        if (e.Args.Length >= 2 && e.Args[0] == "--ui-selftest")
        {
            IsHeadlessSelfTest = true;
            base.OnStartup(e);
            var exitCode = UiVisualTreeSelfTest.Run(e.Args[1]);
            Environment.ExitCode = exitCode;
            Shutdown(exitCode);
            return;
        }

        // 无界面自检:读一遍账号库并把结果写到 selftest.txt,然后退出。用于验证数据层 / 排错。
        if (e.Args.Contains("--selftest"))
        {
            RunSelfTest();
            Shutdown(0);
            return;
        }

        // CLI owns the same lifetime mutex as the GUI before constructing data services.
        // An existing (including older) primary cannot safely accept these commands: fail explicitly.
        if (e.Args.Length >= 1 && e.Args[0] is "--switch" or "--addaccount")
        {
            _singleInstance = new Mutex(true, MutexName, out var cliOwnsInstance);
            var result = !cliOwnsInstance
                ? new AccountCommandResult(2, "rejected", "主实例已运行（可能忙碌或正在退出）；请在主窗口执行账号操作。")
                : Task.Run(() => AccountCommandRunner.RunAsync(e.Args)).GetAwaiter().GetResult();
            try
            {
                Directory.CreateDirectory(AppPaths.Current.Root);
                File.WriteAllText(Path.Combine(AppPaths.Current.Root, e.Args[0] == "--switch" ? "switch.txt" : "addaccount.txt"),
                    System.Text.Json.JsonSerializer.Serialize(result), Encoding.UTF8);
            }
            catch (Exception ex) { result = new(1, "report_failed", result.Message + "；无法写入报告：" + ex.Message); }
            Console.WriteLine(result.Message);
            Shutdown(result.ExitCode);
            return;
        }

        // 国际服生涯链路验证:--careerprobe "Name#1234"。抓暴雪生涯页 → 解析 → 映射,写 careerprobe.txt 并打开。
        if (e.Args.Length >= 1 && e.Args[0] == "--careerprobe")
        {
            var sb4 = new StringBuilder();
            var tag = e.Args.Length >= 2 ? e.Args[1] : "";
            try { Task.Run(() => Services.Overwatch.CareerProbe.RunAsync(sb4, tag)).GetAwaiter().GetResult(); }
            catch (Exception ex) { sb4.AppendLine("异常: " + ex); }
            try
            {
                var dir = AppPaths.Current.Root;
                Directory.CreateDirectory(dir);
                // 不自动打开:这个探针要反复跑,弹一堆记事本比看不到结果更烦
                File.WriteAllText(Path.Combine(dir, "careerprobe.txt"), sb4.ToString(), Encoding.UTF8);
            }
            catch { }
            Shutdown(0);
            return;
        }

        // 国际服生涯界面验证:--careerdemo "Name#1234"。免登录直接开新窗,查布局/分段切换是否正常。
        if (e.Args.Length >= 1 && e.Args[0] == "--careerdemo")
        {
            Stats.CareerWindow.ShowStandalone(e.Args.Length >= 2 ? e.Args[1] : "");
            return;
        }

        // 单实例:已经有一个在跑,就唤起它的窗口然后本进程退出(避免托盘出现两个图标)。
        var instanceName = dropsAcceptanceRoot is null ? MutexName : MutexName + ".DropsAcceptance." +
            Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(Encoding.UTF8.GetBytes(dropsAcceptanceRoot)))[..16];
        _singleInstance = new Mutex(true, instanceName, out bool isNew);
        if (!isNew)
        {
            try { EventWaitHandle.OpenExisting(ShowEventName).Set(); } catch { /* 对面可能还没建好句柄 */ }
            Shutdown(0);
            return;
        }

        BuildProvenance.WriteStartupLog(AppPaths.Current.LogsDir);
        var settings = AppSettings.Load();
        var logSession = new PlatformLogSession(AppPaths.Current.LogsDir);

        // 关掉 WPF 硬件加速:进程内不再建 D3D 设备,游戏加加 / RTSS 等 FPS 悬浮窗就不会把本程序
        // 当游戏挂性能条(它们靠 hook D3D Present 出条)。本工具是静态界面,软件渲染无感。
        System.Windows.Media.RenderOptions.ProcessRenderMode = System.Windows.Interop.RenderMode.SoftwareOnly;

        // 应用亮/暗主题(记住上次选择)。
        try { ThemeManager.Apply(settings.DarkMode); }
        catch { /* ignore */ }

        // 未捕获异常写日志,避免直接崩溃后无从查起。
        DispatcherUnhandledException += OnUnhandled;
        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
            WriteCrash(args.ExceptionObject as Exception);

        base.OnStartup(e);
        var startHidden = global::CloudLightBlizzard.MainWindow.ShouldStartHidden(e.Args, settings.StartMinimized);
        var mainWindow = new global::CloudLightBlizzard.MainWindow(startHidden, logSession, settings: settings);
        if (dropsAcceptanceRoot is not null) mainWindow.Title += " · Drops 本地验收";
        MainWindow = mainWindow;
        if (!startHidden) mainWindow.Show();
    }

    private static void RunSelfTest()
    {
        var sb = new StringBuilder();
        try
        {
            var paths = new BattleNetPaths();
            sb.AppendLine($"LocalRoot     : {paths.LocalRoot}");
            sb.AppendLine($"CachedData.db : {paths.CachedDataDb} (exists={File.Exists(paths.CachedDataDb)})");
            sb.AppendLine($"Battle.net.exe: {paths.ClientExe ?? "(未找到)"}");
            sb.AppendLine($"数据目录可用  : {paths.Exists}");
            sb.AppendLine();

            var accounts = new AccountReader(paths).ReadAccounts(out var activeId);
            sb.AppendLine($"当前账号 id   : {(activeId?.ToString() ?? "(未知)")}");
            sb.AppendLine($"读到账号数    : {accounts.Count}");
            sb.AppendLine("----------------------------------------");
            foreach (var a in accounts)
                sb.AppendLine($"{(a.AccountId == activeId ? "* " : "  ")}{a.AccountId,-11} {a.BattleTag}");
        }
        catch (Exception ex)
        {
            sb.AppendLine("自检异常:" + ex);
        }

        try
        {
            var dir = AppPaths.Current.Root;
            Directory.CreateDirectory(dir);
            File.WriteAllText(Path.Combine(dir, "selftest.txt"), sb.ToString(), Encoding.UTF8);
        }
        catch { /* ignore */ }
    }

    private void OnUnhandled(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        WriteCrash(e.Exception);
        if (ShouldShowUnhandledDialog(e.Exception))
            MessageBox.Show(e.Exception.Message, "出错了", MessageBoxButton.OK, MessageBoxImage.Error);
        e.Handled = true;
    }

    internal static bool ShouldShowUnhandledDialog(Exception ex)
    {
        var root = ex.GetBaseException();
        var signature = $"{root.GetType().FullName}|{root.Message}";
        var now = DateTimeOffset.UtcNow;
        lock (ErrorDialogSync)
        {
            if (string.Equals(_lastErrorDialogSignature, signature, StringComparison.Ordinal) &&
                now - _lastErrorDialogAt < ErrorDialogThrottle)
                return false;
            _lastErrorDialogSignature = signature;
            _lastErrorDialogAt = now;
            return true;
        }
    }

    private static void WriteCrash(Exception? ex)
    {
        if (ex is null) return;
        try
        {
            var dir = AppPaths.Current.Root;
            Directory.CreateDirectory(dir);
            File.AppendAllText(
                Path.Combine(dir, "crash.log"),
                $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {ex}{System.Environment.NewLine}{System.Environment.NewLine}");
        }
        catch { /* 记不了也不能再抛 */ }
    }
}
