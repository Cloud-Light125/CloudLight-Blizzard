using CloudLightBlizzard.Services.Drops;
using CloudLightBlizzard.ViewModels.Visuals;
using System.Text.Json;

namespace CloudLightBlizzard.ViewModels;

public sealed partial class DropsViewModel
{
    private readonly Dictionary<DropsPlatform, PlatformOverviewModel> _visuals = [];
    public PlatformOverviewModel TwitchVisual => VisualFor(DropsPlatform.Twitch);
    public PlatformOverviewModel SoopVisual => VisualFor(DropsPlatform.Soop);
    public PlatformOverviewModel BilibiliVisual => VisualFor(DropsPlatform.Bilibili);
    public PlatformOverviewModel YouTubeVisual => VisualFor(DropsPlatform.YouTube);
    public IReadOnlyList<PlatformOverviewModel> VisualPlatforms => [SoopVisual, TwitchVisual, BilibiliVisual, YouTubeVisual];
    private PlatformOverviewModel VisualFor(DropsPlatform platform)
    {
        if (!_visuals.TryGetValue(platform, out var model)) _visuals[platform] = model = new(For(platform));
        return model;
    }
    private void ApplyVisualState(DropsPlatform platform, JsonElement state) => VisualFor(platform).Apply(state);
}
