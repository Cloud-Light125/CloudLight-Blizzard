namespace CloudLightBlizzard.ViewModels.Visuals;

/// <summary>Shared read-only evidence window; each platform supplies its own protocol conditions.</summary>
internal static class WatchEvidence
{
    internal static bool Recent(DateTimeOffset at) => at<=DateTimeOffset.UtcNow&&DateTimeOffset.UtcNow-at<TimeSpan.FromMinutes(3);
    internal static bool Recent(string value)=>DateTimeOffset.TryParse(value,out var at)&&Recent(at);
}
