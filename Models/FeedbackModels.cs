using System.IO;

namespace CloudLightBlizzard.Models;

public enum FeedbackFailureKind
{
    None,
    NetworkUnavailable,
    ProxyUnavailable,
    ProxyAndDirectUnavailable,
    InvalidProxy,
    Timeout,
    Cancelled,
    PackageFailed,
    ServerRejected,
    PayloadTooLarge,
    ServerUnavailable,
    RateLimited,
    GithubUnavailable,
    GithubTimeout,
    GithubConfiguration,
    GithubRateLimited,
    GithubAssetUploadFailed,
    GithubIssueCreateFailed,
}

public enum FeedbackUploadStage { Uploading, ServerProcessing }

public sealed record FeedbackUploadProgress(long BytesSent, long TotalBytes)
{
    public int Percentage => TotalBytes <= 0 ? 0 : (int)Math.Clamp(BytesSent * 100 / TotalBytes, 0, 100);
    public FeedbackUploadStage Stage => TotalBytes > 0 && BytesSent >= TotalBytes
        ? FeedbackUploadStage.ServerProcessing : FeedbackUploadStage.Uploading;
}

public sealed record FeedbackSubmitRequest(
    string Title,
    string Description,
    string AppVersion,
    string OsVersion,
    string Contact,
    string ClientSubmissionId,
    string? LogsZipPath);

public sealed record FeedbackSubmitResult(bool Success, string? ReportId, FeedbackFailureKind Failure,
    string? Detail = null, int? IssueNumber = null, string? IssueUrl = null);

public sealed record FeedbackLogPreview(string SourcePath, string ArchiveName, long IncludedBytes);

public sealed record FeedbackPackage(string FilePath, IReadOnlyList<FeedbackLogPreview> Logs, long Length)
{
    public void Delete()
    {
        try { if (File.Exists(FilePath)) File.Delete(FilePath); } catch { }
    }
}
