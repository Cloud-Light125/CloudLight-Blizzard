using System.IO;
using System.Text.Json;

namespace CloudLightBlizzard.Services;

public sealed class AccountSwitchLog
{
    private readonly string _file;
    private readonly object _gate = new();

    public AccountSwitchLog(string? file = null)
    {
        _file = file ?? Path.Combine(AppPaths.Current.LogsDir, "account-switch.log");
    }

    public void Write(string eventName, long? sourceAccountId = null, long? targetAccountId = null,
        string? sourceRegion = null, string? targetRegion = null, string? detail = null)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_file)!);
            var record = new
            {
                timestamp = DateTimeOffset.Now,
                @event = eventName,
                sourceAccountId,
                targetAccountId,
                sourceRegion,
                targetRegion,
                detail,
            };
            lock (_gate)
                File.AppendAllText(_file, JsonSerializer.Serialize(record) + Environment.NewLine);
        }
        catch { }
    }
}
