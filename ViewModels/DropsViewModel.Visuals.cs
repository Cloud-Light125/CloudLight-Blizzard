using CloudLightBlizzard.Services.Drops;
using CloudLightBlizzard.ViewModels.Visuals;
using System.Text.Json;

namespace CloudLightBlizzard.ViewModels;

public sealed partial class DropsViewModel
{
    private readonly Dictionary<DropsPlatform, PlatformOverviewModel> _visuals = [];
    public SoopCampaignModel SoopCampaigns { get; } = new();
    private TwitchInventoryModel? _twitchInventory;
    public TwitchInventoryModel TwitchInventory
    {
        get
        {
            if(_twitchInventory is null){_twitchInventory=new(Twitch,TwitchPriorityGames);_twitchInventory.PropertyChanged+=(_,_)=>TwitchVisual.UpdateRuntimeSummary(_twitchInventory.WatchState);TwitchVisual.UpdateRuntimeSummary(_twitchInventory.WatchState);}
            return _twitchInventory;
        }
    }
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
    private void ApplyVisualState(DropsPlatform platform, JsonElement state)
    {
        VisualFor(platform).Apply(state);
        if(platform==DropsPlatform.Twitch)TwitchInventory.Apply(state);
        if(platform==DropsPlatform.Soop)SoopCampaigns.Apply(state);
    }
}
