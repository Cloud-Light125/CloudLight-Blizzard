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
    HttpError,
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
    public string CheckStatusText => Status switch
    {
        UpdateCheckResultStatus.Failed =>
            $"检查更新失败 · {ErrorMessage ?? "更新接口不可用。"}" +
            (HttpStatusCode is { } status ? $"（HTTP {status}）" : ""),
        UpdateCheckResultStatus.NoRelease => "更新服务暂无正式发布版本。",
        _ when HasUpdate => $"发现新版本 {LatestVersion}",
        _ => $"没有新版本 · 最新公开版本 {LatestVersion}",
    };
}
