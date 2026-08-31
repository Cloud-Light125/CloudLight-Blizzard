using System.IO;

namespace CloudLightBlizzard.Services.Drops;

internal static class DropsRecoveryLog
{
    private static readonly object Gate = new();

    public static void Write(DropsPlatform platform, string title, string detail, DropsConnectionState state)
    {
        try
        {
            var directory = AppPaths.Current.LogsDir;
            Directory.CreateDirectory(directory);
            var safeDetail = SensitiveDataRedactor.Redact(detail).Replace('\r', ' ').Replace('\n', ' ');
            lock (Gate)
                File.AppendAllText(Path.Combine(directory, "drops-recovery.log"),
                    $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] [drops-recovery] platform={platform} state={state} title={title} detail={safeDetail}{Environment.NewLine}");
        }
        catch { }
    }
}
