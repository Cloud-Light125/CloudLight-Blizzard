using System.Windows;
using System.Text.Json;
using CloudLightBlizzard.ViewModels.Visuals;

namespace CloudLightBlizzard.ViewModels;

public sealed partial class DropsViewModel
{
    private DateTimeOffset? _twitchDeviceExpiry;
    public Visibility TwitchLoginCardVisibility => TwitchAuthorizationVisibility == Visibility.Visible ? Visibility.Collapsed : Visibility.Visible;
    public string TwitchAuthorizationRemainingText => _twitchDeviceExpiry is { } expiry
        ? DateTimeOffset.Now < expiry ? $"剩余有效时间 {(expiry-DateTimeOffset.Now):mm\\:ss}" : "授权码已过期，请重新获取。"
        : "有效期限以官方授权页面为准。";
    // Presentation only: a running process never substitutes for a verified identity.
    public Visibility TwitchAuthenticatedVisibility => IsTwitchLoggedIn ? Visibility.Visible : Visibility.Collapsed;
}

public sealed partial class DropsRow
{
    private CloudLightBlizzard.ViewModels.Visuals.RewardVisual? _visual;
    public CloudLightBlizzard.ViewModels.Visuals.RewardVisual Visual=>_visual??=CloudLightBlizzard.ViewModels.Visuals.RewardVisual.From(Payload,"Twitch","");
    public string CampaignImageUrl=>Payload.ValueKind==JsonValueKind.Object&&Payload.TryGetProperty("gameImageUrl",out var value)&&value.ValueKind==JsonValueKind.String?value.GetString()??"":"";
    public IReadOnlyList<RewardVisual> CampaignRewards=>V.Rows(Payload,"drops").Select(d=>RewardVisual.From(d,"Twitch","")).ToArray();
    public string SoopProgressSummary
    {
        get
        {
            if (Payload.ValueKind == JsonValueKind.Object && Payload.TryGetProperty("currentProgress", out var rows) && rows.ValueKind == JsonValueKind.Array)
            {
                var first = rows.EnumerateArray().FirstOrDefault();
                if (first.ValueKind == JsonValueKind.Object && first.TryGetProperty("percent", out var percent) && percent.ValueKind == JsonValueKind.Number)
                    return $"官方进度 {percent.GetDouble():0.#}%";
            }
            return "等待官方刷新";
        }
    }
    public string RewardProgressText
    {
        get
        {
            if (Payload.ValueKind != JsonValueKind.Object ||
                !Payload.TryGetProperty("requiredMinutes", out var required) || required.ValueKind != JsonValueKind.Number || !required.TryGetDouble(out var total) || total <= 0)
                return "官方所需时长尚未确认";
            if (!Payload.TryGetProperty("currentMinutes", out var value) || value.ValueKind != JsonValueKind.Number || !value.TryGetDouble(out var current))
                return $"官方进度待确认 · 所需 {total:0.#} 分钟";
            return $"官方进度 {current:0.#} / {total:0.#} 分钟 · 还需 {Math.Max(0, total-current):0.#} 分钟";
        }
    }
}
