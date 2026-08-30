using System.Diagnostics;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using CloudLightBlizzard.Models;

namespace CloudLightBlizzard.Services;

public interface IUpdateService
{
    string CurrentVersion { get; }
    Task<UpdateCheckResult> CheckAsync(CancellationToken cancellationToken = default);
}

public sealed class UpdateService : IUpdateService, IDisposable
{
    public const string GitHubOwner = "Cloud-Light125";
    public const string GitHubRepository = "CloudLight-Blizzard";
    public static string LatestReleaseApiUrl =>
        new Uri(new Uri(CloudServiceConfiguration.DefaultBaseUrl), "v1/update/latest").AbsoluteUri;
    private static readonly TimeSpan SuccessCacheDuration = TimeSpan.FromMinutes(15);
    private static readonly TimeSpan MinimumRequestInterval = TimeSpan.FromSeconds(30);

    private static readonly Regex StableVersionPattern = new(
        @"^(?<major>\d+)\.(?<minor>\d+)(?:\.(?<build>\d+))?(?:\.(?<revision>\d+))?$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private readonly HttpClient _httpClient;
    private readonly CloudHttpClientFactory? _cloudHttpClients;
    private readonly Uri _endpoint;
    private readonly object _cacheGate = new();
    private Task<UpdateCheckResult>? _activeRequest;
    private UpdateCheckResult? _cachedResult;
    private DateTimeOffset _cachedAt;

    public UpdateService(HttpClient httpClient, Assembly? versionAssembly = null)
    {
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        _endpoint = new Uri(LatestReleaseApiUrl);

        CurrentVersion = ReadCurrentVersion(versionAssembly ?? Assembly.GetExecutingAssembly());
    }

    public UpdateService(AppSettings settings, CloudHttpClientFactory httpClients, Assembly? versionAssembly = null)
    {
        ArgumentNullException.ThrowIfNull(settings);
        _cloudHttpClients = httpClients;
        _httpClient = null!;
        _endpoint = EndpointFor(settings);
        CurrentVersion = ReadCurrentVersion(versionAssembly ?? Assembly.GetExecutingAssembly());
    }

    internal UpdateService(HttpClient httpClient, string currentVersion)
        : this(httpClient)
    {
        CurrentVersion = NormalizeVersion(currentVersion)
            ?? throw new ArgumentException("测试版本格式无效。", nameof(currentVersion));
    }

    public string CurrentVersion { get; }

    public Task<UpdateCheckResult> CheckAsync(CancellationToken cancellationToken = default)
    {
        lock (_cacheGate)
        {
            if (_activeRequest is { IsCompleted: false }) return _activeRequest;
            var age = DateTimeOffset.UtcNow - _cachedAt;
            if (_cachedResult is not null &&
                (_cachedResult.Status == UpdateCheckResultStatus.Success
                    ? age < SuccessCacheDuration : age < MinimumRequestInterval))
                return Task.FromResult(_cachedResult);
            _activeRequest = RunAndCacheAsync(cancellationToken);
            return _activeRequest;
        }
    }

    private async Task<UpdateCheckResult> RunAndCacheAsync(CancellationToken cancellationToken)
    {
        try
        {
            var result = await CheckCoreAsync(cancellationToken).ConfigureAwait(false);
            lock (_cacheGate)
            {
                _cachedResult = result;
                _cachedAt = DateTimeOffset.UtcNow;
            }
            return result;
        }
        finally
        {
            lock (_cacheGate) _activeRequest = null;
        }
    }

    private async Task<UpdateCheckResult> CheckCoreAsync(CancellationToken cancellationToken)
    {
        try
        {
            HttpResponseMessage response;
            if (_cloudHttpClients is not null)
            {
                response = await _cloudHttpClients.SendGetAsync(
                        () => CreateLatestReleaseRequest(_endpoint), "update", cancellationToken)
                    .ConfigureAwait(false);
            }
            else
            {
                using var request = CreateLatestReleaseRequest();
                response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead,
                    cancellationToken).ConfigureAwait(false);
            }
            using (response)
            {
            if (!response.IsSuccessStatusCode)
                return await FailedResponseAsync(response, cancellationToken).ConfigureAwait(false);

            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
            WorkerRelease? release;
            try
            {
                release = await JsonSerializer.DeserializeAsync<WorkerRelease>(stream, JsonOptions, cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (JsonException ex)
            {
                return Failed(UpdateFailureKind.InvalidResponse, "更新服务返回了无效数据，请稍后再试。",
                    $"Invalid JSON: {ex.Message}");
            }
            if (release is null || !TryParseStableVersion(release.Version ?? release.Tag,
                    out var latest, out var latestText))
                return Failed(UpdateFailureKind.InvalidResponse, "更新服务返回了无效数据，请稍后再试。",
                    "Missing or invalid stable version");

            if (!TryParseStableVersion(CurrentVersion, out var current, out _))
                throw new InvalidOperationException("无法读取当前程序集版本。");

            var installerName = $"CloudLight-Blizzard-{latestText}-win-x64-Setup.exe";
            var installerAsset = release.Assets?
                .FirstOrDefault(asset => string.Equals(asset.Name, installerName, StringComparison.OrdinalIgnoreCase));
            var installerUrl = installerAsset?.DownloadUrl;

            return new UpdateCheckResult
            {
                Status = UpdateCheckResultStatus.Success,
                CurrentVersion = CurrentVersion,
                LatestVersion = latestText,
                HasUpdate = latest > current,
                ReleaseName = string.IsNullOrWhiteSpace(release.Name) ? $"CloudLight Blizzard {latestText}" : release.Name,
                ReleaseNotes = release.Notes?.Trim() ?? "",
                ReleaseUrl = ValidateRepositoryUrl(release.HtmlUrl, "/releases/tag/"),
                PublishedAt = release.PublishedAt,
                InstallerDownloadUrl = ValidateRepositoryUrl(installerUrl, "/releases/download/") is { Length: > 0 } url
                    ? url : null,
                InstallerSize = installerAsset is { Size: > 0 } ? installerAsset.Size : 0,
            };
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (OperationCanceledException)
        {
            return Failed(UpdateFailureKind.Timeout, "检查更新超时，请稍后再试。", "Update request timed out");
        }
        catch (CloudNetworkException ex)
        {
            var kind = ex.Kind is CloudNetworkFailureKind.InvalidProxy or
                CloudNetworkFailureKind.ProxyConnectionFailed or CloudNetworkFailureKind.ProxyAndDirectConnectionFailed
                ? UpdateFailureKind.ProxyUnavailable : UpdateFailureKind.NetworkUnavailable;
            return Failed(kind, CloudHttpClientFactory.UserMessage(ex.Kind, "update"), ex.Kind.ToString());
        }
        catch (Exception ex)
        {
            return Failed(UpdateFailureKind.NetworkUnavailable, "暂时无法连接更新服务器。",
                $"{ex.GetType().Name}: {ex.Message}");
        }
    }

    public static bool IsNewerVersion(string currentVersion, string latestTag)
    {
        if (!TryParseStableVersion(currentVersion, out var current, out _) ||
            !TryParseStableVersion(latestTag, out var latest, out _))
            return false;
        return latest > current;
    }

    public static string? NormalizeVersion(string? value) =>
        TryParseStableVersion(value, out _, out var normalized) ? normalized : null;

    public void Dispose()
    {
        // Injected/shared clients are owned by their caller or CloudHttpClientFactory.
    }

    private UpdateCheckResult NoRelease() => new()
    {
        Status = UpdateCheckResultStatus.NoRelease,
        CurrentVersion = CurrentVersion,
    };

    private UpdateCheckResult Failed(UpdateFailureKind kind, string message, string technicalDetail,
        DateTimeOffset? retryAt = null) => new()
    {
        Status = UpdateCheckResultStatus.Failed,
        CurrentVersion = CurrentVersion,
        ErrorMessage = message,
        FailureKind = kind,
        RetryAt = retryAt,
        TechnicalDetail = technicalDetail,
    };

    private async Task<UpdateCheckResult> FailedResponseAsync(HttpResponseMessage response,
        CancellationToken cancellationToken)
    {
        string body;
        try
        {
            body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            if (body.Length > 1000) body = body[..1000];
        }
        catch { body = ""; }

        WorkerError? error = null;
        try { error = JsonSerializer.Deserialize<WorkerError>(body, JsonOptions); }
        catch (JsonException) { }
        var resetAt = error?.ResetAt ?? ReadRateLimitReset(response);
        var workerError = response.Headers.TryGetValues("X-Update-Error", out var values)
            ? values.FirstOrDefault() : error?.Error;
        var remaining = Header(response, "X-RateLimit-Remaining");
        var rateLimited = response.StatusCode == HttpStatusCode.TooManyRequests ||
            string.Equals(workerError, "rate_limited", StringComparison.OrdinalIgnoreCase) ||
            response.StatusCode == HttpStatusCode.Forbidden &&
            (remaining == "0" || body.Contains("rate limit", StringComparison.OrdinalIgnoreCase));
        var technical = $"HTTP {(int)response.StatusCode}; limit={Header(response, "X-RateLimit-Limit") ?? "n/a"}; " +
            $"remaining={remaining ?? "n/a"}; reset={Header(response, "X-RateLimit-Reset") ?? resetAt?.ToString("O") ?? "n/a"}; " +
            $"retryAfter={Header(response, "Retry-After") ?? "n/a"}; error={workerError ?? "n/a"}";

        if (rateLimited)
        {
            var message = "GitHub 更新服务请求过于频繁，暂时无法检查更新，请稍后再试。";
            if (resetAt is { } retry && retry > DateTimeOffset.UtcNow)
                message += $" 预计可在 {retry.ToLocalTime():HH:mm} 后再次检查。";
            return Failed(UpdateFailureKind.RateLimited, message, technical, resetAt);
        }
        if ((int)response.StatusCode >= 500)
            return Failed(UpdateFailureKind.Http5xx, "更新服务暂时不可用，请稍后再试。", technical);
        return Failed(UpdateFailureKind.InvalidResponse, "更新服务返回了无效响应，请稍后再试。", technical);
    }

    private static string? Header(HttpResponseMessage response, string name) =>
        response.Headers.TryGetValues(name, out var values) ? values.FirstOrDefault() : null;

    private static DateTimeOffset? ReadRateLimitReset(HttpResponseMessage response)
    {
        var value = Header(response, "X-RateLimit-Reset");
        return long.TryParse(value, out var seconds)
            ? DateTimeOffset.FromUnixTimeSeconds(seconds) : null;
    }

    internal static Uri EndpointFor(AppSettings settings) =>
        new(new Uri(CloudServiceConfiguration.NormalizeBaseUrl(settings.CloudServiceBaseUrl)), "v1/update/latest");

    internal HttpRequestMessage CreateLatestReleaseRequest() => CreateLatestReleaseRequest(_endpoint);

    internal static HttpRequestMessage CreateLatestReleaseRequest(Uri? endpoint = null)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, endpoint ?? new Uri(LatestReleaseApiUrl));
        request.Headers.UserAgent.ParseAdd("CloudLight-Blizzard");
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        return request;
    }

    private static string ReadCurrentVersion(Assembly assembly)
    {
        var informational = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?
            .InformationalVersion;
        var fileVersion = string.IsNullOrWhiteSpace(assembly.Location)
            ? null
            : FileVersionInfo.GetVersionInfo(assembly.Location).FileVersion;
        var assemblyVersion = assembly.GetName().Version?.ToString();
        return NormalizeVersion(informational) ?? NormalizeVersion(fileVersion) ??
               NormalizeVersion(assemblyVersion) ?? "0.0.0";
    }

    private static bool TryParseStableVersion(string? value, out Version version, out string normalized)
    {
        version = new Version(0, 0, 0, 0);
        normalized = "";
        if (string.IsNullOrWhiteSpace(value)) return false;
        var raw = value.Trim();
        if (raw.StartsWith('v') || raw.StartsWith('V')) raw = raw[1..];
        var metadataIndex = raw.IndexOf('+');
        if (metadataIndex >= 0) raw = raw[..metadataIndex];
        var match = StableVersionPattern.Match(raw);
        if (!match.Success) return false;

        if (!int.TryParse(match.Groups["major"].Value, out var major) ||
            !int.TryParse(match.Groups["minor"].Value, out var minor) ||
            !int.TryParse(match.Groups["build"].Success ? match.Groups["build"].Value : "0", out var build) ||
            !int.TryParse(match.Groups["revision"].Success ? match.Groups["revision"].Value : "0", out var revision))
            return false;

        version = new Version(major, minor, build, revision);
        normalized = revision > 0
            ? $"{major}.{minor}.{build}.{revision}"
            : $"{major}.{minor}.{build}";
        return true;
    }

    private static string ValidateRepositoryUrl(string? value, string releasePath)
    {
        if (!Uri.TryCreate(value?.Trim(), UriKind.Absolute, out var uri) ||
            !string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(uri.Host, "github.com", StringComparison.OrdinalIgnoreCase))
            return "";
        var expectedPrefix = $"/{GitHubOwner}/{GitHubRepository}{releasePath}";
        return uri.AbsolutePath.StartsWith(expectedPrefix, StringComparison.OrdinalIgnoreCase)
            ? uri.AbsoluteUri : "";
    }

    private sealed class WorkerRelease
    {
        [JsonPropertyName("version")] public string? Version { get; init; }
        [JsonPropertyName("tag")] public string? Tag { get; init; }
        [JsonPropertyName("name")] public string? Name { get; init; }
        [JsonPropertyName("notes")] public string? Notes { get; init; }
        [JsonPropertyName("htmlUrl")] public string? HtmlUrl { get; init; }
        [JsonPropertyName("publishedAt")] public DateTimeOffset? PublishedAt { get; init; }
        [JsonPropertyName("assets")] public List<WorkerAsset>? Assets { get; init; }
    }

    private sealed class WorkerAsset
    {
        [JsonPropertyName("name")] public string? Name { get; init; }
        [JsonPropertyName("downloadUrl")] public string? DownloadUrl { get; init; }
        [JsonPropertyName("size")] public long Size { get; init; }
    }

    private sealed class WorkerError
    {
        [JsonPropertyName("error")] public string? Error { get; init; }
        [JsonPropertyName("resetAt")] public DateTimeOffset? ResetAt { get; init; }
    }

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };
}
