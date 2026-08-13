using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;

namespace BnetSwitch.Services;

/// <summary>
/// 控制战网客户端:优雅退出(不能强杀!)与启动。
///
/// 为什么不能 taskkill /F：强杀可能造成配置未写完。优先发送普通 WM_CLOSE，
/// 仅在客户端没有响应时才回退到原有会话结束消息；进程退出后还会等待配置文件稳定。
///
/// 注意:只等【客户端进程】退出,忽略 Agent.exe(后台更新代理,常驻/自启,
/// 不锁注册表、不影响令牌导入导出)。
/// </summary>
public sealed class BattleNetController
{
    private const uint WM_QUERYENDSESSION = 0x0011;
    private const uint WM_ENDSESSION = 0x0016;
    private const uint WM_CLOSE = 0x0010;
    private const uint SMTO_ABORTIFHUNG = 0x0002;

    [DllImport("user32.dll")]
    private static extern IntPtr SendMessageTimeout(
        IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam,
        uint flags, uint timeout, out IntPtr result);

    [DllImport("user32.dll")]
    private static extern bool EnumWindows(EnumWindowsProc callback, IntPtr param);

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);

    [DllImport("user32.dll")]
    private static extern bool ShowWindow(IntPtr hWnd, int cmd);

    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern bool IsWindowVisible(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern int GetWindowTextLength(IntPtr hWnd);

    private const int SW_RESTORE = 9;

    private delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr param);

    // 客户端进程(不含 Agent)
    private static readonly string[] ClientProcs =
    {
        "Battle.net", "Battle.net Launcher", "Battle.net Helper",
    };

    private readonly BattleNetPaths _paths;

    public BattleNetController(BattleNetPaths paths) => _paths = paths;

    /// <summary>客户端(不含 Agent)是否在运行。</summary>
    public bool IsClientRunning() =>
        ClientProcs.SelectMany(Process.GetProcessesByName).Any();

    /// <summary>
    /// 优雅退出战网:向所有带主窗口的 Battle.net 进程发送会话结束消息,然后等待客户端全部退出。
    /// 返回是否已完全退出。绝不强杀。
    /// </summary>
    public bool GracefulQuit()
    {
        // 收集所有战网客户端进程的 PID。
        var procs = Process.GetProcessesByName("Battle.net");
        var pids = new HashSet<uint>(procs.Select(p => (uint)p.Id));
        foreach (var p in procs) p.Dispose();

        if (pids.Count > 0)
        {
            // 枚举【所有顶层窗口】(含最小化到托盘的、不可见的),不依赖不稳定的 MainWindowHandle。
            var targets = new List<IntPtr>();
            EnumWindowsProc collector = (h, _) =>
            {
                GetWindowThreadProcessId(h, out var pid);
                if (pids.Contains(pid)) targets.Add(h);
                return true;
            };
            EnumWindows(collector, IntPtr.Zero);
            GC.KeepAlive(collector);

            // 先模拟用户关闭窗口。只有客户端仍未退出时，才使用其已支持的会话结束消息。
            foreach (var h in targets)
                SendMessageTimeout(h, WM_CLOSE, IntPtr.Zero, IntPtr.Zero,
                    SMTO_ABORTIFHUNG, 5000, out _);

            if (WaitUntilStopped(40, 250))
                return WaitForRoamingFilesQuiet();

            foreach (var h in targets)
            {
                SendMessageTimeout(h, WM_QUERYENDSESSION, IntPtr.Zero, IntPtr.Zero,
                    SMTO_ABORTIFHUNG, 5000, out _);
                SendMessageTimeout(h, WM_ENDSESSION, (IntPtr)1, IntPtr.Zero,
                    SMTO_ABORTIFHUNG, 5000, out _);
            }
        }

        return WaitUntilStopped() && WaitForRoamingFilesQuiet();
    }

    /// <summary>进程结束后继续观察账号配置，确认大小和写入时间稳定后才允许复制。</summary>
    public bool WaitForRoamingFilesQuiet(int quietMilliseconds = 1200, int timeoutMilliseconds = 8000)
    {
        var started = Environment.TickCount64;
        var stableSince = Environment.TickCount64;
        var previous = CaptureRoamingStamp();
        while (Environment.TickCount64 - started < timeoutMilliseconds)
        {
            Thread.Sleep(250);
            var current = CaptureRoamingStamp();
            if (!string.Equals(current, previous, StringComparison.Ordinal))
            {
                previous = current;
                stableSince = Environment.TickCount64;
            }
            else if (Environment.TickCount64 - stableSince >= quietMilliseconds)
            {
                return true;
            }
        }
        return false;
    }

    private string CaptureRoamingStamp()
    {
        try
        {
            if (!Directory.Exists(_paths.RoamingDir)) return "";
            return string.Join("|", Directory.EnumerateFiles(_paths.RoamingDir, "*", SearchOption.AllDirectories)
                .Select(file =>
                {
                    var info = new FileInfo(file);
                    return $"{Path.GetRelativePath(_paths.RoamingDir, file)}:{info.Length}:{info.LastWriteTimeUtc.Ticks}";
                }).OrderBy(value => value, StringComparer.OrdinalIgnoreCase));
        }
        catch { return Guid.NewGuid().ToString(); }
    }

    /// <summary>轮询直到客户端(不含 Agent)完全退出。</summary>
    public bool WaitUntilStopped(int attempts = 60, int intervalMs = 250)
    {
        for (var i = 0; i < attempts; i++)
        {
            if (!IsClientRunning()) return true;
            Thread.Sleep(intervalMs);
        }
        return !IsClientRunning();
    }

    /// <summary>
    /// 战网已经在跑就把它的窗口唤到前台,返回是否唤到了。
    /// 缩在托盘里的时候顶层窗是隐藏的,这里拿不到可见窗 → 返回 false,交给调用方再跑一次 exe
    /// (战网是单实例,第二次启动会把已有窗口自己拉出来)。
    /// </summary>
    public bool TryFocusClient()
    {
        var procs = Process.GetProcessesByName("Battle.net");
        var pids = new HashSet<uint>(procs.Select(p => (uint)p.Id));
        foreach (var p in procs) p.Dispose();
        if (pids.Count == 0) return false;

        var hit = IntPtr.Zero;
        EnumWindowsProc collector = (h, _) =>
        {
            GetWindowThreadProcessId(h, out var pid);
            // 只认「可见 + 有标题」的顶层窗:CEF 会开一堆不可见的辅助窗,唤那些没有任何效果
            if (!pids.Contains(pid) || !IsWindowVisible(h) || GetWindowTextLength(h) == 0) return true;
            hit = h;
            return false;
        };
        EnumWindows(collector, IntPtr.Zero);
        GC.KeepAlive(collector);
        if (hit == IntPtr.Zero) return false;

        ShowWindow(hit, SW_RESTORE);
        return SetForegroundWindow(hit);
    }

    public void LaunchClient()
    {
        var exe = _paths.ClientExe;
        if (string.IsNullOrEmpty(exe) || !File.Exists(exe))
            throw new FileNotFoundException("找不到 Battle.net.exe,请点『设置 exe 路径』手动指定。");

        Process.Start(new ProcessStartInfo
        {
            FileName = exe,
            UseShellExecute = true,
            WorkingDirectory = Path.GetDirectoryName(exe),
        });
    }
}
