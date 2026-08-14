namespace CloudLightBlizzard.Models;

public enum UpdateCheckResultStatus
{
    Success,
    NoRelease,
    Failed,
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
    public DateTimeOffset? PublishedAt { get; init; }
    public string? InstallerDownloadUrl { get; init; }
    public string? ErrorMessage { get; init; }
}
