using System.Text.Json;

namespace CloudLightBlizzard.Services.Drops;

internal static class SoopAccountPolicy
{
    public static bool RequiresMultiAccountConfirmation(int existingAccountCount) =>
        existingAccountCount >= 1;

    public static bool IsUnlinkedGameReward(JsonElement reward) =>
        Bool(reward, "requiresGameAccount") && IsExplicitFalse(reward, "gameAccountLinked");

    private static bool Bool(JsonElement owner, string property) =>
        owner.TryGetProperty(property, out var value) &&
        value.ValueKind == JsonValueKind.True;

    private static bool IsExplicitFalse(JsonElement owner, string property) =>
        owner.TryGetProperty(property, out var value) &&
        value.ValueKind == JsonValueKind.False;
}
