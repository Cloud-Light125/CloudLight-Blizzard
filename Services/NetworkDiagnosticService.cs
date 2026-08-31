using System.Net;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Diagnostics;
using CloudLightBlizzard.Services.Drops;

namespace CloudLightBlizzard.Services;

public sealed record NetworkDiagnosticReport(
    DateTimeOffset CompletedAt,
    CloudNetworkProbeResult Proxy,
    CloudNetworkProbeResult Announcement,
    CloudNetworkProbeResult Update,
    CloudNetworkProbeResult? Soop = null,
    CloudNetworkProbeResult? Twitch = null,
    CloudNetworkProbeResult? Bilibili = null)
{
    public string ToDisplayText() => string.Join(Environment.NewLine,
        Format("代理", Proxy, proxyLine: true),
        Format("公告服务", Announcement),
        Format("更新服务", Update),
        Soop is null ? "SOOP：未执行" : Format("SOOP", Soop),
        Twitch is null ? "Twitch：未执行" : Format("Twitch", Twitch),
        Bilibili is null ? "哔哩哔哩：未执行" : Format("哔哩哔哩", Bilibili));

    public IEnumerable<string> ToCopyLines()
    {
        yield return CopyLine("代理检测", Proxy);
        yield return CopyLine("公告服务", Announcement);
        yield return CopyLine("更新服务", Update);
        if (Soop is not null) yield return CopyLine("SOOP", Soop);
        if (Twitch is not null) yield return CopyLine("Twitch", Twitch);
        if (Bilibili is not null) yield return CopyLine("哔哩哔哩（固定直连）", Bilibili);
    }

    private static string Format(string name, CloudNetworkProbeResult result, bool proxyLine = false)
    {
        if (proxyLine && result.Message.StartsWith("未启用", StringComparison.Ordinal))
            return $"{name}：{result.Message}";
        var elapsed = result.ElapsedMilliseconds > 0 ? $" · {result.ElapsedMilliseconds} ms" : "";
        var route = proxyLine ? "" : $" · {RouteName(result.Route)}";
        if (!result.Success && name == "更新服务" &&
            result.Message == "GitHub API 请求频率限制")
            return $"{name}：受限{elapsed}{route} · {result.Message}";
        if (!result.Success && string.Equals(result.Message, "连接超时", StringComparison.Ordinal))
            return $"{name}：直连超时{elapsed}{route}";
        return result.Success
            ? $"{name}：{(proxyLine ? "可用" : "正常")}{elapsed}{route}"
            : $"{name}：失败{elapsed}{route} · {result.Message}";
    }

    private static string CopyLine(string name, CloudNetworkProbeResult result) =>
        $"{name}：{(result.Success ? "正常" : "失败")} / {result.Route} / " +
        $"{result.ElapsedMilliseconds}ms" + (result.Success ? "" : $" / {result.Message}");

    private static string RouteName(string route) => route switch
    {
        "Proxy" => "代理",
        "Direct" => "直连",
        "DirectFallback" => "直连回退",
        "Proxy→DirectFallback" => "代理与直连回退",
        _ => route,
    };
}

public sealed record RuntimeDiagnosticContext(
    string AppVersion,
    bool BattleNetPathValid,
    bool OverwatchPathValid,
    string CurrentRegion,
    string RegionBackupMode,
    string RegionBackupState,
    DropsRuntimeDiagnosticSnapshot Drops);

public sealed class NetworkDiagnosticService
{
    private static readonly TimeSpan ProbeTimeout = TimeSpan.FromSeconds(15);
    private readonly AppSettings _settings;
    private readonly CloudHttpClientFactory _httpClients;
    private readonly Func<HttpClient> _directClientFactory;

    public NetworkDiagnosticService(AppSettings settings, CloudHttpClientFactory httpClients,
        Func<HttpClient>? directClientFactory = null)
    {
        _settings = settings;
        _httpClients = httpClients;
        _directClientFactory = directClientFactory ?? CreateDirectClient;
    }

    public async Task<NetworkDiagnosticReport> RunAsync(CancellationToken cancellationToken = default)
    {
        var announcementEndpoint = AnnouncementService.EndpointFor(_settings);
        var updateEndpoint = UpdateService.EndpointFor(_settings);
        var proxy = await ProbeWithTimeoutAsync(
            token => _httpClients.ProbeProxyAsync(
                () => new HttpRequestMessage(HttpMethod.Get, announcementEndpoint),
                "diagnostic-proxy", IsSuccess, token), "Proxy", cancellationToken).ConfigureAwait(false);

        var announcementTask = ProbeWithTimeoutAsync(
            token => _httpClients.ProbeGetAsync(
                () => new HttpRequestMessage(HttpMethod.Get, announcementEndpoint),
                "diagnostic-announcement", IsSuccess, token), DefaultRoute(), cancellationToken);
        var updateTask = ProbeWithTimeoutAsync(
            token => _httpClients.ProbeGetAsync(() => UpdateService.CreateLatestReleaseRequest(updateEndpoint),
                "diagnostic-update", status => IsSuccess(status) || status == HttpStatusCode.NotFound, token),
            DefaultRoute(), cancellationToken);
        var soopTask = ProbeWithTimeoutAsync(
            token => _httpClients.ProbeGetAsync(
                () => new HttpRequestMessage(HttpMethod.Get, "https://www.sooplive.com/"),
                "diagnostic-soop", status => IsSuccess(status) || (int)status is >= 300 and < 400, token),
            DefaultRoute(), cancellationToken);
        var twitchTask = ProbeWithTimeoutAsync(
            token => _httpClients.ProbeGetAsync(
                () => new HttpRequestMessage(HttpMethod.Get, "https://www.twitch.tv/"),
                "diagnostic-twitch", status => IsSuccess(status) || (int)status is >= 300 and < 400, token),
            DefaultRoute(), cancellationToken);
        var bilibiliTask = ProbeWithTimeoutAsync(
            ProbeBilibiliDirectAsync, "Direct", cancellationToken);
        await Task.WhenAll(announcementTask, updateTask, soopTask, twitchTask, bilibiliTask).ConfigureAwait(false);
        return new NetworkDiagnosticReport(DateTimeOffset.Now, proxy,
            await announcementTask.ConfigureAwait(false), await updateTask.ConfigureAwait(false),
            await soopTask.ConfigureAwait(false), await twitchTask.ConfigureAwait(false),
            await bilibiliTask.ConfigureAwait(false));
    }

