using System.Text.Json;

namespace CloudLightBlizzard.Services.Drops;

internal static class SoopTaskPolicy
{
    public static bool IsSelectable(JsonElement task)
    {
        if (Bool(task, "eventOnly")) return false;
        var lifecycle = Lifecycle(task);
        return lifecycle is "active" or "notYetOpen";
    }

    public static string Status(JsonElement task)
    {
        return Lifecycle(task) switch
        {
            "completed" => "已完成",
            "ended" => "已结束",
            "active" => "进行中",
            "notYetOpen" => "未开始",
            _ => "状态未知",
        };
    }

    private static string Lifecycle(JsonElement task)
    {
        // Completion always wins, even while the event time window is open.
        if (Bool(task, "completed")) return "completed";
        if (task.TryGetProperty("lifecycle", out var value) && value.ValueKind == JsonValueKind.String)
        {
            var lifecycle = value.GetString();
            if (lifecycle is "completed" or "ended" or "active" or "notYetOpen") return lifecycle;
            if (lifecycle == "upcoming") return "notYetOpen"; // compatibility with pre-normalization Workers
        }
        // Compatibility fallback for an older Worker. Keep the canonical
        // precedence rather than trusting an arbitrary flag order.
        if (Bool(task, "ended")) return "ended";
        if (Bool(task, "active")) return "active";
        if (Bool(task, "notYetOpen")) return "notYetOpen";
        return "unknown";
    }

    private static bool Bool(JsonElement owner, string property) =>
        owner.TryGetProperty(property, out var value) &&
        value.ValueKind == JsonValueKind.True && value.GetBoolean();
}
