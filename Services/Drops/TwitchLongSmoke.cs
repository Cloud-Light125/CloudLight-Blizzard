using System.Diagnostics;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace CloudLightBlizzard.Services.Drops;

/// <summary>Opt-in real-network acceptance. Isolated existing Session, no Claim commands.</summary>
internal static class TwitchLongSmoke
{
    internal static async Task RunAsync(string outputRoot, StringBuilder report)
    {
        var repo = new DirectoryInfo(AppContext.BaseDirectory);
        while (repo is not null && !File.Exists(Path.Combine(repo.FullName, "test-output", "drops-round2", "manual-ui", "drops", "twitch", "cookies.jar"))) repo = repo.Parent;
        if (repo is null) throw new InvalidOperationException("BLOCKED：没有已授权的隔离 Twitch Session。");
        var temporary = Path.Combine(Path.GetTempPath(), "cloudlight-twitch-round3-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temporary);
        var data = Path.Combine(temporary, "data");
        Directory.CreateDirectory(data);
        File.Copy(Path.Combine(repo.FullName, "test-output", "drops-round2", "manual-ui", "drops", "twitch", "cookies.jar"), Path.Combine(data, "cookies.jar"));
        var samples = new List<object>();
        var errors = new List<string>();
        var sourceMode = Environment.GetEnvironmentVariable("CLOUDLIGHT_TWITCH_SMOKE_SOURCE") == "1";
        using var budget = new CancellationTokenSource(TimeSpan.FromMinutes(75));
        async Task<DropsHostService> Connect()
        {
            var host = new DropsHostService();
            host.ConfigureProxy(new(true, "http://127.0.0.1:7897", false));
            host.ConfigureStartInfoForSelfTest(_ =>
            {
                var info = new ProcessStartInfo(sourceMode
                    ? Path.Combine(repo.FullName, ".worker-build-venv", "Scripts", "python.exe")
                    : Path.Combine(repo.FullName, "artifacts", "drops", "twitch", "twitch-worker.exe"))
                { UseShellExecute = false, CreateNoWindow = true, RedirectStandardInput = true, RedirectStandardOutput = true,
                    RedirectStandardError = true, StandardInputEncoding = new UTF8Encoding(false),
                    StandardOutputEncoding = new UTF8Encoding(false), StandardErrorEncoding = new UTF8Encoding(false) };
                if (sourceMode) info.ArgumentList.Add(Path.Combine(repo.FullName, "Integrations", "Drops", "twitch", "worker.py"));
                foreach (var arg in new[] { "--data-dir", data, "--log-file", Path.Combine(temporary, "worker.log") }) info.ArgumentList.Add(arg);
                return info;
            });
            host.EventReceived += (_, e) => { if (e.Name is "error" or "connection_error") lock (errors) errors.Add(Text(e.Payload, "code", e.Name)); };
            await host.RequestAsync(DropsPlatform.Twitch, "save_settings", new { settings = new { auto_claim_drops = false, exclude = Array.Empty<string>(), priority = Array.Empty<string>(), priority_mode = "ENDING_SOONEST" } }, budget.Token);
            return host;
        }
        DropsHostService? host = null;
        try
        {
            host = await Connect();
            var state = await host.RequestAsync(DropsPlatform.Twitch, "auto_start", cancellationToken: budget.Token);
            var ready = Stopwatch.StartNew();
            while ((!state.TryGetProperty("currentDrop", out var current) || current.ValueKind != JsonValueKind.Object) && ready.Elapsed < TimeSpan.FromMinutes(2))
            {
                await Task.Delay(TimeSpan.FromSeconds(10), budget.Token);
                state = await host.RequestAsync(DropsPlatform.Twitch, "load_state", cancellationToken: budget.Token);
            }
            var dropId = state.TryGetProperty("currentDrop", out var selected) && selected.ValueKind == JsonValueKind.Object ? Text(selected, "id") : "";
            var campaignId = "";
            if (state.TryGetProperty("inventory", out var inventory))
                foreach (var drop in inventory.EnumerateArray()) if (Text(drop, "id") == dropId) campaignId = Text(drop, "campaignId");
            var account = state.TryGetProperty("accounts", out var accounts) && accounts.GetArrayLength() > 0 ? accounts[0].ToString() : "unknown";
            var accountTag = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(account)))[..12];
            var watch = Stopwatch.StartNew();
            for (var index = 0; index <= 6; index++)
            {
                if (index > 0)
                {
                    var delay = TimeSpan.FromMinutes(index * 10) - watch.Elapsed;
                    if (delay > TimeSpan.Zero) await Task.Delay(delay, budget.Token);
                }
                var refresh = false;
                string? queryFailure = null;
                try { state = await host.RequestAsync(DropsPlatform.Twitch, "refresh", cancellationToken: budget.Token); refresh = Bool(state, "refreshCompleted"); }
                catch (Exception ex) { queryFailure = ex.GetType().Name; state = await host.RequestAsync(DropsPlatform.Twitch, "load_state", cancellationToken: budget.Token); }
                var target = state.GetProperty("inventory").EnumerateArray().FirstOrDefault(d => Text(d, "id") == dropId && Text(d, "campaignId") == campaignId);
                var campaign = state.GetProperty("campaigns").EnumerateArray().FirstOrDefault(c => Text(c, "id") == campaignId);
                samples.Add(new { sampledAt = DateTimeOffset.Now.ToString("o"), elapsedSeconds = Math.Round(watch.Elapsed.TotalSeconds),
                    accountTag, campaignId, dropId, dropPresent = target.ValueKind == JsonValueKind.Object,
                    officialMinutes = target.ValueKind == JsonValueKind.Object ? Number(target, "currentMinutes") : (double?)null,
                    requiredMinutes = target.ValueKind == JsonValueKind.Object ? Number(target, "requiredMinutes") : (double?)null,
                    officialPercent = target.ValueKind == JsonValueKind.Object ? Number(target, "progress") * 100 : (double?)null,
                    estimatedExtraMinutes = target.ValueKind == JsonValueKind.Object ? Number(target, "estimatedExtraMinutes") : (double?)null,
                    claimed = Bool(target, "claimed"), completed = Bool(target, "completed"), eligible = Bool(campaign, "eligible"),
                    campaignAvailable = Bool(campaign, "available"), running = Bool(state, "running"),
                    channel = state.TryGetProperty("currentChannel", out var channel) ? channel.Clone() : default(JsonElement?),
                    websockets = state.TryGetProperty("websockets", out var ws) ? ws.EnumerateArray().Select(w => Text(w, "status")).ToArray() : [],
                    generation = Number(state, "_workerGeneration"), remoteRefreshCompleted = refresh, queryFailure,
                    watchRequests = state.TryGetProperty("watchRequests", out var watches) ? watches.Clone() : default(JsonElement?),
                    progressBlocked = string.IsNullOrEmpty(dropId) ? "no_current_eligible_reward" : target.ValueKind != JsonValueKind.Object ? "target_not_returned" : "",
                    inventorySource = Text(state, "inventorySource"), lastInventorySyncAt = Text(state, "lastInventorySyncAt") });
                File.WriteAllText(Path.Combine(outputRoot, "twitch-long-samples.json"), JsonSerializer.Serialize(samples, new JsonSerializerOptions { WriteIndented = true }));
            }
            var stopped = await host.StopAsync(DropsPlatform.Twitch, budget.Token);
            await host.DisposeAsync();
            host = await Connect();
            var restored = await host.RequestAsync(DropsPlatform.Twitch, "auto_start", cancellationToken: budget.Token);
            restored = await host.RequestAsync(DropsPlatform.Twitch, "refresh", cancellationToken: budget.Token);
            var finalDrop = restored.GetProperty("inventory").EnumerateArray().FirstOrDefault(d => Text(d, "id") == dropId && Text(d, "campaignId") == campaignId);
            File.WriteAllText(Path.Combine(outputRoot, "twitch-long-result.json"), JsonSerializer.Serialize(new {
                elapsedSeconds = Math.Round(watch.Elapsed.TotalSeconds), dropId, campaignId, stopped = !Bool(stopped, "running"),
                sessionRestored = Text(restored, "authState") is "running" or "authenticated",
                restartOfficialMinutes = finalDrop.ValueKind == JsonValueKind.Object ? Number(finalDrop, "currentMinutes") : (double?)null,
                restartRefresh = Bool(restored, "refreshCompleted"), errors, sourceMode, autoClaim = false, claimsSubmitted = 0 }, new JsonSerializerOptions { WriteIndented = true }));
            await host.StopAsync(DropsPlatform.Twitch, budget.Token);
            report.AppendLine("Twitch 60-minute real-network smoke completed; evaluate official samples, not only process health.");
        }
        finally
        {
            if (host is not null) await host.DisposeAsync();
            // Only the explicitly created temporary Session copy is removed.
            Directory.Delete(temporary, true);
        }
    }
    private static string Text(JsonElement e, string key, string fallback = "") => e.ValueKind == JsonValueKind.Object && e.TryGetProperty(key, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() ?? fallback : fallback;
    private static bool Bool(JsonElement e, string key) => e.ValueKind == JsonValueKind.Object && e.TryGetProperty(key, out var v) && v.ValueKind == JsonValueKind.True;
    private static double Number(JsonElement e, string key) => e.ValueKind == JsonValueKind.Object && e.TryGetProperty(key, out var v) && v.TryGetDouble(out var n) ? n : 0;
}
