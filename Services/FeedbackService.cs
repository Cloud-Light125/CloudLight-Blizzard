using System.Diagnostics;
using System.Net;
using System.Net.Http;
using System.IO;
using System.Text.Json;
using CloudLightBlizzard.Models;

namespace CloudLightBlizzard.Services;

public sealed class FeedbackService : IDisposable
{
    private readonly AppSettings _settings;
    private readonly Func<HttpClient>? _clientFactory;
    private readonly CloudHttpClientFactory? _httpClients;
    private readonly bool _ownsHttpClients;

    public FeedbackService(AppSettings settings, Func<HttpClient>? clientFactory = null,
        CloudHttpClientFactory? httpClients = null)
    {
        _settings = settings;
        _clientFactory = clientFactory;
        _ownsHttpClients = clientFactory is null && httpClients is null;
        _httpClients = clientFactory is null ? httpClients ?? new CloudHttpClientFactory(settings) : httpClients;
    }

    public async Task<FeedbackSubmitResult> SubmitAsync(FeedbackSubmitRequest request,
        IProgress<FeedbackUploadProgress>? progress, CancellationToken cancellationToken)
    {
        if (!string.IsNullOrWhiteSpace(request.LogsZipPath) &&
            new FileInfo(request.LogsZipPath).Length > CloudServiceConfiguration.MaximumZipBytes)
            return new(false, null, FeedbackFailureKind.PayloadTooLarge);

        var fields = new Dictionary<string, string>
        {
            ["title"] = request.Title,
            ["description"] = request.Description,
            ["appVersion"] = request.AppVersion,
            ["osVersion"] = request.OsVersion,
            ["contact"] = request.Contact,
            ["clientSubmissionId"] = request.ClientSubmissionId,
        };
        using var content = new MultipartProgressContent(fields, request.LogsZipPath, progress);
        using var injectedClient = _clientFactory?.Invoke();
        using var requestMessage = new HttpRequestMessage(HttpMethod.Post,
            new Uri(new Uri(CloudServiceConfiguration.NormalizeBaseUrl(_settings.CloudServiceBaseUrl)), "v1/feedback"))
        { Content = content };
        using var overallTimeout = new CancellationTokenSource(TimeSpan.FromMinutes(10));
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, overallTimeout.Token);
        var timer = Stopwatch.StartNew();
        try
        {
            using var response = injectedClient is not null
                ? await injectedClient.SendAsync(requestMessage, HttpCompletionOption.ResponseHeadersRead, linked.Token)
                    .ConfigureAwait(false)
                : await _httpClients!.SendWithoutReplayAsync(requestMessage, "feedback", linked.Token)
                    .ConfigureAwait(false);
            await using var stream = await response.Content.ReadAsStreamAsync(linked.Token).ConfigureAwait(false);
            FeedbackResponse? payload = null;
            try
            {
                payload = await JsonSerializer.DeserializeAsync<FeedbackResponse>(stream,
                    new JsonSerializerOptions { PropertyNameCaseInsensitive = true }, linked.Token).ConfigureAwait(false);
            }
            catch (JsonException) when (!response.IsSuccessStatusCode) { }
            if (response.IsSuccessStatusCode && payload is { Success: true } &&
                !string.IsNullOrWhiteSpace(payload.ReportId))
                return new(true, payload.ReportId, FeedbackFailureKind.None,
                    IssueNumber: payload.IssueNumber, IssueUrl: payload.IssueUrl);
            return new(false, null, MapFailure(response.StatusCode, payload?.Error),
                payload?.Error ?? $"HTTP {(int)response.StatusCode}");
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return new(false, null, FeedbackFailureKind.Cancelled);
        }
        catch (OperationCanceledException)
        {
            return new(false, null, FeedbackFailureKind.Timeout);
        }
        catch (CloudNetworkException ex)
        {
            var failure = ex.Kind switch
            {
                CloudNetworkFailureKind.ProxyConnectionFailed => FeedbackFailureKind.ProxyUnavailable,
                CloudNetworkFailureKind.ProxyAndDirectConnectionFailed => FeedbackFailureKind.ProxyAndDirectUnavailable,
                CloudNetworkFailureKind.InvalidProxy => FeedbackFailureKind.InvalidProxy,
                _ => FeedbackFailureKind.NetworkUnavailable,
            };
            return new(false, null, failure, CloudHttpClientFactory.UserMessage(ex.Kind, "feedback"));
        }
        catch (HttpRequestException ex)
        {
            return new(false, null, timer.Elapsed >= TimeSpan.FromSeconds(14)
                ? FeedbackFailureKind.Timeout : FeedbackFailureKind.NetworkUnavailable, ex.Message);
        }
        catch (IOException ex)
        {
            return new(false, null, FeedbackFailureKind.NetworkUnavailable, ex.Message);
        }
        catch (JsonException ex)
        {
            return new(false, null, FeedbackFailureKind.ServerRejected, ex.Message);
        }
    }

    private sealed class FeedbackResponse
    {
        public bool Success { get; set; }
        public string? ReportId { get; set; }
        public int? IssueNumber { get; set; }
        public string? IssueUrl { get; set; }
        public string? Error { get; set; }
    }

    private static FeedbackFailureKind MapFailure(HttpStatusCode status, string? error) => error switch
    {
        "github_unavailable" => FeedbackFailureKind.GithubUnavailable,
        "github_timeout" => FeedbackFailureKind.GithubTimeout,
        "github_auth_failed" => FeedbackFailureKind.GithubConfiguration,
        "github_rate_limited" => FeedbackFailureKind.GithubRateLimited,
        "github_asset_upload_failed" => FeedbackFailureKind.GithubAssetUploadFailed,
        "github_issue_create_failed" => FeedbackFailureKind.GithubIssueCreateFailed,
        "payload_too_large" => FeedbackFailureKind.PayloadTooLarge,
        "temporarily_unavailable" => FeedbackFailureKind.ServerUnavailable,
        "rate_limited" => FeedbackFailureKind.RateLimited,
        _ when status == HttpStatusCode.RequestEntityTooLarge => FeedbackFailureKind.PayloadTooLarge,
        _ when status is HttpStatusCode.ServiceUnavailable or HttpStatusCode.BadGateway or HttpStatusCode.GatewayTimeout
            => FeedbackFailureKind.ServerUnavailable,
        _ => FeedbackFailureKind.ServerRejected,
    };

    public void Dispose()
    {
        if (_ownsHttpClients) _httpClients?.Dispose();
    }
}
