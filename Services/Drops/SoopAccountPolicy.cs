using System.Text.Json;

namespace CloudLightBlizzard.Services.Drops;

internal static class SoopAccountPolicy
{
    public static bool RequiresMultiAccountConfirmation(int existingAccountCount) =>
        existingAccountCount >= 1;

    public static bool IsUnlinkedGameReward(JsonElement reward) =>
        Bool(reward, "requiresGameAccount") && IsExplicitFalse(reward, "gameAccountLinked");

    public static string GameAccountBindingText(JsonElement item)
    {
        if (!Bool(item, "requiresGameAccount")) return "游戏账号：无需绑定";
        if (Bool(item, "gameAccountLinked")) return "游戏账号：✓ 已绑定";
        if (IsExplicitFalse(item, "gameAccountLinked")) return "游戏账号：⚠ 未绑定";
        return "游戏账号：待刷新";
    }

    private static bool Bool(JsonElement owner, string property) =>
        owner.TryGetProperty(property, out var value) &&
        value.ValueKind == JsonValueKind.True;

    private static bool IsExplicitFalse(JsonElement owner, string property) =>
        owner.TryGetProperty(property, out var value) &&
        value.ValueKind == JsonValueKind.False;
}
