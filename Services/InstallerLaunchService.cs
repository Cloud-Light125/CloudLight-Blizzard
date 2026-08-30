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

        if (string.IsNullOrWhiteSpace(installerPath) || !File.Exists(installerPath))
        {
            error = "已下载的安装程序不存在，请重新下载。";
            return false;
        }

        try
        {
            using var installerProcess = _launcher.Start(installerPath);
            if (installerProcess is null)
            {
                error = "系统没有返回已启动的安装程序进程。";
                return false;
            }
        }
        catch (Exception ex)
        {
            error = ex.Message;
            return false;
        }

        installerStarted();
        requestShutdown();
        error = "";
        return true;
    }
}