    public string BuildCopyText(RuntimeDiagnosticContext context, NetworkDiagnosticReport? report)
    {
        var lines = new List<string>
        {
            $"CloudLight Blizzard {context.AppVersion}",
            $"Windows：{RuntimeInformation.OSDescription} {RuntimeInformation.OSArchitecture}",
            "",
            $"网络代理：{(_settings.EnableProxy ? "已启用" : "未启用")}",
            $"代理：{SafeProxyDisplay(_settings)}",
            $"直连回退：{(_settings.FallbackDirect ? "开启" : "关闭")}",
        };
        if (report is not null)
            lines.AddRange(report.ToCopyLines());
        else
            lines.Add("网络诊断：尚未执行");
        lines.AddRange([
            "",
            $"Battle.net 路径：{(context.BattleNetPathValid ? "有效" : "无效或尚未识别")}",
            $"Overwatch 路径：{(context.OverwatchPathValid ? "有效" : "无效或尚未设置")}",
            $"当前区服：{context.CurrentRegion}",
            $"Region BackupMode：{context.RegionBackupMode}",
            $"Region BackupState：{context.RegionBackupState}",
            "",
            $"SOOP：{context.Drops.SoopStatus}",
            $"SOOP 最后成功：{context.Drops.SoopLastSuccess}",
            $"Twitch：{context.Drops.TwitchStatus}",
            $"Twitch 最后成功：{context.Drops.TwitchLastSuccess}",
            $"YouTube：{context.Drops.YouTubeStatus}",
            $"YouTube 最后成功：{context.Drops.YouTubeLastSuccess}",
            $"哔哩哔哩：{context.Drops.BilibiliStatus}",
            $"哔哩哔哩最后成功：{context.Drops.BilibiliLastSuccess}",
            "哔哩哔哩网络模式：DIRECT（Worker 清理代理环境变量，HTTP 客户端 trust_env=false）",
        ]);
        if (!string.IsNullOrWhiteSpace(context.Drops.RecentNetworkError))
            lines.Add($"最近网络错误：{context.Drops.RecentNetworkError}");
        return SensitiveDataRedactor.Redact(string.Join(Environment.NewLine, lines));
    }

    internal static string SafeProxyDisplay(AppSettings settings)
    {
        if (!settings.EnableProxy) return "未启用";
        if (!ProxyValidator.TryNormalize(settings.ProxyUrl, out var normalized, out _)) return "地址无效";
        var uri = new Uri(normalized);
        return $"{uri.IdnHost}:{uri.Port}";
    }

    private string DefaultRoute() => _settings.EnableProxy ? "Proxy" : "Direct";
    private static bool IsSuccess(HttpStatusCode status) => (int)status is >= 200 and < 300;

    private async Task<CloudNetworkProbeResult> ProbeBilibiliDirectAsync(CancellationToken cancellationToken)
    {
        var stopwatch = Stopwatch.StartNew();
        try
        {
            using var client = _directClientFactory();
            using var response = await client.GetAsync("https://live.bilibili.com/",
                HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
            var status = response.StatusCode;
            var success = (int)status is >= 200 and < 400;
            return new CloudNetworkProbeResult(success, "Direct", stopwatch.ElapsedMilliseconds,
                (int)status, null, success ? "正常" : $"HTTP {(int)status}");
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return new CloudNetworkProbeResult(false, "Direct", stopwatch.ElapsedMilliseconds,
                null, CloudNetworkFailureKind.DirectConnectionFailed, "连接超时");
        }
        catch (HttpRequestException)
        {
            return new CloudNetworkProbeResult(false, "Direct", stopwatch.ElapsedMilliseconds,
                null, CloudNetworkFailureKind.DirectConnectionFailed, "直连失败");
        }
        catch
        {
            return new CloudNetworkProbeResult(false, "Direct", stopwatch.ElapsedMilliseconds,
                null, CloudNetworkFailureKind.DirectConnectionFailed, "诊断请求失败");
        }
    }

    private static HttpClient CreateDirectClient() => new(new HttpClientHandler
    {
        UseProxy = false,
        Proxy = null,
        AllowAutoRedirect = true,
    }) { Timeout = ProbeTimeout };

    private static async Task<CloudNetworkProbeResult> ProbeWithTimeoutAsync(
        Func<CancellationToken, Task<CloudNetworkProbeResult>> probe, string route,
        CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(ProbeTimeout);
        try
        {
            return await probe(timeout.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return new CloudNetworkProbeResult(false, route, (long)ProbeTimeout.TotalMilliseconds,
                null, null, "连接超时");
        }
        catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
        {
            Debug.WriteLine(ex);
            return new CloudNetworkProbeResult(false, route, 0, null, null, "诊断请求失败");
        }
    }
}
