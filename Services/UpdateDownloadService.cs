using System.Net;
using CloudLightBlizzard.Models;
using System.Net.Http;
using System.IO;

namespace CloudLightBlizzard.Services;

public enum UpdateDownloadPhase
{
    Downloading,
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
}

public sealed class UpdateDownloadService
{
    private const int BufferSize = 128 * 1024;
    private readonly CloudHttpClientFactory _httpClients;

    public UpdateDownloadService(CloudHttpClientFactory httpClients)
    {
        _httpClients = httpClients ?? throw new ArgumentNullException(nameof(httpClients));
    }

    public async Task<string> DownloadInstallerAsync(
        UpdateCheckResult result,
        IProgress<UpdateDownloadProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(result);
        var latestVersion = UpdateService.NormalizeVersion(result.LatestVersion)
            ?? throw new InvalidOperationException("更新服务返回的版本号无效。");
        var downloadUri = ValidateInstallerUri(result, latestVersion);
        var installerName = $"CloudLight-Blizzard-{latestVersion}-win-x64-Setup.exe";
        var root = Path.Combine(Path.GetTempPath(), "CloudLight Blizzard", "updates", latestVersion);
        Directory.CreateDirectory(root);
        var finalPath = Path.Combine(root, installerName);
        var partialPath = finalPath + ".partial";

        TryDelete(partialPath);

        var finalPathReplaced = false;
        try
        {
            long received;
            long expectedLength;
            long? contentLength;
            HttpResponseMessage response;
            using (response = await _httpClients.SendGetAsync(
                       () => CreateInstallerRequest(downloadUri),
                       "update-download",
                       cancellationToken)
                   .ConfigureAwait(false))
            {
                if (!response.IsSuccessStatusCode)
                    throw new HttpRequestException($"下载安装包失败：HTTP {(int)response.StatusCode}。", null, response.StatusCode);

                contentLength = response.Content.Headers.ContentLength;
                expectedLength = result.InstallerSize > 0
                    ? result.InstallerSize
                    : contentLength is > 0 ? contentLength.Value : 0;
                received = 0;
                progress?.Report(new UpdateDownloadProgress(
                    0, expectedLength > 0 ? expectedLength : null));

                await using (var input = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false))
                await using (var output = new FileStream(
                                 partialPath, FileMode.Create, FileAccess.Write, FileShare.None,
                                 BufferSize, FileOptions.Asynchronous | FileOptions.SequentialScan))
                {
                    var buffer = new byte[BufferSize];
                    while (true)
                    {
                        var read = await input.ReadAsync(buffer.AsMemory(0, buffer.Length), cancellationToken)
                            .ConfigureAwait(false);
                        if (read == 0) break;
                        await output.WriteAsync(buffer.AsMemory(0, read), cancellationToken).ConfigureAwait(false);
                        received += read;
                        progress?.Report(new UpdateDownloadProgress(
                            received, expectedLength > 0 ? expectedLength : contentLength));
                    }
                    await output.FlushAsync(cancellationToken).ConfigureAwait(false);
                }
            }

            // The response, input stream, and output stream are all closed before any installer validation
            // or launch can happen.  The UI can therefore move from download to post-processing honestly.
            cancellationToken.ThrowIfCancellationRequested();
            progress?.Report(new UpdateDownloadProgress(
                received, expectedLength > 0 ? expectedLength : received, UpdateDownloadPhase.Verifying));

            ValidateInstallerFile(partialPath, expectedLength);
            File.Move(partialPath, finalPath, true);
            finalPathReplaced = true;
            ValidateInstallerFile(finalPath, expectedLength);
            return finalPath;
        }
        catch
        {
            TryDelete(partialPath);
            if (finalPathReplaced) TryDelete(finalPath);
            throw;
        }
    }

    public static string FormatBytes(long bytes)
    {
        if (bytes < 1024) return $"{bytes} B";
        if (bytes < 1024 * 1024) return $"{bytes / 1024d:0.0} KB";
        return $"{bytes / 1024d / 1024d:0.0} MB";
    }

    private static HttpRequestMessage CreateInstallerRequest(Uri uri)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, uri);
        request.Headers.UserAgent.ParseAdd("CloudLight-Blizzard");
        request.Headers.Accept.ParseAdd("application/octet-stream");
        return request;
    }

    private static Uri ValidateInstallerUri(UpdateCheckResult result, string latestVersion)
    {
        if (string.IsNullOrWhiteSpace(result.InstallerDownloadUrl) ||
            !Uri.TryCreate(result.InstallerDownloadUrl.Trim(), UriKind.Absolute, out var uri) ||
            !string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(uri.Host, "github.com", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("当前版本没有可用的在线更新安装包。");

        var expectedPrefix =
            $"/{UpdateService.GitHubOwner}/{UpdateService.GitHubRepository}/releases/download/";
        if (!uri.AbsolutePath.StartsWith(expectedPrefix, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("在线更新安装包地址未通过安全校验。");

        var expectedName = $"CloudLight-Blizzard-{latestVersion}-win-x64-Setup.exe";
        var actualName = Uri.UnescapeDataString(uri.Segments[^1]);
        if (!string.Equals(actualName, expectedName, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("在线更新安装包文件名与版本不匹配。");
        return uri;
    }

    private static void ValidateInstallerFile(string path, long expectedLength)
    {
        using var stream = new FileStream(
            path, FileMode.Open, FileAccess.Read, FileShare.Read,
            BufferSize, FileOptions.SequentialScan);
        if (expectedLength > 0 && stream.Length != expectedLength)
            throw new InvalidDataException($"安装包大小校验失败：应为 {expectedLength} 字节，实际为 {stream.Length} 字节。");
        if (stream.Length < 2 || stream.ReadByte() != 'M' || stream.ReadByte() != 'Z')
            throw new InvalidDataException("下载内容不是有效的 Windows 安装程序。");
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path)) File.Delete(path);
        }
        catch { }
    }
}
