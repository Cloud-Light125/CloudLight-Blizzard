using System.IO;
using System.Text;

namespace BnetSwitch.Services.OverwatchRegion;

public static class RegionSwitchLog
{
    private static readonly object Gate = new();
    internal static string? FileOverride { get; set; }

    private static string LogFile => FileOverride ?? Path.Combine(
        AppPaths.Current.LogsDir, "region-switch.log");

    public static void Write(string eventName, OverwatchRegion? target = null,
        CurrentGameRegion? current = null, GenerationCompatibility? compatibility = null,
        string? generationId = null, string? detail = null)
    {
        try
        {
            var line = new StringBuilder()
                .Append(DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff"))
                .Append(" | ").Append(eventName);
            if (target is not null) line.Append(" | TargetRegion=").Append(target);
            if (current is not null) line.Append(" | CurrentDetectedRegion=").Append(current);
            if (compatibility is not null) line.Append(" | GenerationCompatibility=").Append(compatibility);
            if (!string.IsNullOrWhiteSpace(generationId)) line.Append(" | ActiveGenerationId=").Append(generationId);
            if (!string.IsNullOrWhiteSpace(detail)) line.Append(" | ").Append(detail.Replace('\r', ' ').Replace('\n', ' '));
            lock (Gate)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(LogFile)!);
                File.AppendAllText(LogFile, line.AppendLine().ToString(), Encoding.UTF8);
            }
        }
        catch
        {
            // Logging must never make a switch fail.
        }
    }
}
