using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Security.Authentication;
using System.Text.Json;
using System.Text.Json.Serialization;
using CloudLightBlizzard.Models;

namespace CloudLightBlizzard.Services;

public enum UpdateDownloadPhase
{
    Preparing,
    Downloading,
    WaitingRetry,
    Verifying,
}

public sealed record UpdateDownloadProgress(
    long BytesReceived,
    long? TotalBytes,
    UpdateDownloadPhase Phase = UpdateDownloadPhase.Downloading)
{
    public int? Percentage => TotalBytes is > 0
        ? (int)Math.Clamp(BytesReceived * 100L / TotalBytes.Value, 0, 100)
        : null;
    public int RetryAttempt { get; init; }
    public int MaxRetries { get; init; }
    public TimeSpan? RetryDelay { get; init; }
    public bool Resumed { get; init; }
}

/// <summary>
/// Installer downloader with bounded retry, resumable partial files, metadata validation,
/// and strict size/MZ/SHA-256 verification. The caller owns the CancellationTokenSource.
/// </summary>
public sealed class UpdateDownloadService
{
    private const int BufferSize = 128 * 1024;
    private const int MaxRetryCount = 3;
    private const int MaxAttempts = MaxRetryCount + 1;
    private readonly CloudHttpClientFactory _httpClients;
    private readonly TimeSpan[] _retryDelays;
    private readonly object _stateGate = new();
    private UpdaterState _state = UpdaterState.Idle;

    public UpdateDownloadService(CloudHttpClientFactory httpClients,
        IReadOnlyList<TimeSpan>? retryDelays = null)
    {
        _httpClients = httpClients ?? throw new ArgumentNullException(nameof(httpClients));
        _retryDelays = (retryDelays is { Count: > 0 } ? retryDelays : new[]
        {
            TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(5),
        }).ToArray();
    }

    public UpdaterState State { get { lock (_stateGate) return _state; } private set { lock (_stateGate) _state = value; StateChanged?.Invoke(value); } }
    public event Action<UpdaterState>? StateChanged;
    public string? LastPartialPath { get; private set; }
    public string? LastMetadataPath { get; private set; }

