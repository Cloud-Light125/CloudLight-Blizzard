using System.Net;
using System.Net.Http;
using System.IO;
using System.Text.Json;
using CloudLightBlizzard.Services.Drops;

namespace CloudLightBlizzard.Services;

public enum CloudNetworkFailureKind
{
    DirectConnectionFailed,
    ProxyConnectionFailed,
    ProxyAndDirectConnectionFailed,
    InvalidProxy,
}

public sealed class CloudNetworkException : HttpRequestException
{
    public CloudNetworkException(CloudNetworkFailureKind kind, Exception? inner = null)
        : base(kind.ToString(), inner) => Kind = kind;

    public CloudNetworkFailureKind Kind { get; }
}

/// <summary>
/// Reuses direct/proxy clients while selecting the current application proxy settings for every request.
/// Idempotent GET requests may retry once without the proxy; request bodies are never replayed here.
/// </summary>
public sealed class CloudHttpClientFactory : IDisposable
{
    private readonly AppSettings _settings;
    private readonly string _logFile;
    private readonly object _gate = new();
    private readonly HttpClient _directClient;
    private readonly List<HttpClient> _retiredProxyClients = new();
    private HttpClient? _proxyClient;
    private string? _proxyKey;
    private bool _disposed;

    public CloudHttpClientFactory(AppSettings settings, string? logFile = null)
    {
        _settings = settings;
        _logFile = logFile ?? Path.Combine(AppPaths.Current.LogsDir, "network.log");
        _directClient = CreateClient(null);
    }

    public async Task<HttpResponseMessage> SendGetAsync(Func<HttpRequestMessage> requestFactory,
        string service, CancellationToken cancellationToken)
    {
        var route = ReadRoute();
        if (!route.ProxyEnabled)
        {
            try
            {
                return await SendAttemptAsync(_directClient, requestFactory(), service, "Direct", null,
                    cancellationToken).ConfigureAwait(false);
            }
            catch (HttpRequestException ex)
            {
                throw new CloudNetworkException(CloudNetworkFailureKind.DirectConnectionFailed, ex);
            }
        }
        if (route.ProxyUrl is null)
        {
            Log(service, "Proxy", null, null, nameof(CloudNetworkFailureKind.InvalidProxy));
            throw new CloudNetworkException(CloudNetworkFailureKind.InvalidProxy);
        }

        try
        {
            return await SendAttemptAsync(GetProxyClient(route.ProxyUrl), requestFactory(), service, "Proxy",
                route.ProxyUrl, cancellationToken).ConfigureAwait(false);
        }
        catch (HttpRequestException proxyError) when (route.FallbackDirect && !cancellationToken.IsCancellationRequested)
        {
            try
            {
                return await SendAttemptAsync(_directClient, requestFactory(), service, "DirectFallback", null,
                    cancellationToken).ConfigureAwait(false);
            }
            catch (HttpRequestException directError)
            {
                throw new CloudNetworkException(CloudNetworkFailureKind.ProxyAndDirectConnectionFailed,
                    new AggregateException(proxyError, directError));
            }
        }
        catch (HttpRequestException ex)
        {
            throw new CloudNetworkException(CloudNetworkFailureKind.ProxyConnectionFailed, ex);
        }
    }

