using System.Text.Json;

namespace CloudLightBlizzard.Services.Drops;

public enum DropsPlatform { Soop, YouTube, Twitch }

public enum WorkerLifecycle { Stopped, Starting, Running, Stopping, Crashed }

public sealed record DropsProxySettings(bool EnableProxy, string ProxyUrl, bool FallbackDirect);

public sealed record WorkerEvent(DropsPlatform Platform, string Name, JsonElement Payload);

public sealed record WorkerSnapshot(
    DropsPlatform Platform,
    WorkerLifecycle Lifecycle,
    string Status,
    string Summary,
    DateTimeOffset? StartedAt,
    int? ProcessId,
    string? LastError);

public sealed record ImportCandidate(string RelativePath, bool Sensitive = false);

public enum ImportConflictAction { Skip, Overwrite, Cancel }

public sealed record ImportResult(bool Success, bool Cancelled, IReadOnlyList<string> Copied,
    IReadOnlyList<string> Skipped, IReadOnlyList<string> Failed);