    public async Task<string> DownloadInstallerAsync(
        UpdateCheckResult result,
        IProgress<UpdateDownloadProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(result);
        string latestVersion;
        Uri downloadUri;
        string installerName;
        try
        {
            latestVersion = UpdateService.NormalizeReleaseVersion(result.LatestVersion)
                ?? throw new InvalidOperationException("更新服务返回的版本号无效。");
            downloadUri = ValidateInstallerUri(result, latestVersion);
            if (!UpdateService.IsValidSha256Digest(result.InstallerDigest))
                throw new InvalidDataException("更新服务没有提供有效的 SHA-256 摘要，已拒绝安装包。");
            installerName = string.IsNullOrWhiteSpace(result.InstallerName)
                ? $"CloudLight-Blizzard-{latestVersion}-win-x64-Setup.exe"
                : Path.GetFileName(result.InstallerName);
            if (string.IsNullOrWhiteSpace(installerName) ||
                !installerName.EndsWith("-win-x64-Setup.exe", StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("更新服务返回的安装包文件名无效。");
        }
        catch
        {
            State = UpdaterState.Failed;
            throw;
        }
        var releaseKey = SafeReleaseKey(result.Tag, latestVersion);
        var root = Path.Combine(Path.GetTempPath(), "CloudLight Blizzard", "updates", releaseKey);
        Directory.CreateDirectory(root);
        var finalPath = Path.Combine(root, installerName);
        var partialPath = finalPath + ".partial";
        var metadataPath = Path.Combine(root, "update-download.json");
        LastPartialPath = partialPath;
        LastMetadataPath = metadataPath;

        State = UpdaterState.Preparing;
        progress?.Report(new UpdateDownloadProgress(0, ExpectedLength(result, null), UpdateDownloadPhase.Preparing));
        if (File.Exists(finalPath))
        {
            try
            {
                await ValidateInstallerFileAsync(finalPath, result.InstallerSize, result.InstallerDigest,
                    cancellationToken).ConfigureAwait(false);
                State = UpdaterState.ReadyToInstall;
                return finalPath;
            }
            catch { TryDelete(finalPath); }
        }

        var metadata = LoadMetadata(metadataPath);
        var partialSize = File.Exists(partialPath) ? new FileInfo(partialPath).Length : 0;
        if (!IsMetadataCompatible(metadata, latestVersion, downloadUri, result, partialSize))
        {
            TryDelete(partialPath);
            TryDelete(metadataPath);
            metadata = null;
            partialSize = 0;
        }
        if (result.InstallerSize > 0 && partialSize > result.InstallerSize)
        {
            TryDelete(partialPath);
            TryDelete(metadataPath);
            metadata = null;
            partialSize = 0;
        }

        var finalPathReplaced = false;
        var keepPartialOnFailure = false;
        try
        {
            long received = partialSize;
            long expectedLength = result.InstallerSize;
            string? etag = metadata?.ETag;
            string? lastModified = metadata?.LastModified;
            var resumed = partialSize > 0;
            for (var attempt = 1; ; attempt++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                try
                {
                    State = UpdaterState.Downloading;
                    var responseResult = await DownloadAttemptAsync(downloadUri, result, partialPath, metadata,
                        received, progress, cancellationToken).ConfigureAwait(false);
                    received = responseResult.BytesReceived;
                    expectedLength = responseResult.ExpectedLength > 0 ? responseResult.ExpectedLength : expectedLength;
                    etag = responseResult.ETag ?? etag;
                    lastModified = responseResult.LastModified ?? lastModified;
                    resumed |= responseResult.Resumed;
                    metadata = new ResumeMetadata
                    {
                        Version = latestVersion,
                        DownloadUrl = downloadUri.AbsoluteUri,
                        ExpectedSize = expectedLength,
                        Digest = result.InstallerDigest,
                        DownloadedBytes = received,
                        ETag = etag,
                        LastModified = lastModified,
                    };
                    SaveMetadata(metadataPath, metadata);
                    break;
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    State = UpdaterState.Cancelled;
                    received = File.Exists(partialPath) ? new FileInfo(partialPath).Length : received;
                    SaveMetadataBestEffort(metadataPath, latestVersion, downloadUri, result, received, etag, lastModified);
                    throw;
                }
                catch (Exception ex) when (IsRetryable(ex) && attempt < MaxAttempts)
                {
                    keepPartialOnFailure = true;
                    received = File.Exists(partialPath) ? new FileInfo(partialPath).Length : received;
                    SaveMetadataBestEffort(metadataPath, latestVersion, downloadUri, result, received, etag, lastModified);
                    var retryIndex = Math.Min(attempt - 1, _retryDelays.Length - 1);
                    var delay = _retryDelays[Math.Max(0, retryIndex)];
                    State = UpdaterState.WaitingRetry;
                    progress?.Report(new UpdateDownloadProgress(received,
                        ExpectedLength(result, expectedLength), UpdateDownloadPhase.WaitingRetry)
                    {
                        RetryAttempt = attempt,
                        MaxRetries = MaxRetryCount,
                        RetryDelay = delay,
                        Resumed = resumed,
                    });
                    await Task.Delay(delay, cancellationToken).ConfigureAwait(false);
                }
                catch
                {
                    State = UpdaterState.Failed;
                    throw;
                }
            }

            cancellationToken.ThrowIfCancellationRequested();
            State = UpdaterState.Verifying;
            progress?.Report(new UpdateDownloadProgress(received,
                ExpectedLength(result, expectedLength), UpdateDownloadPhase.Verifying) { Resumed = resumed });
            await ValidateInstallerFileAsync(partialPath, expectedLength, result.InstallerDigest,
                cancellationToken).ConfigureAwait(false);
            File.Move(partialPath, finalPath, true);
            finalPathReplaced = true;
            await ValidateInstallerFileAsync(finalPath, expectedLength, result.InstallerDigest,
                cancellationToken).ConfigureAwait(false);
            TryDelete(metadataPath);
            State = UpdaterState.ReadyToInstall;
            return finalPath;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            State = UpdaterState.Cancelled;
            throw;
        }
        catch
        {
            if (State != UpdaterState.Failed) State = UpdaterState.Failed;
            if (!keepPartialOnFailure)
            {
                TryDelete(partialPath);
                TryDelete(metadataPath);
            }
            if (finalPathReplaced) TryDelete(finalPath);
            throw;
        }
    }

    public void MarkLaunchingInstaller() => State = UpdaterState.LaunchingInstaller;
    public void MarkFailed() => State = UpdaterState.Failed;
    public void MarkCompleted() => State = UpdaterState.Completed;

    public static string FormatBytes(long bytes)
    {
        if (bytes < 1024) return $"{bytes} B";
        if (bytes < 1024 * 1024) return $"{bytes / 1024d:0.0} KB";
        return $"{bytes / 1024d / 1024d:0.0} MB";
    }

    private async Task<AttemptResult> DownloadAttemptAsync(Uri uri, UpdateCheckResult result,
        string partialPath, ResumeMetadata? metadata, long existingBytes,
        IProgress<UpdateDownloadProgress>? progress, CancellationToken token)
    {
        var request = CreateInstallerRequest(uri, existingBytes, metadata);
        using var response = await _httpClients.SendGetAsync(() => request, "update-download", token)
            .ConfigureAwait(false);
        if (existingBytes > 0 && (response.StatusCode == HttpStatusCode.OK ||
                                  response.StatusCode == HttpStatusCode.RequestedRangeNotSatisfiable))
        {
            // A proxy/CDN may ignore Range (200), or the partial may no longer describe a
            // valid range (416). Never append a response to an old or incompatible asset.
            TryDelete(partialPath);
            TryDelete(Path.Combine(Path.GetDirectoryName(partialPath)!, "update-download.json"));
            return await DownloadAttemptAsync(uri, result, partialPath, null, 0, progress, token).ConfigureAwait(false);
        }
        if (!response.IsSuccessStatusCode)
            throw new HttpRequestException($"下载安装包失败：HTTP {(int)response.StatusCode}。", null, response.StatusCode);
        var resumed = existingBytes > 0;
        if (resumed)
        {
            var contentRange = response.Content.Headers.ContentRange;
            if (response.StatusCode != HttpStatusCode.PartialContent || contentRange?.From != existingBytes ||
                contentRange.To is null || contentRange.Length is null ||
                result.InstallerSize > 0 && contentRange.Length != result.InstallerSize)
            {
                TryDelete(partialPath);
                TryDelete(Path.Combine(Path.GetDirectoryName(partialPath)!, "update-download.json"));
                return await DownloadAttemptAsync(uri, result, partialPath, null, 0, progress, token).ConfigureAwait(false);
            }
            if (metadata?.ETag is { Length: > 0 } expectedTag &&
                !string.Equals(expectedTag, response.Headers.ETag?.Tag, StringComparison.Ordinal))
            {
                TryDelete(partialPath);
                TryDelete(Path.Combine(Path.GetDirectoryName(partialPath)!, "update-download.json"));
                return await DownloadAttemptAsync(uri, result, partialPath, null, 0, progress, token).ConfigureAwait(false);
            }
        }
        var expected = result.InstallerSize > 0 ? result.InstallerSize
            : response.Content.Headers.ContentLength is > 0
                ? existingBytes + response.Content.Headers.ContentLength.Value : 0;
        progress?.Report(new UpdateDownloadProgress(existingBytes, expected > 0 ? expected : null,
            UpdateDownloadPhase.Downloading) { Resumed = resumed });
        var mode = resumed ? FileMode.Append : FileMode.Create;
        await using var input = await response.Content.ReadAsStreamAsync(token).ConfigureAwait(false);
        await using var output = new FileStream(partialPath, mode, FileAccess.Write, FileShare.Read,
            BufferSize, FileOptions.Asynchronous | FileOptions.SequentialScan);
        var received = existingBytes;
        var buffer = new byte[BufferSize];
        while (true)
        {
            var read = await input.ReadAsync(buffer.AsMemory(0, buffer.Length), token).ConfigureAwait(false);
            if (read == 0) break;
            await output.WriteAsync(buffer.AsMemory(0, read), token).ConfigureAwait(false);
            received += read;
            progress?.Report(new UpdateDownloadProgress(received, expected > 0 ? expected : null,
                UpdateDownloadPhase.Downloading) { Resumed = resumed });
        }
        await output.FlushAsync(token).ConfigureAwait(false);
        return new AttemptResult(received, expected,
            response.Headers.ETag?.Tag,
            response.Content.Headers.LastModified?.ToString("R") ?? GetHeaderValue(response.Headers, "Last-Modified"),
            resumed);
    }

    private static string? GetHeaderValue(HttpHeaders headers, string name) =>
        headers.TryGetValues(name, out var values) ? values.FirstOrDefault() : null;

    private static HttpRequestMessage CreateInstallerRequest(Uri uri, long existingBytes, ResumeMetadata? metadata)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, uri);
        request.Headers.UserAgent.ParseAdd("CloudLight-Blizzard");
        request.Headers.Accept.ParseAdd("application/octet-stream");
        if (existingBytes > 0)
        {
            request.Headers.Range = new RangeHeaderValue(existingBytes, null);
            if (metadata?.ETag is { Length: > 0 } etag) request.Headers.TryAddWithoutValidation("If-Range", etag);
            else if (metadata?.LastModified is { Length: > 0 } modified) request.Headers.TryAddWithoutValidation("If-Range", modified);
        }
        return request;
    }