    public async Task<HttpResponseMessage> SendWithoutReplayAsync(HttpRequestMessage request,
        string service, CancellationToken cancellationToken)
    {
        var route = ReadRoute();
        if (!route.ProxyEnabled)
        {
            try
            {
                return await SendAttemptAsync(_directClient, request, service, "Direct", null, cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (HttpRequestException ex)
            {
                throw new CloudNetworkException(CloudNetworkFailureKind.DirectConnectionFailed, ex);
            }
        }
        if (route.ProxyUrl is null)
        {
            request.Dispose();
            Log(service, "Proxy", null, null, nameof(CloudNetworkFailureKind.InvalidProxy));
            throw new CloudNetworkException(CloudNetworkFailureKind.InvalidProxy);
        }

        try
        {
            return await SendAttemptAsync(GetProxyClient(route.ProxyUrl), request, service, "Proxy", route.ProxyUrl,
                cancellationToken).ConfigureAwait(false);
        }
        catch (HttpRequestException ex)
        {
            // A multipart POST may have already started streaming. Never replay it automatically.
            throw new CloudNetworkException(CloudNetworkFailureKind.ProxyConnectionFailed, ex);
        }
    }

    public static string UserMessage(CloudNetworkFailureKind kind, string service) => kind switch
    {
        CloudNetworkFailureKind.InvalidProxy => "当前代理地址无效。",
        CloudNetworkFailureKind.ProxyAndDirectConnectionFailed => "代理和直连均无法连接服务器。",
        CloudNetworkFailureKind.ProxyConnectionFailed when service == "announcement" => "公告服务代理连接失败。",
        CloudNetworkFailureKind.ProxyConnectionFailed when service == "feedback" => "无法通过当前代理连接反馈服务器。",
        CloudNetworkFailureKind.ProxyConnectionFailed when service == "update" => "无法通过当前代理连接更新服务器。",
        CloudNetworkFailureKind.DirectConnectionFailed when service == "announcement" => "暂时无法连接公告服务器。",
        CloudNetworkFailureKind.DirectConnectionFailed when service == "feedback" => "暂时无法连接反馈服务器。",
        _ => "暂时无法连接更新服务器。",
    };

    private async Task<HttpResponseMessage> SendAttemptAsync(HttpClient client, HttpRequestMessage request,
        string service, string route, Uri? proxyUrl, CancellationToken cancellationToken)
    {
        using (request)
        {
            try
            {
                var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead,
                    cancellationToken).ConfigureAwait(false);
                Log(service, route, proxyUrl, (int)response.StatusCode, null);
                if (proxyUrl is not null && response.StatusCode == HttpStatusCode.ProxyAuthenticationRequired)
                {
                    response.Dispose();
                    throw new HttpRequestException("Proxy authentication failed.");
                }
                return response;
            }
            catch (Exception ex)
            {
                Log(service, route, proxyUrl, null, ex.GetType().Name);
                throw;
            }
        }
    }

    private RouteSnapshot ReadRoute()
    {
        if (!_settings.EnableProxy) return new(false, null, false);
        return ProxyValidator.TryNormalize(_settings.ProxyUrl, out var proxyUrl, out _)
            ? new(true, new Uri(proxyUrl), _settings.FallbackDirect)
            : new(true, null, _settings.FallbackDirect);
    }

    private HttpClient GetProxyClient(Uri proxyUrl)
    {
        var key = proxyUrl.AbsoluteUri;
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (_proxyClient is not null && string.Equals(_proxyKey, key, StringComparison.OrdinalIgnoreCase))
                return _proxyClient;
            if (_proxyClient is not null) _retiredProxyClients.Add(_proxyClient);
            _proxyClient = CreateClient(proxyUrl);
            _proxyKey = key;
            return _proxyClient;
        }
    }

    private static HttpClient CreateClient(Uri? proxyUrl)
    {
        var handler = new SocketsHttpHandler
        {
            ConnectTimeout = TimeSpan.FromSeconds(15),
            AutomaticDecompression = DecompressionMethods.All,
            PooledConnectionLifetime = TimeSpan.FromMinutes(5),
            UseProxy = proxyUrl is not null,
            Proxy = proxyUrl is null ? null : new WebProxy(proxyUrl),
        };
        var client = new HttpClient(handler) { Timeout = Timeout.InfiniteTimeSpan };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("CloudLight-Blizzard/2.0");
        return client;
    }

    private void Log(string service, string route, Uri? proxyUrl, int? status, string? exceptionType)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_logFile)!);
            var proxy = proxyUrl is null ? null : $"{proxyUrl.IdnHost}:{proxyUrl.Port}";
            var record = JsonSerializer.Serialize(new
            {
                timestamp = DateTimeOffset.Now,
                service,
                route,
                proxy,
                status,
                exceptionType,
            });
            lock (_gate) File.AppendAllText(_logFile, record + Environment.NewLine);
        }
        catch { }
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed) return;
            _disposed = true;
            _directClient.Dispose();
            _proxyClient?.Dispose();
            foreach (var client in _retiredProxyClients) client.Dispose();
            _retiredProxyClients.Clear();
        }
    }

    private sealed record RouteSnapshot(bool ProxyEnabled, Uri? ProxyUrl, bool FallbackDirect);
}
