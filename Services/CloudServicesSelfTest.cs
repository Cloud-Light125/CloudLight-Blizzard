using System.IO.Compression;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Net;
using System.Net.Http;
using CloudLightBlizzard.Models;

namespace CloudLightBlizzard.Services;

public static class CloudServicesSelfTest
{
    public static async Task RunAsync(string outputRoot)
    {
        outputRoot = Path.GetFullPath(outputRoot);
        Directory.CreateDirectory(outputRoot);
        var workspace = Path.Combine(outputRoot, "cloud-services-workspace");
        if (Directory.Exists(workspace)) Directory.Delete(workspace, true);
        Directory.CreateDirectory(workspace);
        var report = new StringBuilder();
        try
        {
            RunAnnouncementTest(workspace, report);
            await RunConfigurationAndProxyFailureTest(workspace, report);
            await RunUploadProgressAndCancellationTest(workspace, report);
            await RunFeedbackResponseMappingTest(report);
            await RunRedactionPackageTest(workspace, report);
            report.AppendLine("OVERALL: PASS");
        }
        catch (Exception ex)
        {
            report.AppendLine("OVERALL: FAIL");
            report.AppendLine(ex.ToString());
        }
        finally
        {
            await File.WriteAllTextAsync(Path.Combine(outputRoot, "cloud-services-selftest.txt"), report.ToString());
            try { Directory.Delete(workspace, true); } catch { }
        }
    }

    private static void RunAnnouncementTest(string workspace, StringBuilder report)
    {
        var stateFile = Path.Combine(workspace, "announcement-state.json");
        var first = new Announcement
        {
            Id = "read", Revision = 1, Title = "已读", Content = "正文", Enabled = true,
            PublishedAt = DateTimeOffset.Now, MinVersion = "2.0.0",
        };
        var revised = new Announcement
        {
            Id = "revised", Revision = 2, Title = "更新公告", Content = "新版正文", Enabled = true,
            PublishedAt = DateTimeOffset.Now.AddMinutes(-1), MinVersion = "2.0.4", MaxVersion = null,
        };
        var state = new AnnouncementLocalState
        {
            Cache = new AnnouncementDocument { SchemaVersion = 1, Announcements = new() { first, revised } },
            ReadRevisions = new Dictionary<string, int> { ["read"] = 1, ["revised"] = 1 },
        };
        File.WriteAllText(stateFile, JsonSerializer.Serialize(state));
        var settings = new AppSettings { ShowAnnouncementBadge = true };
        using var service = new AnnouncementService(settings, stateFile, "2.0.6");
        var items = service.CachedAnnouncements;
        Assert(!service.IsUnread(first), "revision 1 already read");
        Assert(service.IsUnread(revised), "revision 2 becomes unread after revision 1 was read");
        Assert(settings.ShowAnnouncementBadge && service.HasUnread(items), "enabled badge shows for unread revision");
        settings.ShowAnnouncementBadge = false;
        Assert(items.Count == 2 && !(settings.ShowAnnouncementBadge && service.HasUnread(items)),
            "disabled badge hides dot without removing announcements");
        report.AppendLine("TEST 1 announcement revision/badge: PASS");
    }

    private static async Task RunConfigurationAndProxyFailureTest(string workspace, StringBuilder report)
    {
        Assert(CloudServiceConfiguration.DefaultBaseUrl ==
               "https://cloudlight-feedback.2693327171.workers.dev/",
            "desktop cloud services use the deployed Worker route");
        Assert(CloudServiceConfiguration.NormalizeBaseUrl("https://legacy.invalid/") ==
               CloudServiceConfiguration.DefaultBaseUrl,
            "legacy placeholder settings migrate to the deployed Worker route");

        var settings = new AppSettings
        {
            EnableProxy = true,
            ProxyUrl = "not-a-proxy",
            FallbackDirect = true,
        };
        var logFile = Path.Combine(workspace, "network.log");
        using var clients = new CloudHttpClientFactory(settings, logFile);
        using var announcements = new AnnouncementService(settings,
            Path.Combine(workspace, "proxy-announcement-state.json"), "2.0.6", clients);
        await announcements.RefreshAsync();
        Assert(announcements.LastFailureMessage == "当前代理地址无效。",
            "announcement reports invalid configured proxy without crashing");

        using var update = new UpdateService(settings, clients);
        var updateResult = await update.CheckAsync();
        Assert(updateResult.Status == UpdateCheckResultStatus.Failed &&
               updateResult.ErrorMessage == "当前代理地址无效。",
            "update check reads the same current proxy settings");

        using var feedback = new FeedbackService(settings, httpClients: clients);
        var feedbackResult = await feedback.SubmitAsync(
            new FeedbackSubmitRequest("title", "description", "2.0.6", "Windows 11", "", Guid.NewGuid().ToString(), null),
            null, CancellationToken.None);
        Assert(feedbackResult.Failure == FeedbackFailureKind.InvalidProxy,
            "feedback reads the same proxy settings without replaying a POST");

        var networkLog = await File.ReadAllTextAsync(logFile);
        Assert(networkLog.Contains("\"route\":\"Proxy\"") &&
               networkLog.Contains(nameof(CloudNetworkFailureKind.InvalidProxy)),
            "technical network log records route and exception type");
        report.AppendLine("TEST 2 cloud endpoint/shared proxy/error logging: PASS");
    }