    private static long? ExpectedLength(UpdateCheckResult result, long? responseLength) =>
        result.InstallerSize > 0 ? result.InstallerSize : responseLength is > 0 ? responseLength : null;

    private static bool IsMetadataCompatible(ResumeMetadata? metadata, string version, Uri uri,
        UpdateCheckResult result, long partialSize) =>
        partialSize == 0 || metadata is not null &&
        string.Equals(metadata.Version, version, StringComparison.OrdinalIgnoreCase) &&
        string.Equals(metadata.DownloadUrl, uri.AbsoluteUri, StringComparison.Ordinal) &&
        (result.InstallerSize <= 0 || metadata.ExpectedSize == result.InstallerSize) &&
        string.Equals(metadata.Digest, result.InstallerDigest, StringComparison.OrdinalIgnoreCase) &&
        metadata.DownloadedBytes == partialSize;

    private static ResumeMetadata? LoadMetadata(string path)
    {
        try { return File.Exists(path) ? JsonSerializer.Deserialize<ResumeMetadata>(File.ReadAllText(path)) : null; }
        catch { return null; }
    }

    private static void SaveMetadata(string path, ResumeMetadata metadata)
    {
        var temp = path + ".tmp";
        File.WriteAllText(temp, JsonSerializer.Serialize(metadata, JsonOptions));
        File.Move(temp, path, true);
    }

