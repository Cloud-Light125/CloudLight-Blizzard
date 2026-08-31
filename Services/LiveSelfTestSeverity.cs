using System.Net.Http;
using System.Net.Sockets;
using CloudLightBlizzard.Models;
using CloudLightBlizzard.Services.Diagnostics;

namespace CloudLightBlizzard.Services;

public enum LiveSelfTestSeverity
{
    Pass,
    Warning,
    Fail,
}

public static class LiveSelfTestSeverityExtensions
{
    public static string DisplayText(this LiveSelfTestSeverity severity) => severity switch
    {
        LiveSelfTestSeverity.Pass => "PASS",
        LiveSelfTestSeverity.Warning => "WARNING",
        _ => "FAIL",
    };

    public static LiveSelfTestSeverity FromDiagnostic(this DiagnosticSeverity severity) => severity switch
    {
        DiagnosticSeverity.Healthy => LiveSelfTestSeverity.Pass,
        DiagnosticSeverity.Warning => LiveSelfTestSeverity.Warning,
        DiagnosticSeverity.Error => LiveSelfTestSeverity.Fail,
        _ => LiveSelfTestSeverity.Warning,
    };
}

public static class CloudServicesSeverity
{
    public static LiveSelfTestSeverity ClassifyNetwork(AppSettings settings,
        NetworkDiagnosticReport report, ICollection<string>? reasons = null)
    {
        var severity = LiveSelfTestSeverity.Pass;
        AddProbe(report.Proxy, "代理", isProxyProbe: true);
        AddProbe(report.Announcement, "公告服务");
        AddProbe(report.Update, "更新服务");
        if (report.Soop is not null) AddProbe(report.Soop, "SOOP");
        if (report.Twitch is not null) AddProbe(report.Twitch, "Twitch");
        if (report.Bilibili is not null) AddProbe(report.Bilibili, "哔哩哔哩");
        return severity;

        void AddProbe(CloudNetworkProbeResult probe, string name, bool isProxyProbe = false)
        {
            if (probe.Success) return;
            if (isProxyProbe && settings.EnableProxy)
            {
                Fail($"{name}：代理配置已启用，但代理探测失败（{probe.Message}）。");
                return;
            }
            if (IsDirectConnectivityWarning(settings, probe))
            {
                Warn($"{name}：直连目标暂时无法访问（{probe.Message}）。");
                return;
            }
            if (string.Equals(probe.Message, "GitHub API 请求频率限制", StringComparison.Ordinal))
            {
                Warn($"{name}：服务请求受限。");
                return;
            }
            Fail($"{name}：返回了非预期网络结果（{probe.Message}）。");
        }

        void Warn(string reason)
        {
            if (severity == LiveSelfTestSeverity.Pass) severity = LiveSelfTestSeverity.Warning;
            reasons?.Add(reason);
        }

        void Fail(string reason)
        {
            severity = LiveSelfTestSeverity.Fail;
            reasons?.Add(reason);
        }
    }

    public static LiveSelfTestSeverity ClassifyLiveResult(AppSettings settings,
        NetworkDiagnosticReport report, UpdateCheckResult updateResult, ICollection<string>? reasons = null)
    {
        var severity = ClassifyNetwork(settings, report, reasons);
        if (updateResult.Status == UpdateCheckResultStatus.Success)
        {
            var expectedUpdate = UpdateService.IsNewerVersion(updateResult.CurrentVersion,
                updateResult.LatestVersion);
            if (updateResult.HasUpdate != expectedUpdate)
                SetFail("更新服务返回的 HasUpdate 与版本比较结果不一致。");
        }
        else if (IsExpectedConnectivityFailure(settings, updateResult))
        {
            SetWarning("更新检查直连超时或暂时无法建立连接。");
        }
        else if (updateResult.FailureKind == UpdateFailureKind.RateLimited)
        {
            SetWarning("更新服务请求受限，请稍后重试。");
        }
        else
        {
            SetFail("更新服务返回失败或无法解析的结果：" +
                    (updateResult.ErrorMessage ?? updateResult.FailureKind.ToString()));
        }
        return severity;

        void SetWarning(string reason)
        {
            if (severity == LiveSelfTestSeverity.Pass) severity = LiveSelfTestSeverity.Warning;
            reasons?.Add(reason);
        }

        void SetFail(string reason)
        {
            severity = LiveSelfTestSeverity.Fail;
            reasons?.Add(reason);
        }
    }

    public static bool IsDirectConnectivityWarning(AppSettings settings, CloudNetworkProbeResult probe) =>
        !settings.EnableProxy && string.Equals(probe.Route, "Direct", StringComparison.OrdinalIgnoreCase) &&
        (probe.FailureKind == CloudNetworkFailureKind.DirectConnectionFailed ||
         string.Equals(probe.Message, "连接超时", StringComparison.Ordinal) ||
         string.Equals(probe.Message, "直连失败", StringComparison.Ordinal));

    private static bool IsExpectedConnectivityFailure(AppSettings settings, UpdateCheckResult result)
    {
        if (settings.EnableProxy) return false;
        if (result.FailureKind == UpdateFailureKind.Timeout) return true;
        if (result.FailureKind != UpdateFailureKind.NetworkUnavailable) return false;
        var detail = result.TechnicalDetail ?? "";
        return detail.Contains(nameof(CloudNetworkFailureKind.DirectConnectionFailed), StringComparison.Ordinal) ||
               detail.Contains(nameof(HttpRequestException), StringComparison.Ordinal) ||
               detail.Contains(nameof(TaskCanceledException), StringComparison.Ordinal) ||
               detail.Contains(nameof(SocketException), StringComparison.Ordinal);
    }
}