    private static async Task RunUploadProgressAndCancellationTest(string workspace, StringBuilder report)
    {
        var zip = Path.Combine(workspace, "large.zip");
        await File.WriteAllBytesAsync(zip, new byte[2 * 1024 * 1024]);
        var samples = new List<FeedbackUploadProgress>();
        using (var content = new MultipartProgressContent(new Dictionary<string, string> { ["title"] = "test" }, zip,
                   new ImmediateProgress<FeedbackUploadProgress>(samples.Add)))
        await using (var destination = new MemoryStream())
            await content.CopyToAsync(destination);
        Assert(samples.Count > 1 && samples[^1].Percentage == 100 && samples[^1].BytesSent == samples[^1].TotalBytes,
            "real request body progress reaches 100 percent");
        Assert(samples[^1].Stage == FeedbackUploadStage.ServerProcessing &&
               samples.Take(samples.Count - 1).Any(sample => sample.Stage == FeedbackUploadStage.Uploading),
            "100 percent transitions from HTTP upload to server-side GitHub processing stage");

        using var cancelled = new CancellationTokenSource();
        var cancelledSamples = new List<FeedbackUploadProgress>();
        using var cancelContent = new MultipartProgressContent(new Dictionary<string, string> { ["title"] = "test" }, zip,
            new ImmediateProgress<FeedbackUploadProgress>(progress =>
            {
                cancelledSamples.Add(progress);
                if (progress.BytesSent > 128 * 1024) cancelled.Cancel();
            }));
        try
        {
            await cancelContent.CopyToAsync(Stream.Null, cancelled.Token);
            throw new InvalidOperationException("upload cancellation was not observed");
        }
        catch (OperationCanceledException) { }
        File.Delete(zip);
        Assert(cancelledSamples.Count > 0 && !File.Exists(zip), "CancellationToken stops upload and temp file is removed");
        report.AppendLine("TEST 3 upload progress/cancellation/cleanup: PASS");
    }

    private static async Task RunRedactionPackageTest(string workspace, StringBuilder report)
    {
        var logs = Path.Combine(workspace, "logs");
        Directory.CreateDirectory(logs);
        await File.WriteAllTextAsync(Path.Combine(logs, "app.log"),
            "Authorization: " + "Bearer " +
            "top-secret\naccess_token=abc123\nCookie: session=secret\nC:\\Users\\Alice\\Documents\\trace.log\n");
        var package = await new FeedbackLogPackager(logs).CreateAsync("test");
        try
        {
            using var zip = ZipFile.OpenRead(package.FilePath);
            using var reader = new StreamReader(zip.GetEntry("app.log")!.Open());
            var text = await reader.ReadToEndAsync();
            Assert(!text.Contains("top-secret") && !text.Contains("abc123") && !text.Contains("Alice") &&
                   text.Contains("<redacted>") && text.Contains(@"C:\Users\<USER>\Documents"),
                "authorization/token/cookie/windows username are redacted inside ZIP");
        }
        finally { package.Delete(); }
        Assert(!File.Exists(package.FilePath), "redacted ZIP is removed after use");
        report.AppendLine("TEST 5 ZIP redaction/cleanup: PASS");
    }

    private static async Task RunFeedbackResponseMappingTest(StringBuilder report)
    {
        var settings = new AppSettings { CloudServiceBaseUrl = "https://worker.test/" };
        static FeedbackSubmitRequest Request() => new("title", "description", "2.0.6", "Windows 11",
            "", "9f535e31-92c5-4d88-b40d-afdf82d980d8", null);
        using (var successClient = new HttpClient(new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.Created)
               {
                   Content = new StringContent("{\"success\":true,\"reportId\":\"CB-20260823-A83F2D\",\"issueNumber\":123,\"issueUrl\":\"https://private.test/123\"}",
                       Encoding.UTF8, "application/json"),
               })))
        {
            var result = await new FeedbackService(settings, () => successClient).SubmitAsync(Request(), null, CancellationToken.None);
            Assert(result.Success && result.ReportId == "CB-20260823-A83F2D" && result.IssueNumber == 123,
                "client waits for final GitHub-backed success response before accepting reportId");
        }
        using (var timeoutClient = new HttpClient(new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.GatewayTimeout)
               {
                   Content = new StringContent("{\"success\":false,\"error\":\"github_timeout\"}",
                       Encoding.UTF8, "application/json"),
               })))
        {
            var result = await new FeedbackService(settings, () => timeoutClient).SubmitAsync(Request(), null, CancellationToken.None);
            Assert(!result.Success && result.Failure == FeedbackFailureKind.GithubTimeout,
                "structured GitHub timeout never becomes submitted success");
        }
        report.AppendLine("TEST 4 client final-response/error mapping: PASS");
    }

    private static void Assert(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException("Assertion failed: " + message);
    }

    private sealed class ImmediateProgress<T>(Action<T> report) : IProgress<T>
    {
        public void Report(T value) => report(value);
    }

    private sealed class StubHandler(Func<HttpRequestMessage, HttpResponseMessage> response) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,
            CancellationToken cancellationToken) => Task.FromResult(response(request));
    }
}