    private static void SaveMetadataBestEffort(string path, string version, Uri uri,
        UpdateCheckResult result, long bytes, string? etag, string? lastModified)
    {
        try
        {
            SaveMetadata(path, new ResumeMetadata
            {
                Version = version, DownloadUrl = uri.AbsoluteUri, ExpectedSize = result.InstallerSize,
                Digest = result.InstallerDigest, DownloadedBytes = bytes, ETag = etag, LastModified = lastModified,
            });
        }
        catch { }
    }

    private static async Task ValidateInstallerFileAsync(string path, long expectedLength,
        string? expectedDigest, CancellationToken token)
    {
        await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read,
            BufferSize, FileOptions.Asynchronous | FileOptions.SequentialScan);
        if (expectedLength > 0 && stream.Length != expectedLength)
            throw new InvalidDataException($"安装包大小校验失败：应为 {expectedLength} 字节，实际为 {stream.Length} 字节。");
        if (stream.Length < 2 || stream.ReadByte() != 'M' || stream.ReadByte() != 'Z')
            throw new InvalidDataException("下载内容不是有效的 Windows 安装程序。");
        if (!UpdateService.IsValidSha256Digest(expectedDigest))
            throw new InvalidDataException("安装包 SHA-256 摘要缺失或格式无效。");
        stream.Position = 0;
        var actual = Convert.ToHexString(await SHA256.HashDataAsync(stream, token).ConfigureAwait(false));
        var expected = expectedDigest!["sha256:".Length..];
        if (!string.Equals(actual, expected, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("安装包 SHA-256 校验失败，已拒绝运行。");
    }

    private static Uri ValidateInstallerUri(UpdateCheckResult result, string latestVersion)
    {
        if (string.IsNullOrWhiteSpace(result.InstallerDownloadUrl) ||
            !Uri.TryCreate(result.InstallerDownloadUrl.Trim(), UriKind.Absolute, out var uri) ||
            !string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(uri.Host, "github.com", StringComparison.OrdinalIgnoreCase) ||
            !string.IsNullOrEmpty(uri.UserInfo))
            throw new InvalidOperationException("当前版本没有可用的在线更新安装包。");
        var expectedPrefix = $"/{UpdateService.GitHubOwner}/{UpdateService.GitHubRepository}/releases/download/";
        if (!uri.AbsolutePath.StartsWith(expectedPrefix, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("在线更新安装包地址未通过安全校验。");
        var expectedName = string.IsNullOrWhiteSpace(result.InstallerName)
            ? $"CloudLight-Blizzard-{latestVersion}-win-x64-Setup.exe"
            : Path.GetFileName(result.InstallerName);
        var actualName = Uri.UnescapeDataString(uri.Segments[^1]);
        if (!string.Equals(actualName, expectedName, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("在线更新安装包文件名与版本不匹配。");
        return uri;
    }

    private static string SafeReleaseKey(string? tag, string fallback)
    {
        var value = string.IsNullOrWhiteSpace(tag) ? fallback : tag!.Trim().TrimStart('v', 'V');
        var safe = new string(value.Where(character => char.IsLetterOrDigit(character) ||
            character is '.' or '-' or '_').ToArray());
        return string.IsNullOrWhiteSpace(safe) ? fallback : safe;
    }

    private static bool IsRetryable(Exception ex)
    {
        // HttpClient/SocketsHttpHandler reports a request timeout as a
        // TaskCanceledException when the caller token itself was not cancelled.
        // The surrounding catch already filters genuine user cancellation.
        if (ex is OperationCanceledException) return true;
        if (ex is HttpRequestException http)
        {
            if (IsCertificateError(http)) return false;
            return http.StatusCode is null or HttpStatusCode.BadGateway or HttpStatusCode.ServiceUnavailable or HttpStatusCode.GatewayTimeout;
        }
        if (ex is CloudNetworkException network) return !IsCertificateError(network);
        return ex is IOException or TimeoutException;
    }

    private static bool IsCertificateError(Exception ex)
    {
        for (Exception? current = ex; current is not null; current = current.InnerException)
        {
            if (current is AuthenticationException ||
                current.Message.Contains("certificate", StringComparison.OrdinalIgnoreCase) ||
                current.Message.Contains("SSL", StringComparison.OrdinalIgnoreCase) ||
                current.Message.Contains("TLS", StringComparison.OrdinalIgnoreCase))
                return true;
        }
        return false;
    }

    private static void TryDelete(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); } catch { }
    }

    private sealed record AttemptResult(long BytesReceived, long ExpectedLength, string? ETag,
        string? LastModified, bool Resumed);

    private sealed class ResumeMetadata
    {
        public string Version { get; init; } = "";
        public string DownloadUrl { get; init; } = "";
        public long ExpectedSize { get; init; }
        public string? Digest { get; init; }
        public long DownloadedBytes { get; init; }
        public string? ETag { get; init; }
        public string? LastModified { get; init; }
    }

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
    };
}
