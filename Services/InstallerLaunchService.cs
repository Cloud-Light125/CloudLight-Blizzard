using System.Diagnostics;
using System.IO;

namespace CloudLightBlizzard.Services;

public interface IInstallerLauncher
{
    Process? Start(string installerPath);
}

public sealed class ProcessInstallerLauncher : IInstallerLauncher
{
    public Process? Start(string installerPath) =>
        Process.Start(new ProcessStartInfo
        {
            FileName = installerPath,
            UseShellExecute = true,
        });
}

public sealed class UpdateInstallerLaunchCoordinator
{
    private readonly IInstallerLauncher _launcher;
    private int _launchRequested;

    public UpdateInstallerLaunchCoordinator(IInstallerLauncher launcher)
    {
        _launcher = launcher ?? throw new ArgumentNullException(nameof(launcher));
    }

    public bool TryLaunchAndRequestShutdown(
        string installerPath,
        Action installerStarted,
        Action requestShutdown,
        out string error)
    {
        ArgumentNullException.ThrowIfNull(installerStarted);
        ArgumentNullException.ThrowIfNull(requestShutdown);

        if (Interlocked.CompareExchange(ref _launchRequested, 1, 0) != 0)
        {
            error = "安装程序已经启动或正在启动。";
            return false;
        }

        if (string.IsNullOrWhiteSpace(installerPath) || !File.Exists(installerPath))
        {
            Interlocked.Exchange(ref _launchRequested, 0);
            error = "已下载的安装程序不存在，请重新下载。";
            return false;
        }

        try
        {
            using var installerProcess = _launcher.Start(installerPath);
            if (installerProcess is null)
            {
                Interlocked.Exchange(ref _launchRequested, 0);
                error = "系统没有返回已启动的安装程序进程。";
                return false;
            }
        }
        catch (Exception ex)
        {
            Interlocked.Exchange(ref _launchRequested, 0);
            error = ex.Message;
            return false;
        }

        installerStarted();
        requestShutdown();
        error = "";
        return true;
    }
}
