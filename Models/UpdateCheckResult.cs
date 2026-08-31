namespace CloudLightBlizzard.Models;

public enum UpdateCheckResultStatus
{
    Success,
    NoRelease,
    Failed,
}

public enum UpdateFailureKind
{
    None,
    NetworkUnavailable,
    ProxyUnavailable,
    Timeout,
    Http5xx,
    RateLimited,
    InvalidResponse,
}

public sealed class UpdateCheckResult
{
    public UpdateCheckResultStatus Status { get; init; }
    public string CurrentVersion { get; init; } = "";
    public string LatestVersion { get; init; } = "";
    public bool HasUpdate { get; init; }
    public string ReleaseName { get; init; } = "";
    public string ReleaseNotes { get; init; } = "";
    public string ReleaseUrl { get; init; } = "";
    public string Tag { get; init; } = "";
    public DateTimeOffset? PublishedAt { get; init; }
    public string? InstallerDownloadUrl { get; init; }
    public string? InstallerName { get; init; }
    public long InstallerSize { get; init; }
    public string? InstallerDigest { get; init; }
    public int? HttpStatusCode { get; init; }
    public UpdateChannel Channel { get; init; } = UpdateChannel.Stable;
    public string? ErrorMessage { get; init; }
    public UpdateFailureKind FailureKind { get; init; }
    public DateTimeOffset? RetryAt { get; init; }
    public string? TechnicalDetail { get; init; }
}
