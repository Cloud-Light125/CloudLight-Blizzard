using System.Text.Json;

namespace CloudLightBlizzard.Services.Drops;

internal static class SoopTaskPolicy
{
    public static bool IsSelectable(JsonElement task) =>
        !Bool(task, "eventOnly") &&
        !Bool(task, "ended") &&
        !Bool(task, "completed") &&
        (Bool(task, "active") || Bool(task, "notYetOpen"));

    public static string Status(JsonElement task)
    {
        if (Bool(task, "completed")) return "已完成";
        if (Bool(task, "active")) return "进行中";
        if (Bool(task, "notYetOpen")) return "未开始";
        if (Bool(task, "ended")) return "已结束";
        return "状态未知";
    }

    private static bool Bool(JsonElement owner, string property) =>
        owner.TryGetProperty(property, out var value) &&
        value.ValueKind == JsonValueKind.True && value.GetBoolean();
}
