using CloudLightBlizzard.Services.Diagnostics;
namespace CloudLightBlizzard.ViewModels;

public sealed record DiagnosticModuleVisual(string Name, string State, string Summary);
public sealed partial class DiagnosticsViewModel
{
    public string HealthyCountText => Checks.Count == 0 ? "—" : Checks.Count(c => c.Status == DiagnosticSeverity.Healthy).ToString();
    public string WarningCountText => Checks.Count == 0 ? "—" : Checks.Count(c => c.Status == DiagnosticSeverity.Warning).ToString();
    public string ErrorCountText => Checks.Count == 0 ? "—" : Checks.Count(c => c.Status == DiagnosticSeverity.Error).ToString();
    public IReadOnlyList<DiagnosticModuleVisual> Modules => new[]{
        ("应用与环境",new[]{"环境","应用"}), ("网络与代理",new[]{"网络","代理"}), ("掉宝后台",new[]{"Drops","Worker"}),
        ("账号与备份",new[]{"账号","Battle"}), ("区服与快照",new[]{"区服","快照"}), ("更新与通知",new[]{"更新","通知"}),
    }.Select(group =>
    {
        var checks = Checks.Where(c => group.Item2.Any(tag => c.Category.Contains(tag, StringComparison.OrdinalIgnoreCase))).ToArray();
        var errors = checks.Count(c => c.Status == DiagnosticSeverity.Error); var warnings = checks.Count(c => c.Status == DiagnosticSeverity.Warning);
        return new DiagnosticModuleVisual(group.Item1, checks.Length == 0 ? "待检查" : errors > 0 ? "需要处理" : warnings > 0 ? "存在警告" : IsRunning || Report is null ? "检查中" : "已通过",
            checks.Length == 0 ? "尚无本轮检查结果" : $"已返回 {checks.Length} 项 · 警告 {warnings} · 错误 {errors}");
    }).ToArray();
    private void RaiseVisualCounts() { foreach (var p in new[] { nameof(HealthyCountText), nameof(WarningCountText), nameof(ErrorCountText), nameof(Modules) }) Raise(p); }
}
