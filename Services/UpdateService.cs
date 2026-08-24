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
    public const string GitHubOwner = "yundan125";
    public const string GitHubRepository = "CloudLight-Blizzard";
    public static string LatestReleaseApiUrl =>
        $"https://api.github.com/repos/{GitHubOwner}/{GitHubRepository}/releases/latest";

    private static readonly Regex StableVersionPattern = new(
        @"^(?<major>\d+)\.(?<minor>\d+)(?:\.(?<build>\d+))?(?:\.(?<revision>\d+))?$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private readonly HttpClient _httpClient;
    private readonly CloudHttpClientFactory? _cloudHttpClients;

    public UpdateService(HttpClient httpClient, Assembly? versionAssembly = null)
    {
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));

        CurrentVersion = ReadCurrentVersion(versionAssembly ?? Assembly.GetExecutingAssembly());
    }

    public UpdateService(AppSettings settings, CloudHttpClientFactory httpClients, Assembly? versionAssembly = null)
    {
        ArgumentNullException.ThrowIfNull(settings);
        _cloudHttpClients = httpClients;
        _httpClient = null!;
        CurrentVersion = ReadCurrentVersion(versionAssembly ?? Assembly.GetExecutingAssembly());
    }

    internal UpdateService(HttpClient httpClient, string currentVersion)
        : this(httpClient)
    {
        CurrentVersion = NormalizeVersion(currentVersion)
            ?? throw new ArgumentException("测试版本格式无效。", nameof(currentVersion));
    }

    public string CurrentVersion { get; }

    public async Task<UpdateCheckResult> CheckAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            HttpResponseMessage response;
            if (_cloudHttpClients is not null)
            {
                response = await _cloudHttpClients.SendGetAsync(CreateLatestReleaseRequest, "update", cancellationToken)
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
            if (response.StatusCode == HttpStatusCode.NotFound)
                return NoRelease();

            response.EnsureSuccessStatusCode();
            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
            var release = await JsonSerializer.DeserializeAsync<GitHubRelease>(stream, JsonOptions, cancellationToken)
                .ConfigureAwait(false);
            if (release is null || release.Draft || release.Prerelease ||
                !TryParseStableVersion(release.TagName, out var latest, out var latestText))
                return NoRelease();

            if (!TryParseStableVersion(CurrentVersion, out var current, out _))
                throw new InvalidOperationException("无法读取当前程序集版本。");

            var installerName = $"CloudLight-Blizzard-{latestText}-win-x64-Setup.exe";
            var installerUrl = release.Assets?
                .FirstOrDefault(asset => string.Equals(asset.Name, installerName, StringComparison.OrdinalIgnoreCase))?
                .BrowserDownloadUrl;

            return new UpdateCheckResult
            {
                Status = UpdateCheckResultStatus.Success,
                CurrentVersion = CurrentVersion,
                LatestVersion = latestText,
                HasUpdate = latest > current,
                ReleaseName = string.IsNullOrWhiteSpace(release.Name) ? $"CloudLight Blizzard {latestText}" : release.Name,
                ReleaseNotes = release.Body?.Trim() ?? "",
                ReleaseUrl = ValidateRepositoryUrl(release.HtmlUrl, "/releases/tag/"),
                PublishedAt = release.PublishedAt,
                InstallerDownloadUrl = ValidateRepositoryUrl(installerUrl, "/releases/download/") is { Length: > 0 } url
                    ? url : null,
            };
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (OperationCanceledException)
        {
            return Failed("请求 GitHub Release 超时。");
        }
        catch (CloudNetworkException ex)
        {
            return Failed(CloudHttpClientFactory.UserMessage(ex.Kind, "update"));
        }
        catch (Exception ex)
        {
            return Failed(ex.Message);
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

    private UpdateCheckResult Failed(string message) => new()
    {
        Status = UpdateCheckResultStatus.Failed,
        CurrentVersion = CurrentVersion,
        ErrorMessage = message,
    };

    internal static HttpRequestMessage CreateLatestReleaseRequest()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, LatestReleaseApiUrl);
        request.Headers.UserAgent.ParseAdd("CloudLight-Blizzard");
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
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

    private sealed class GitHubRelease
    {
        [JsonPropertyName("tag_name")] public string? TagName { get; init; }
        [JsonPropertyName("name")] public string? Name { get; init; }
        [JsonPropertyName("body")] public string? Body { get; init; }
        [JsonPropertyName("html_url")] public string? HtmlUrl { get; init; }
        [JsonPropertyName("published_at")] public DateTimeOffset? PublishedAt { get; init; }
        [JsonPropertyName("draft")] public bool Draft { get; init; }
        [JsonPropertyName("prerelease")] public bool Prerelease { get; init; }
        [JsonPropertyName("assets")] public List<GitHubAsset>? Assets { get; init; }
    }

    private sealed class GitHubAsset
    {
        [JsonPropertyName("name")] public string? Name { get; init; }
        [JsonPropertyName("browser_download_url")] public string? BrowserDownloadUrl { get; init; }
    }

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };
}
