using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using CloudLightBlizzard.Models;

namespace CloudLightBlizzard.Services;

internal static class UpdateSafetySelfTest
{
    internal static async Task RunAsync(string workspace, StringBuilder report)
    {
        void Check(bool ok, string label) { if (!ok) throw new InvalidOperationException(label); report.AppendLine("PASS " + label); }
        var bytes = Enumerable.Range(0, 16384).Select(i => (byte)(i % 251)).ToArray();
        bytes[0] = (byte)'M'; bytes[1] = (byte)'Z';
        foreach (var mode in new[] { "fallback", "proxy", "no-fallback", "range-etag", "range-date", "cancel", "bad-hash", "bad-size" })
        {
            var tag = "safety-" + Guid.NewGuid().ToString("N");
            var root = Path.Combine(UpdateDownloadService.DownloadRoot, tag);
            Directory.CreateDirectory(root);
            var result = new UpdateCheckResult
            {
                LatestVersion = "2.1.9", Tag = tag, InstallerName = "CloudLight-Blizzard-2.1.9-win-x64-Setup.exe",
                InstallerDownloadUrl = "https://github.com/Cloud-Light125/CloudLight-Blizzard/releases/download/v2.1.9/CloudLight-Blizzard-2.1.9-win-x64-Setup.exe",
                InstallerSize = mode == "bad-size" ? bytes.Length + 1 : bytes.Length,
                InstallerDigest = "sha256:" + Convert.ToHexString(SHA256.HashData(mode == "bad-hash" ? new byte[bytes.Length] : bytes)),
            };
            var resumed = mode.StartsWith("range-", StringComparison.Ordinal);
            const int offset = 4096;
            const string date = "Mon, 12 Oct 2026 12:00:00 GMT";
            if (resumed)
            {
                await File.WriteAllBytesAsync(Path.Combine(root, result.InstallerName) + ".partial", bytes[..offset]);
                await File.WriteAllTextAsync(Path.Combine(root, "update-download.json"), JsonSerializer.Serialize(new
                {
                    Version = result.LatestVersion, DownloadUrl = result.InstallerDownloadUrl,
                    ExpectedSize = bytes.Length, Digest = result.InstallerDigest, DownloadedBytes = offset,
                    ETag = mode == "range-etag" ? "\"fixture\"" : null, LastModified = date,
                }));
            }
            var proxyCalls = 0; var directCalls = 0;
            HttpRequestMessage? proxyRequest = null; HttpRequestMessage? directRequest = null;
            using var cancel = new CancellationTokenSource();
            using var factory = new CloudHttpClientFactory(new AppSettings
            {
                EnableProxy = true, ProxyUrl = "http://127.0.0.1:7897", FallbackDirect = mode != "no-fallback",
            }, Path.Combine(workspace, "update-" + mode + ".log"), proxy => new HttpClient(new Handler(async (request, token) =>
            {
                if (proxy is not null)
                {
                    proxyCalls++; proxyRequest = request;
                    if (mode == "cancel")
                    {
                        cancel.Cancel();
                        await Task.Delay(Timeout.Infinite, token);
                    }
                    if (mode != "proxy") throw new HttpRequestException("offline proxy fixture");
                }
                else { directCalls++; directRequest = request; }
                if (resumed)
                {
                    Check(request.Headers.Range?.Ranges.Single().From == offset, mode + " Range preserved");
                    Check(request.Headers.IfRange?.ToString() == (mode == "range-etag" ? "\"fixture\"" : date), mode + " If-Range preserved");
                }
                var response = new HttpResponseMessage(resumed ? HttpStatusCode.PartialContent : HttpStatusCode.OK)
                { Content = new ByteArrayContent(resumed ? bytes[offset..] : bytes) };
                response.Headers.ETag = new EntityTagHeaderValue("\"fixture\"");
                response.Content.Headers.LastModified = DateTimeOffset.Parse(date);
                if (resumed) response.Content.Headers.ContentRange = new ContentRangeHeaderValue(offset, bytes.Length - 1, bytes.Length);
                return response;
            })));
            var downloader = new UpdateDownloadService(factory, new[] { TimeSpan.Zero });
            Exception? error = null;
            string? downloaded = null;
            try { downloaded = await downloader.DownloadInstallerAsync(result, cancellationToken: cancel.Token); }
            catch (Exception ex) { error = ex; }
            try
            {
                if (mode == "no-fallback") Check(error is not null && directCalls == 0, "disabled fallback fails without direct request");
                else if (mode == "cancel") Check(error is OperationCanceledException && proxyCalls == 1 && directCalls == 0,
                    "cancellation stops routing without fallback/repeated download");
                else if (mode is "bad-hash" or "bad-size") Check(error is InvalidDataException && downloaded is null,
                    mode + " rejected after successful fallback");
                else
                {
                    Check(error is null && downloaded is not null && File.ReadAllBytes(downloaded).SequenceEqual(bytes), mode + " verified exact file content");
                    Check(downloader.State == UpdaterState.ReadyToInstall, mode + " length/MZ/SHA256 validation completed");
                    Check(proxyCalls == 1 && directCalls == (mode == "proxy" ? 0 : 1), mode + " expected handler counts");
                    if (mode != "proxy") Check(!ReferenceEquals(proxyRequest, directRequest), mode + " distinct request objects");
                    using var exclusive = new FileStream(downloaded!, FileMode.Open, FileAccess.Read, FileShare.None);
                    Check(exclusive.Length == bytes.Length, mode + " file handles released");
                }
            }
            finally { Directory.Delete(root, true); } // Fresh GUID fixture only; never a real updater directory.
        }
        var now = DateTime.UtcNow;
        void State(bool running, long? id, DateTime deadline, BattleNetLoginEvidence evidence, AccountSwitchVerificationState expected, string label) =>
            Check(AccountSwitchVerification.Evaluate(running, id, 2, now, deadline, evidence,
                targetObservedThisAttempt: expected == AccountSwitchVerificationState.LoggedIn) == expected, label);
        State(false, null, now.AddSeconds(1), BattleNetLoginEvidence.None, AccountSwitchVerificationState.WaitingForBattleNet, "absent client before deadline waits");
        State(false, null, now, BattleNetLoginEvidence.None, AccountSwitchVerificationState.Unconfirmed, "absent client at deadline terminates");
        State(false, 2, now, BattleNetLoginEvidence.None, AccountSwitchVerificationState.Unconfirmed, "stale target cache cannot confirm absent client");
        State(true, 1, now.AddSeconds(1), BattleNetLoginEvidence.None, AccountSwitchVerificationState.WaitingForLogin, "running client waits for target login");
        State(true, 1, now, BattleNetLoginEvidence.None, AccountSwitchVerificationState.Unconfirmed, "running wrong account times out");
        State(true, 2, now, BattleNetLoginEvidence.None, AccountSwitchVerificationState.LoggedIn, "confirmed target wins over deadline");
        State(true, 1, now, BattleNetLoginEvidence.RealAuthExpired, AccountSwitchVerificationState.LoginRequired, "real expiry precedes timeout");
        State(true, 2, now, BattleNetLoginEvidence.None, AccountSwitchVerificationState.Unconfirmed, "running client with only old cached target is not confirmed");
        State(true, 2, now, BattleNetLoginEvidence.RealAuthExpired, AccountSwitchVerificationState.LoginRequired, "old cached target cannot hide real auth expiry");
        State(false, null, now, BattleNetLoginEvidence.RealAuthExpired, AccountSwitchVerificationState.LoginRequired, "reliable auth expiry precedes deadline even if client subsequently exits");
    }
    private sealed class Handler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> send) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) => send(request, cancellationToken);
    }
}
