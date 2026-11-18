using System.IO;
using System.Text;
using System.Text.Json;

namespace CloudLightBlizzard.Services.Drops;

/// <summary>Offline acceptance of the installed binaries, using synthetic data only.</summary>
internal static class PackagedWorkersSelfTest
{
    internal static async Task RunAsync(string output, StringBuilder report)
    {
        void Check(bool ok, string message)
        {
            if (!ok) throw new InvalidOperationException(message);
            report.AppendLine("PASS " + message);
        }
#if !CLOUDLIGHT_LIVE_AUDIT
        var assembly = typeof(App).Assembly;
        Check(assembly.GetType("CloudLightBlizzard.Services.Drops.TwitchLiveAudit") is null &&
              assembly.GetType("CloudLightBlizzard.Services.Drops.RealDropsEvidenceRecorder") is null,
              "Release contains no real-account audit or evidence-recorder implementation");
#endif
        using var budget = new CancellationTokenSource(TimeSpan.FromMinutes(3));
        string[] platforms = ["twitch", "soop", "bilibili", "youtube"];
        foreach (var platform in platforms)
            Check(File.Exists(Path.Combine(AppContext.BaseDirectory, "_internal", "drops", platform, platform + "-worker.exe")),
                platform + " packaged executable exists; source fallback is unnecessary");

        var data = Path.Combine(AppPaths.Current.Root, "drops", "twitch");
        Directory.CreateDirectory(data);
        var cookie = Path.Combine(data, "cookies.jar");
        var journal = Path.Combine(data, "claim-journal.json");
        const string opaqueCookie = "isolated-synthetic-session-never-authenticate";
        var journalText = "{\"" + new string('a', 64) + "\":\"unconfirmed\"}";
        File.WriteAllText(cookie, opaqueCookie);
        File.WriteAllText(journal, journalText);
        await using (var host = new DropsHostService())
        {
            foreach (var platform in Enum.GetValues<DropsPlatform>())
            {
                var hello = await host.RequestAsync(platform, "hello", cancellationToken: budget.Token);
                Check(hello.GetProperty("platform").GetString()!.Equals(platform.ToString(), StringComparison.OrdinalIgnoreCase),
                    platform + " actual Host/JSONL handshake");
                var identity = await host.RequestAsync(platform, "provenance_check", cancellationToken: budget.Token);
                Check(identity.GetProperty("frozen").GetBoolean() && identity.GetProperty("protocol").GetInt32() == 1,
                    platform + " runs a frozen binary with protocol 1");
                var state = await host.RequestAsync(platform, "load_state", cancellationToken: budget.Token);
                Check(!state.GetProperty("running").GetBoolean() && state.GetProperty("_workerGeneration").GetInt64() > 0,
                    platform + " stopped state and Host generation");
                await host.RequestAsync(platform, "get_inventory", cancellationToken: budget.Token);
                if (platform == DropsPlatform.Twitch)
                {
                    Check(state.GetProperty("coreAvailable").GetBoolean() && !state.GetProperty("settings").GetProperty("auto_claim_drops").GetBoolean(),
                        "Twitch frozen Core loads and automatic claiming remains off by default");
                    await host.RequestAsync(platform, "get_campaigns", cancellationToken: budget.Token);
                    var one = await host.RequestAsync(platform, "claim_drop", new { id = "synthetic-never-submit", campaignId = "synthetic" }, budget.Token);
                    var batch = await host.RequestAsync(platform, "claim_all_drops", cancellationToken: budget.Token);
                    Check(one.GetProperty("status").GetString() == "not_running" && batch.GetProperty("status").GetString() == "not_running",
                        "frozen single and batch commands reject an offline session without a Claim");
                    foreach (var mode in new[] { "ending", "remaining", "progress_high", "progress_low", "custom" })
                    {
                        var saved = await host.RequestAsync(platform, "save_settings", new { settings = new { campaign_watch_priority =
                            new Dictionary<string, object> { ["synthetic-game"] = new { mode, campaignIds = new[] { "b", "a" } } } } }, budget.Token);
                        Check(saved.GetProperty("campaign_watch_priority").GetProperty("synthetic-game").GetProperty("mode").GetString() == mode,
                            "frozen Twitch saves campaign priority " + mode);
                    }
                }
                if (platform == DropsPlatform.Soop)
                {
                    Check(identity.GetProperty("core").GetProperty("matches").GetBoolean() &&
                          state.GetProperty("coreCapabilities").GetProperty("forceRefreshAccount").GetBoolean(),
                        "SOOP exact embedded Core and per-account refresh protection are present");
                    var saved=await host.RequestAsync(platform,"save_settings",new{settings=new{campaign_selection_mode="selected_only",selected_games=new[]{"game:a","game:b","game:c"},game_priority=new[]{"game:c","game:a","game:b"},campaign_priorities=new Dictionary<string,string[]>{{"game:a",["a2","a1"]},{"game:b",["b1"]}},campaign_sort_mode="custom"}},budget.Token);
                    Check(saved.GetProperty("selected_games").GetArrayLength()==3&&saved.GetProperty("campaign_priorities").GetProperty("game:a")[0].GetString()=="a2","SOOP frozen JSONL saves three games and multiple ordered activities");
                    Check(state.GetProperty("campaigns").GetArrayLength()==0,"SOOP frozen public catalog is independent of an empty account inventory");
                }
                var stopped = await host.StopAsync(platform, budget.Token);
                Check(!stopped.GetProperty("running").GetBoolean(), platform + " normal stop");
            }
        }
        await using (var restarted = new DropsHostService())
        {
            var soop=await restarted.RequestAsync(DropsPlatform.Soop,"load_state",cancellationToken:budget.Token);
            Check(soop.GetProperty("settings").GetProperty("game_priority")[0].GetString()=="game:c"&&soop.GetProperty("settings").GetProperty("campaign_priorities").GetProperty("game:a").GetArrayLength()==2&&!soop.GetProperty("running").GetBoolean(),"SOOP priorities survive a new frozen Worker process without starting watching");
            var state = await restarted.RequestAsync(DropsPlatform.Twitch, "load_state", cancellationToken: budget.Token);
            Check(state.GetProperty("settings").GetProperty("campaign_watch_priority").GetProperty("synthetic-game").GetProperty("mode").GetString() == "custom",
                "frozen Twitch restores campaign priority after a new Worker process");
            Check(!state.GetProperty("running").GetBoolean() && state.GetProperty("claimOperations").GetProperty("submitted").GetInt32() == 0,
                "Worker restart submits zero Claims and starts no watching");
        }
        Check(File.ReadAllText(cookie) == opaqueCookie && File.ReadAllText(journal) == journalText,
            "synthetic Session and uncertain claim-journal survive stop and restart unchanged");
        File.WriteAllText(Path.Combine(output, "worker-scope.txt"),
            "Actual frozen processes: offline handshake, configuration, session-file preservation and shutdown. " +
            "Claim fault injection and nonempty inventory: separately tested using verified binary-extracted code. " +
            "Real Session validation and live Inventory are NOT TESTED in this run.");
    }
}
