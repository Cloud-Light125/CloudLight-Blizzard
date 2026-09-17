using System.Text.Json;

namespace CloudLightBlizzard.Services.Drops;

internal static class SoopAccountPolicy
{
    internal enum GameAccountRequirement
    {
        Unsupported,
        Required,
        Unknown,
    }

    internal enum GameAccountLinkState
    {
        Linked,
        Unlinked,
        Unknown,
    }

    public static bool RequiresMultiAccountConfirmation(int existingAccountCount) =>
        existingAccountCount >= 1;

    public static bool IsUnlinkedGameReward(JsonElement reward) =>
        Requirement(reward) == GameAccountRequirement.Required &&
        LinkState(reward) == GameAccountLinkState.Unlinked;

    public static bool CanClaimInApp(JsonElement reward) =>
        Requirement(reward) == GameAccountRequirement.Unsupported ||
        Requirement(reward) == GameAccountRequirement.Required &&
        LinkState(reward) == GameAccountLinkState.Linked;

    public static bool RequiresOfficialInventory(JsonElement reward) =>
        !CanClaimInApp(reward);

    public static string GameAccountBindingText(JsonElement item)
    {
        var requirement = Requirement(item);
        if (requirement == GameAccountRequirement.Unsupported) return "游戏账号：不支持绑定";
        if (requirement == GameAccountRequirement.Unknown) return "游戏账号：无法确认";
        return LinkState(item) switch
        {
            GameAccountLinkState.Linked => "游戏账号：已绑定",
            GameAccountLinkState.Unlinked => "游戏账号：未绑定",
            _ => "游戏账号：无法确认",
        };
    }

    public static string GameAccountClaimRequirementText(JsonElement item) => Requirement(item) switch
    {
        GameAccountRequirement.Required => "领取条件：需要游戏账号绑定",
        GameAccountRequirement.Unsupported => "领取条件：无需游戏账号绑定",
        _ => "领取条件：待确认",
    };

    private static GameAccountRequirement Requirement(JsonElement item)
    {
        if (item.ValueKind != JsonValueKind.Object)
            return GameAccountRequirement.Unknown;
        if (item.TryGetProperty("gameAccountRequirement", out var state) &&
            state.ValueKind == JsonValueKind.String)
        {
            return state.GetString() switch
            {
                "required" => GameAccountRequirement.Required,
                "unsupported" or "not_supported" => GameAccountRequirement.Unsupported,
                _ => GameAccountRequirement.Unknown,
            };
        }
        // Compatibility with old snapshots: true was authoritative, while
        // false also represented a missing SOOP field and must stay unknown.
        return Bool(item, "requiresGameAccount")
            ? GameAccountRequirement.Required
            : GameAccountRequirement.Unknown;
    }

    private static GameAccountLinkState LinkState(JsonElement item)
    {
        if (item.ValueKind != JsonValueKind.Object)
            return GameAccountLinkState.Unknown;
        if (item.TryGetProperty("gameAccountLinkState", out var state) &&
            state.ValueKind == JsonValueKind.String)
        {
            return state.GetString() switch
            {
                "linked" => GameAccountLinkState.Linked,
                "unlinked" => GameAccountLinkState.Unlinked,
                _ => GameAccountLinkState.Unknown,
            };
        }
        if (Requirement(item) != GameAccountRequirement.Required)
            return GameAccountLinkState.Unknown;
        if (Bool(item, "gameAccountLinked")) return GameAccountLinkState.Linked;
        if (IsExplicitFalse(item, "gameAccountLinked")) return GameAccountLinkState.Unlinked;
        return GameAccountLinkState.Unknown;
    }

    private static bool Bool(JsonElement owner, string property) =>
        owner.TryGetProperty(property, out var value) &&
        value.ValueKind == JsonValueKind.True;

    private static bool IsExplicitFalse(JsonElement owner, string property) =>
        owner.TryGetProperty(property, out var value) &&
        value.ValueKind == JsonValueKind.False;
}
