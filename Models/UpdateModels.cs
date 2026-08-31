using System.Text.Json.Serialization;

namespace CloudLightBlizzard.Models;

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum UpdateChannel
{
    Stable,
    Beta,
}

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum UpdaterState
{
    Idle,
    Checking,
    UpdateAvailable,
    Preparing,
    Downloading,
    Paused,
    WaitingRetry,
    Verifying,
    ReadyToInstall,
    LaunchingInstaller,
    Completed,
    Cancelled,
    Failed,
}
