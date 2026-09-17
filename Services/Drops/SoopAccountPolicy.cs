using System.Text.Json;

namespace CloudLightBlizzard.Services.Drops;

internal static class SoopAccountPolicy
{
    public static bool RequiresMultiAccountConfirmation(int existingAccountCount) =>
        existingAccountCount >= 1;

    public static bool IsUnlinkedGameReward(JsonElement reward) =>
        Requirement(reward) == true && IsExplicitFalse(reward, "gameAccountLinked");

    public static string GameAccountBindingText(JsonElement item)
    {
        var requirement = Requirement(item);
        if (requirement == false) return "游戏账号：不支持绑定";
        if (requirement is null) return "游戏账号：状态待确认";
        if (Bool(item, "gameAccountLinked")) return "游戏账号：已绑定";
        if (IsExplicitFalse(item, "gameAccountLinked")) return "游戏账号：未绑定";
        return "游戏账号：待刷新";
    }

    public static string GameAccountClaimRequirementText(JsonElement item) => Requirement(item) switch
    {
        true => "领取条件：需要游戏账号绑定",
        false => "领取条件：无需游戏账号绑定",
        _ => "领取条件：待确认",
    };

    private static bool? Requirement(JsonElement item)
    {
        if (item.TryGetProperty("gameAccountRequirement", out var state) &&
            state.ValueKind == JsonValueKind.String)
        {
            return state.GetString() switch
            {
                "required" => true,
                "not_supported" => false,
                _ => null,
            };
        }
        // Compatibility with old snapshots: true was authoritative, while
        // false also represented a missing SOOP field and must stay unknown.
        return Bool(item, "requiresGameAccount") ? true : null;
    }

    private static bool Bool(JsonElement owner, string property) =>
        owner.TryGetProperty(property, out var value) &&
        value.ValueKind == JsonValueKind.True;

    private static bool IsExplicitFalse(JsonElement owner, string property) =>
        owner.TryGetProperty(property, out var value) &&
        value.ValueKind == JsonValueKind.False;
}
