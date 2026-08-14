using System.IO;
using System.Text;
using System.Threading;
using System.Windows;
using System.Windows.Threading;
using CloudLightBlizzard.Services;

namespace CloudLightBlizzard;

public partial class App : Application
{
    /// <summary>第二个实例用它唤起已运行的窗口。</summary>
    public const string ShowEventName = @"Local\CloudLight Blizzard.Show";
    private const string MutexName = @"Local\CloudLight Blizzard.SingleInstance";
    private Mutex? _singleInstance;

    protected override void OnStartup(StartupEventArgs e)
    {
        if (e.Args.Length >= 2 && e.Args[0] == "--feature-selftest")
        {
            Task.Run(() => FeatureSelfTest.Run(e.Args[1])).GetAwaiter().GetResult();
            Shutdown(0);
            return;
        }

        // 无界面自检:读一遍账号库并把结果写到 selftest.txt,然后退出。用于验证数据层 / 排错。
        if (e.Args.Contains("--selftest"))
        {
            RunSelfTest();
            Shutdown(0);
            return;
        }

        // 无界面切换:--switch <accountId>。走的是与界面相同的服务逻辑,结果写到 switch.txt。
        if (e.Args.Length >= 1 && e.Args[0] == "--switch")
        {
            RunSwitch(e.Args.Length >= 2 ? e.Args[1] : "");
            Shutdown(0);
            return;
        }

        // 登录新号:--addaccount。优雅退出 → 清空本地令牌(非登出)→ 启动到登录页,供手动登录新号。
        if (e.Args.Contains("--addaccount"))
        {
            RunAddAccount();
            Shutdown(0);
            return;
        }

        // 战绩界面布局验证:--statsdemo。免登录用演示数据开 StatsWindow(查布局是否崩)。
        if (e.Args.Contains("--statsdemo"))
        {
            Stats.StatsWindow.ShowDemo();
            return;
        }

        // 好友列表验证:--friendtest。用缓存登录态实测 getFriendModule,写 friendtest.txt。
        if (e.Args.Contains("--friendtest"))
        {
            var sb3 = new StringBuilder();
            try { Task.Run(() => Services.Overwatch.OwProbe.RunFriendTestAsync(sb3)).GetAwaiter().GetResult(); }
            catch (Exception ex) { sb3.AppendLine("异常: " + ex); }
            try
            {
                var dir = AppPaths.Current.Root;
                File.WriteAllText(Path.Combine(dir, "friendtest.txt"), sb3.ToString(), Encoding.UTF8);
            }
            catch { }
            Shutdown(0);
            return;
        }

        // 对局详情布局验证:--matchdemo。免登录用演示数据开记分板窗。
        if (e.Args.Contains("--matchdemo"))
        {
            Stats.MatchDetailWindow.ShowDemo();
            return;
        }

        // 对局翻页验证:--matchtest。用缓存登录态实测 queryMatchList,写 matchtest.txt。
        if (e.Args.Contains("--matchtest"))
        {
            var sb2 = new StringBuilder();
            try { Task.Run(() => Services.Overwatch.OwProbe.RunMatchTestAsync(sb2)).GetAwaiter().GetResult(); }
            catch (Exception ex) { sb2.AppendLine("异常: " + ex); }
            try
            {
                var dir = AppPaths.Current.Root;
                File.WriteAllText(Path.Combine(dir, "matchtest.txt"), sb2.ToString(), Encoding.UTF8);
            }
            catch { }
            Shutdown(0);
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

        // 守望先锋战绩链路验证:--owtest。扫码 → 遍历账号库拉取翻译 → 写 owtest.txt 并打开。
        if (e.Args.Contains("--owtest"))
        {
            RunOwTest();
            Shutdown(0);
            return;
        }

        // 单实例:已经有一个在跑,就唤起它的窗口然后本进程退出(避免托盘出现两个图标)。
        _singleInstance = new Mutex(true, MutexName, out bool isNew);
        if (!isNew)
        {
            try { EventWaitHandle.OpenExisting(ShowEventName).Set(); } catch { /* 对面可能还没建好句柄 */ }
            Shutdown(0);
            return;
        }

        // 关掉 WPF 硬件加速:进程内不再建 D3D 设备,游戏加加 / RTSS 等 FPS 悬浮窗就不会把本程序
        // 当游戏挂性能条(它们靠 hook D3D Present 出条)。本工具是静态界面,软件渲染无感。
        System.Windows.Media.RenderOptions.ProcessRenderMode = System.Windows.Interop.RenderMode.SoftwareOnly;

        // 应用亮/暗主题(记住上次选择)。
        try { ThemeManager.Apply(AppSettings.Load().DarkMode); }
        catch { /* ignore */ }

        // 未捕获异常写日志,避免直接崩溃后无从查起。
        DispatcherUnhandledException += OnUnhandled;
        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
            WriteCrash(args.ExceptionObject as Exception);

        base.OnStartup(e);
        var settings = AppSettings.Load();
        var startHidden = global::CloudLightBlizzard.MainWindow.ShouldStartHidden(e.Args, settings.StartMinimized);
        var mainWindow = new global::CloudLightBlizzard.MainWindow(startHidden);
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

    private static void RunSwitch(string idStr)
    {
        var sb = new StringBuilder();
        try
        {
            if (!long.TryParse(idStr, out var targetId))
            {
                sb.AppendLine("用法: CloudLight Blizzard.exe --switch <accountId>");
            }
            else
            {
                var paths = new BattleNetPaths();
                var reader = new AccountReader(paths);
                var profiles = new AppDataStore(paths);
                var controller = new BattleNetController(paths);

                if (!profiles.HasProfile(targetId))
                {
                    sb.AppendLine($"账号 {targetId} 没有快照,无法切换。");
                }
                else
                {
                    var accounts = reader.ReadAccounts(out var before);
                    string TagOf(long id) =>
                        accounts.FirstOrDefault(a => a.AccountId == id)?.BattleTag ?? id.ToString();

                    sb.AppendLine($"切换前当前账号: {(before?.ToString() ?? "未知")} {(before is long b ? TagOf(b) : "")}");
                    sb.AppendLine($"目标账号: {targetId} {TagOf(targetId)}");

                    var stopped = controller.GracefulQuit();
                    sb.AppendLine($"战网已关闭:{stopped}");
                    if (!stopped)
                    {
                        sb.AppendLine("战网未能退出,已中止。");
                    }
                    else
                    {
                        // 不自动备份当前号(避免把出错状态覆盖好快照)。只注入目标令牌,绝不登出。
                        profiles.Restore(targetId);
                        sb.AppendLine($"已注入目标快照 {targetId}");
                        controller.LaunchClient();
                        sb.AppendLine("已启动战网。");
                    }
                }
            }
        }
        catch (Exception ex)
        {
            sb.AppendLine("切换异常: " + ex);
        }

        try
        {
            var dir = AppPaths.Current.Root;
            Directory.CreateDirectory(dir);
            File.WriteAllText(Path.Combine(dir, "switch.txt"), sb.ToString(), Encoding.UTF8);
        }
        catch { /* ignore */ }
    }

    private static void RunAddAccount()
    {
        var sb = new StringBuilder();
        try
        {
            var paths = new BattleNetPaths();
            var profiles = new AppDataStore(paths);
            var controller = new BattleNetController(paths);

            var stopped = controller.GracefulQuit();
            sb.AppendLine($"战网已关闭:{stopped}");
            if (stopped)
            {
                profiles.ClearCurrentPointer();       // 清本地指针(文件层),非登出
                sb.AppendLine("已清空登录指针(未登出、未碰注册表)。");
                controller.LaunchClient();
                sb.AppendLine("已启动到登录页,请手动登录新号,登录后回工具『保存当前登录为快照』。");
            }
        }
        catch (Exception ex)
        {
            sb.AppendLine("异常: " + ex);
        }

        try
        {
            var dir = AppPaths.Current.Root;
            Directory.CreateDirectory(dir);
            File.WriteAllText(Path.Combine(dir, "addaccount.txt"), sb.ToString(), Encoding.UTF8);
        }
        catch { /* ignore */ }
    }

    // 战绩链路验证(--owtest):在线程池跑异步流程,避免阻塞 UI 线程 dispatcher 造成死锁。
    private static void RunOwTest()
    {
        var sb = new StringBuilder();
        try { Task.Run(() => Services.Overwatch.OwProbe.RunAsync(sb)).GetAwaiter().GetResult(); }
        catch (Exception ex) { sb.AppendLine("异常: " + ex); }
        try
        {
            var dir = AppPaths.Current.Root;
            Directory.CreateDirectory(dir);
            var p = Path.Combine(dir, "owtest.txt");
            File.WriteAllText(p, sb.ToString(), Encoding.UTF8);
            try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo { FileName = p, UseShellExecute = true }); } catch { }
        }
        catch { /* ignore */ }
    }

    private void OnUnhandled(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        WriteCrash(e.Exception);
        MessageBox.Show(e.Exception.Message, "出错了", MessageBoxButton.OK, MessageBoxImage.Error);
        e.Handled = true;
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
