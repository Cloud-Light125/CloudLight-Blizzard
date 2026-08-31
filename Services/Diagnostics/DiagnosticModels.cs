using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace CloudLightBlizzard.Services.Diagnostics;

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum DiagnosticSeverity
{
    Healthy,
    Warning,
    Error,
    Info,
}

public sealed class DiagnosticCheck
{
    public string Id { get; init; } = "";
    public string Category { get; init; } = "";
    public string Name { get; init; } = "";
    public DiagnosticSeverity Status { get; init; } = DiagnosticSeverity.Info;
    public string Summary { get; init; } = "";
    public string Details { get; init; } = "";
    public long DurationMilliseconds { get; set; }
    public DateTimeOffset Timestamp { get; init; } = DateTimeOffset.Now;

    [JsonIgnore]
    public string StatusIcon => Status switch
    {
        DiagnosticSeverity.Healthy => "✓",
        DiagnosticSeverity.Warning => "!",
        DiagnosticSeverity.Error => "×",
        _ => "…",
    };

    [JsonIgnore]
    public string StatusText => Status switch
    {
        DiagnosticSeverity.Healthy => "正常",
        DiagnosticSeverity.Warning => "警告",
        DiagnosticSeverity.Error => "错误",
        _ => "信息",
    };
}

public sealed record DiagnosticProgress(int Completed, int Total, DiagnosticCheck? Current,
    bool IsCompleted = false);

public sealed class DiagnosticRunReport
{
    public string AppVersion { get; init; } = "";
    public DateTimeOffset StartedAt { get; init; }
    public DateTimeOffset CompletedAt { get; init; }
    public bool Cancelled { get; init; }
    public List<DiagnosticCheck> Checks { get; init; } = new();

    [JsonIgnore]
    public int HealthyCount => Checks.Count(item => item.Status == DiagnosticSeverity.Healthy);
    [JsonIgnore]
    public int WarningCount => Checks.Count(item => item.Status == DiagnosticSeverity.Warning);
    [JsonIgnore]
    public int ErrorCount => Checks.Count(item => item.Status == DiagnosticSeverity.Error);
    [JsonIgnore]
    public string OverallText => ErrorCount > 0 ? "需要处理错误" : WarningCount > 0 ? "需要注意" : "一切正常";

    public string ToDisplayText()
    {
        var builder = new StringBuilder();
        builder.AppendLine($"CloudLight Blizzard {AppVersion}");
        builder.AppendLine($"诊断时间：{StartedAt.ToLocalTime():yyyy-MM-dd HH:mm:ss} - {CompletedAt.ToLocalTime():HH:mm:ss}");
        builder.AppendLine($"总体状态：{OverallText}");
        builder.AppendLine();
        foreach (var check in Checks)
        {
            builder.AppendLine($"{check.StatusIcon} [{check.Category}] {check.Name}：{check.Summary}");
            if (!string.IsNullOrWhiteSpace(check.Details)) builder.AppendLine($"  {check.Details}");
        }
        if (Cancelled) builder.AppendLine("诊断已取消，以上为已完成项目。");
        return DiagnosticSanitizer.Sanitize(builder.ToString());
    }

    public string ToJson(JsonSerializerOptions? options = null) =>
        DiagnosticSanitizer.Sanitize(JsonSerializer.Serialize(this, options ?? DiagnosticJson.Options));
}

public static class DiagnosticJson
{
    public static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter() },
    };
}
