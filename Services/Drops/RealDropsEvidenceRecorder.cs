using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using CloudLightBlizzard.Controls;
using CloudLightBlizzard.ViewModels;
using CloudLightBlizzard.ViewModels.Visuals;

namespace CloudLightBlizzard.Services.Drops;

/// <summary>Opt-in passive evidence for the real isolated application. No queries, authentication or UI input.</summary>
internal sealed class RealDropsEvidenceRecorder : TraceListener
{
    private readonly MainWindow _window;
    private readonly DropsHostService _host;
    private readonly DropsViewModel _drops;
    private readonly string _output;
    private readonly object _gate = new();
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromSeconds(5) };
    private string _lastStateKey = "", _lastUiHash = "";
    private int _states;
    private static readonly HashSet<string> Fields = new(StringComparer.Ordinal)
    {
        "id", "sequence", "_emittedAt", "event", "ok", "result", "payload", "state", "running", "authState",
        "connectionState", "inventorySource", "lastInventorySyncAt", "refreshCompleted", "campaigns", "inventory",
        "currentDrop", "currentChannel", "channels", "name", "game", "campaign", "campaignId", "dropId", "benefits",
        "imageUrl", "gameImageUrl", "type", "currentMinutes", "requiredMinutes", "estimatedExtraMinutes", "progress",
        "completed", "claimed", "canClaim", "preconditionDropIds", "preconditionsMet", "preconditionsSatisfiedForMining",
        "eligible", "available", "availability", "linked", "active", "expired", "upcoming", "finished", "excluded",
        "priority", "startsAt", "endsAt", "drops", "totalDrops", "claimedDrops", "completedDrops", "remainingMinutes",
        "online", "dropsEnabled", "watchRequests", "channelId", "attempts", "lastResult", "lastAttemptAt", "lastSuccessAt",
        "successes", "failures", "consecutiveFailures", "playlistStatus", "segmentStatus", "failure", "code", "stage", "lastAcceptedAt", "accepted", "httpStatus", "transport", "benefitIds",
        "status", "phase", "method", "http", "host", "path", "route", "attempt", "elapsedMs", "replayable",
        "auto_claim_drops", "settings", "_workerGeneration", "_workerSequence", "_stateTime", "_snapshotStarted",
        "watchSchedule", "rank", "campaignName", "eligibleCampaignIds", "claimOperations", "submitted", "gameId"
    };

    internal static void Attach(MainWindow window, string output)
    {
        var flags = BindingFlags.Instance | BindingFlags.NonPublic;
        var main = (MainViewModel)typeof(MainWindow).GetField("_vm", flags)!.GetValue(window)!;
        var page = (Views.Pages.DropsPage)typeof(MainWindow).GetField("_dropsPage", flags)!.GetValue(window)!;
        _ = new RealDropsEvidenceRecorder(window, main.DropsHost, (DropsViewModel)page.DataContext, output);
    }

    private RealDropsEvidenceRecorder(MainWindow window, DropsHostService host, DropsViewModel drops, string output)
    {
        output=Path.Combine(output,DateTimeOffset.Now.ToString("yyyyMMdd-HHmmss")+"-"+Environment.ProcessId);
        _window = window; _host = host; _drops = drops; _output = output;
        Directory.CreateDirectory(output);
        _host.ProtocolObserved += Observe;
        _timer.Tick += (_, _) => CaptureUi();
        _timer.Start();
        PresentationTraceSources.DataBindingSource.Switch.Level = SourceLevels.Error;
        PresentationTraceSources.DataBindingSource.Listeners.Add(this);
        _window.Closed += (_, _) =>
        {
            _timer.Stop(); _host.ProtocolObserved -= Observe;
            PresentationTraceSources.DataBindingSource.Listeners.Remove(this);
        };
        File.WriteAllText(Path.Combine(output, "identity.json"), JsonSerializer.Serialize(new
        {
            source = "REAL WPF and packaged Worker; passive observer", BuildProvenance.BuildCommit,
            BuildProvenance.BuildTimestamp, startedAt = DateTimeOffset.Now, processId = Environment.ProcessId,
            claimsSubmittedByRecorder = 0, queriesSubmittedByRecorder = 0
        }));
        File.WriteAllText(Path.Combine(output, "binding-errors.txt"), "");
    }

    private static string Hash(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();
    internal static JsonElement SafeSnapshot(JsonElement value)=>JsonSerializer.SerializeToElement(Safe(value));
    private static object? Safe(JsonElement value)
    {
        if (value.ValueKind == JsonValueKind.Object)
        {
            var result = new Dictionary<string, object?>();
            foreach (var field in value.EnumerateObject().Where(p => Fields.Contains(p.Name)))
            {
                if (field.Name.EndsWith("Url", StringComparison.Ordinal) && field.Value.ValueKind == JsonValueKind.String)
                {
                    var raw = field.Value.GetString() ?? "";
                    result[field.Name] = Uri.TryCreate(raw, UriKind.Absolute, out var uri) ? uri.GetLeftPart(UriPartial.Path) : "";
                    result[field.Name + "Sha256"] = Hash(raw);
                }
                else result[field.Name] = Safe(field.Value);
            }
            return result;
        }
        if (value.ValueKind == JsonValueKind.Array) return value.EnumerateArray().Select(Safe).ToArray();
        return value.ValueKind == JsonValueKind.String ? SensitiveDataRedactor.Redact(value.GetString() ?? "") :
            value.ValueKind is JsonValueKind.Undefined or JsonValueKind.Null ? null : value.Clone();
    }

    private void Observe(DropsPlatform platform, long generation, JsonElement envelope)
    {
        if (platform != DropsPlatform.Twitch) return;
        try
        {
            var state = V.Get(envelope, "result");
            if (V.Has(state, "state")) state = V.Get(state, "state");
            if (!V.Has(state, "inventory")) state = V.Get(envelope, "payload");
            var current = V.Get(state, "currentDrop");
            lock (_gate)
            {
                var key = generation + "|" + V.Bool(state, "running") + "|" + V.Text(state, "lastInventorySyncAt") + "|" + V.Text(current, "id") + "|" + V.Number(current, "currentMinutes");
                if (V.Has(state, "inventory") && key != _lastStateKey && _states < 512)
                {
                    _lastStateKey = key;
                    File.WriteAllText(Path.Combine(_output, $"state-{++_states:0000}.json"), JsonSerializer.Serialize(new
                    { source = "Worker JSONL received by Host", receivedAt = DateTimeOffset.Now, generation, state = Safe(state) }));
                }
                var eventName = V.Text(envelope, "event");
                if (eventName is "watch_request" or "watch_schedule" or "claim_progress" or "claim_result" or "current_channel" or "drop" or "network_request" or "connection_status" || V.Has(state, "inventory"))
                    File.AppendAllText(Path.Combine(_output, "protocol-summary.jsonl"), JsonSerializer.Serialize(new
                    {
                        receivedAt = DateTimeOffset.Now, generation, eventName, sequence = V.Number(envelope, "sequence"),
                        inventoryCount = V.Rows(state, "inventory").Count(), campaignCount = V.Rows(state, "campaigns").Count(),
                        running = V.Bool(state, "running"), currentDrop = Safe(current), currentChannel = Safe(V.Get(state, "currentChannel")),
                        watchRequests = Safe(V.Get(state, "watchRequests")), payload = eventName.Length > 0 ? Safe(V.Get(envelope, "payload")) : null
                    }) + Environment.NewLine);
            }
        }
        catch (Exception ex) { RecordFailure(ex); }
    }

    private void CaptureUi()
    {
        try
        {
            var visual = _drops.TwitchVisual;
            var images = Descendants(_window).OfType<VisualImage>().Where(i => i.IsVisible).Select(i =>
            {
                var sourceHash = Hash(i.SourceUrl ?? "");
                var cache = Path.Combine(AppPaths.Current.Root, "cache", "visual-images", sourceHash + ".img");
                Rect bounds;
                try { bounds = i.TransformToAncestor(_window).TransformBounds(new Rect(0, 0, i.ActualWidth, i.ActualHeight)); }
                catch { bounds = Rect.Empty; }
                return new
                {
                    sourceHash, sourceHost = Uri.TryCreate(i.SourceUrl, UriKind.Absolute, out var uri) ? uri.Host : "",
                    benefitId = (i.DataContext as BenefitVisual)?.Id, dropId = (i.DataContext as RewardVisual)?.Id,
                    loaded = i.LoadedImage is not null, i.LoadState, pixelWidth = i.LoadedImage?.PixelWidth, pixelHeight = i.LoadedImage?.PixelHeight,
                    cachePresent = File.Exists(cache), cacheBytes = File.Exists(cache) ? new FileInfo(cache).Length : 0,
                    visibleInViewport = !bounds.IsEmpty && bounds.IntersectsWith(new Rect(0, 0, _window.ActualWidth, _window.ActualHeight)),
                    bounds = new { x = bounds.IsEmpty ? 0 : bounds.X, y = bounds.IsEmpty ? 0 : bounds.Y, width = i.ActualWidth, height = i.ActualHeight }
                };
            }).ToArray();
            var tiles=Descendants(_window).OfType<System.Windows.Controls.Border>().Where(b=>b.IsVisible&&b.Tag as string=="RewardTile").Select(b=>
            {
                var bounds=b.TransformToAncestor(_window).TransformBounds(new Rect(0,0,b.ActualWidth,b.ActualHeight));
                var viewport=Ancestors(b).OfType<CampaignInventoryView>().FirstOrDefault();
                var clip=viewport is null?new Rect(0,0,_window.ActualWidth,_window.ActualHeight):viewport.TransformToAncestor(_window).TransformBounds(new Rect(0,0,viewport.ActualWidth,viewport.ActualHeight));
                var card=b.DataContext as RewardCardVisual;
                return new{card?.Key,dropId=card?.Drop.Id,campaignId=card?.Drop.CampaignId,benefitId=card?.Benefit?.Id,official=card?.Drop.Current,required=card?.Drop.Required,percent=card?.Drop.Percent,state=card?.Drop.StateText,x=bounds.X,y=bounds.Y,bounds.Width,bounds.Height,intersects=bounds.IntersectsWith(clip),fullyVisible=clip.Contains(bounds)};
            }).ToArray();
            var sample = new
            {
                running = _drops.Twitch.Running, _drops.Twitch.Status, _drops.Twitch.WorkerHealth, _drops.Twitch.ConnectionStateText,
                visual.Channel, activeDropId = visual.ActiveReward?.Id, featuredDropId = visual.FeaturedReward?.Id,
                campaignId = visual.FeaturedReward?.CampaignId, officialMinutes = visual.FeaturedReward?.Current,
                requiredMinutes = visual.FeaturedReward?.Required, percent = visual.FeaturedReward?.Percent, estimate = visual.FeaturedReward?.Estimate,
                benefits = visual.FeaturedReward?.Benefits.Select(b => new { b.Id, b.Name, imageUrlSha256 = Hash(b.ImageUrl) }), images,
                tiles, window = new { _window.ActualWidth, _window.ActualHeight, dpi = VisualTreeHelper.GetDpi(_window).PixelsPerInchX }
                ,claimActions=_drops.TwitchInventory.AllCampaigns.SelectMany(c=>c.Drops).Select(d=>new{d.Id,d.CampaignId,d.Claimed,d.CanClaim,d.Current,d.Required,button=d.ClaimAction?.ButtonText,enabled=d.ClaimAction?.CanExecute,busy=d.ClaimAction?.Busy,message=d.ClaimAction?.Message}),
                _drops.TwitchInventory.PrioritySummary,_drops.TwitchInventory.BatchClaimSummary
            };
            var json = JsonSerializer.Serialize(sample);
            if (Hash(json) == _lastUiHash) return;
            _lastUiHash = Hash(json);
            File.AppendAllText(Path.Combine(_output, "wpf-samples.jsonl"), JsonSerializer.Serialize(new { sampledAt = DateTimeOffset.Now, sample }) + Environment.NewLine);
            if(_window.ActualWidth>0&&_window.ActualHeight>0&&_window.IsVisible)
            {
                var bitmap=new RenderTargetBitmap((int)_window.ActualWidth,(int)_window.ActualHeight,96,96,PixelFormats.Pbgra32);
                bitmap.Render(_window);var png=new PngBitmapEncoder();png.Frames.Add(BitmapFrame.Create(bitmap));
                using var stream=File.Create(Path.Combine(_output,"wpf-latest.png"));png.Save(stream);
            }
        }
        catch (Exception ex) { RecordFailure(ex); }
    }

    private static IEnumerable<DependencyObject> Descendants(DependencyObject root)
    {
        yield return root;
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
            foreach (var child in Descendants(VisualTreeHelper.GetChild(root, i))) yield return child;
    }
    private static IEnumerable<DependencyObject> Ancestors(DependencyObject root)
    {for(var parent=VisualTreeHelper.GetParent(root);parent is not null;parent=VisualTreeHelper.GetParent(parent))yield return parent;}
    private void RecordFailure(Exception ex)
    {
        try { lock (_gate) File.AppendAllText(Path.Combine(_output, "recorder-errors.txt"), ex.GetType().Name + Environment.NewLine); } catch { }
    }
    public override void Write(string? message) { }
    public override void WriteLine(string? message)
    {
        try { lock (_gate) File.AppendAllText(Path.Combine(_output, "binding-errors.txt"), SensitiveDataRedactor.Redact(message ?? "") + Environment.NewLine); } catch { }
    }
}
