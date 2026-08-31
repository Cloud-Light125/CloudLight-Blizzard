using System.IO;
using System.Text.Json;
using CloudLightBlizzard.Services.Drops;

namespace CloudLightBlizzard.Services;

public sealed class UpdateLog
{
    private readonly string _file;
    private readonly object _gate = new();

    public UpdateLog(string? file = null)
    {
        _file = file ?? Path.Combine(AppPaths.Current.LogsDir, "update.log");
    }

    public void Write(string eventName, UpdateCheckMode mode, string currentVersion,
        string? latestVersion = null, string? skippedVersion = null, bool? hasUpdate = null,
        string? detail = null)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_file)!);
            var record = new
            {
                module = "[updater]",
                timestamp = DateTimeOffset.Now,
                @event = eventName,
                mode = mode.ToString(),
                current = currentVersion,
                latest = latestVersion,
                skipped = skippedVersion,
                hasUpdate,
                detail = SensitiveDataRedactor.Redact(detail),
            };
            lock (_gate)
                File.AppendAllText(_file, JsonSerializer.Serialize(record) + Environment.NewLine);
        }
        catch { }
    }
}
