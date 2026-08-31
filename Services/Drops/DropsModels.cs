using System.Text.Json;

namespace CloudLightBlizzard.Services.Drops;

public enum DropsPlatform { Soop, YouTube, Twitch, Bilibili }

public enum WorkerLifecycle { Stopped, Starting, Running, Stopping, Crashed }

public enum DropsConnectionState
{
    Disconnected,
    Connecting,
    Connected,
    Degraded,
    WaitingRetry,
    Recovering,
    Failed,
    Stopped,
}

public sealed record DropsProxySettings(bool EnableProxy, string ProxyUrl, bool FallbackDirect);

public sealed record DropsRuntimeDiagnosticSnapshot(
    string SoopStatus,
    string TwitchStatus,
    string YouTubeStatus,
    string SoopLastSuccess,
    string TwitchLastSuccess,
    string YouTubeLastSuccess,
    string RecentNetworkError)
{
    public string BilibiliStatus { get; init; } = "未运行";
    public string BilibiliLastSuccess { get; init; } = "无";
    public IReadOnlyList<DropsPlatformRecoveryDiagnostic> Platforms { get; init; } = Array.Empty<DropsPlatformRecoveryDiagnostic>();
    public IReadOnlyList<DropsRecoveryEvent> RecentEvents { get; init; } = Array.Empty<DropsRecoveryEvent>();
}

public sealed record DropsPlatformRecoveryDiagnostic(
    string Platform,
    DropsConnectionState State,
    DateTimeOffset? LastHeartbeatAt,
    DateTimeOffset? LastProgressAt,
    DateTimeOffset? LastReconnectAt,
    DateTimeOffset? NextRetryAt,
    int ConsecutiveFailures,
    int ReconnectCount,
    string WorkerHealth);

public sealed record WorkerEvent(DropsPlatform Platform, string Name, JsonElement Payload);

public sealed record WorkerSnapshot(
    DropsPlatform Platform,
    WorkerLifecycle Lifecycle,
    string Status,
    string Summary,
    DateTimeOffset? StartedAt,
    int? ProcessId,
    string? LastError);

public sealed record DropsRecoveryEvent(DateTimeOffset Timestamp, DropsPlatform Platform,
    string Title, string Detail, DropsConnectionState State)
{
    public string PlatformText => Platform switch
    {
        DropsPlatform.Soop => "SOOP",
        DropsPlatform.YouTube => "YouTube",
        DropsPlatform.Bilibili => "哔哩哔哩",
        _ => "Twitch",
    };
    public string TimeText => Timestamp.ToLocalTime().ToString("HH:mm:ss");
    public string StateText => State switch
    {
        DropsConnectionState.Connected => "已连接",
        DropsConnectionState.WaitingRetry => "等待重试",
        DropsConnectionState.Recovering => "恢复中",
        DropsConnectionState.Failed => "失败",
        _ => State.ToString(),
    };
}

public sealed record ImportCandidate(string RelativePath, bool Sensitive = false);

public enum ImportConflictAction { Skip, Overwrite, Cancel }

public sealed record ImportResult(bool Success, bool Cancelled, IReadOnlyList<string> Copied,
    IReadOnlyList<string> Skipped, IReadOnlyList<string> Failed);
