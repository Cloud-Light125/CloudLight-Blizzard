using System.Windows;
using CloudLightBlizzard.Models;
using CloudLightBlizzard.Services;
using CloudLightBlizzard.Services.Drops;
using CloudLightBlizzard.Services.OverwatchRegion;

namespace CloudLightBlizzard.ViewModels;

public sealed class OverviewViewModel : ObservableObject
{
    private static readonly TimeSpan StatusTtl = TimeSpan.FromMinutes(30);
    private readonly MainViewModel _main;
    private readonly AnnouncementService? _announcements;
    private string _overallText = "正在读取状态…";
    private string _overallDetail = "";
    private string _regionText = "无法确认";
    private string _battleNetText = "尚未检测";
    private string _regionActionText = "打开区服切换";
    private string _soopText = "未初始化";
    private string _twitchText = "未初始化";
    private string _dropsProgressText = "当前进度：暂无数据";
    private string _proxyText = "未检查";
    private string _announcementText = "未检查";
    private string _networkUpdateText = "未检查";
    private string _currentVersionText = "";
    private string _latestVersionText = "尚未检查";
    private string _snapshotText = "尚未准备";
    private string _snapshotTimeText = "";
    private string _activityText = "暂无最近活动";

    public OverviewViewModel(MainViewModel main, AnnouncementService? announcements = null)
    {
        _main = main;
        _announcements = announcements;
    }
    public string OverallText { get => _overallText; private set => Set(ref _overallText, value); }
    public string OverallDetail { get => _overallDetail; private set => Set(ref _overallDetail, value); }
    public string RegionText { get => _regionText; private set => Set(ref _regionText, value); }
    public string BattleNetText { get => _battleNetText; private set => Set(ref _battleNetText, value); }
    public string RegionActionText { get => _regionActionText; private set => Set(ref _regionActionText, value); }
    public string SoopText { get => _soopText; private set => Set(ref _soopText, value); }
    public string TwitchText { get => _twitchText; private set => Set(ref _twitchText, value); }
    public string DropsProgressText { get => _dropsProgressText; private set => Set(ref _dropsProgressText, value); }
    public string ProxyText { get => _proxyText; private set => Set(ref _proxyText, value); }
    public string AnnouncementText { get => _announcementText; private set => Set(ref _announcementText, value); }
    public string NetworkUpdateText { get => _networkUpdateText; private set => Set(ref _networkUpdateText, value); }
    public string CurrentVersionText { get => _currentVersionText; private set => Set(ref _currentVersionText, value); }
    public string LatestVersionText { get => _latestVersionText; private set => Set(ref _latestVersionText, value); }
    public string SnapshotText { get => _snapshotText; private set => Set(ref _snapshotText, value); }
    public string SnapshotTimeText { get => _snapshotTimeText; private set => Set(ref _snapshotTimeText, value); }
    public string ActivityText { get => _activityText; private set => Set(ref _activityText, value); }

    public Task RefreshAsync()
    {
        var status = _main.RegionStatusSnapshot;
        RegionText = FormatStatus(MainViewModel.RegionDisplayName(status.CurrentRegion),
            _main.RegionStatusLastCheckedAt);
        BattleNetText = _main.BattleNetPathValid ? "可启动" : "路径未识别";
        RegionActionText = status.CurrentRegion == CurrentGameRegion.China ? "切换到国际服" : "打开区服切换";

        var drops = _main.GetDropsDiagnosticSnapshot();
        SoopText = FormatDropsStatus(drops.SoopStatus, drops.Platforms, "SOOP");
        TwitchText = FormatDropsStatus(drops.TwitchStatus, drops.Platforms, "Twitch");
        DropsProgressText = drops.RecentNetworkError == "无" ? "当前进度：由 Drops 页面显示" : $"最近网络事件：{drops.RecentNetworkError}";

        ProxyText = _main.Settings.EnableProxy ? "已启用" : "直连";
        AnnouncementText = FormatAnnouncementStatus();
        NetworkUpdateText = FormatUpdateStatus();
        CurrentVersionText = _main.UpdateChecks.CurrentVersion;
        LatestVersionText = FormatLatestVersion();
        SnapshotText = status.State switch
        {
            RegionBackupState.Ready => status.BackupMode == RegionBackupMode.VerifiedDifference ? "VerifiedDifference：正常" : "FullSnapshot：正常",
            RegionBackupState.Stale => "快照：过期",
            RegionBackupState.Empty => "尚未准备快照",
            _ => $"快照：{status.State}",
        };
        SnapshotTimeText = status.State is RegionBackupState.Ready or RegionBackupState.Stale
            ? $"文件 {status.DifferenceCount:N0} 个 · {UpdateDownloadService.FormatBytes(status.BackupBytes)}"
            : "前往区服切换准备文件";
        ActivityText = _main.StatusText is { Length: > 0 } ? _main.StatusText : "暂无最近活动";
        var attention = status.CurrentRegion is CurrentGameRegion.Mixed or CurrentGameRegion.Unknown ||
                        status.State is RegionBackupState.Error or RegionBackupState.Empty ||
                        IsStale(_main.RegionStatusLastCheckedAt) ||
                        IsUnknownOrStale(_announcements?.LastCheckAt) ||
                        IsUnknownOrStale(_main.UpdateChecks.LastCheckAt ?? _main.Settings.LastUpdateCheckAt);
        OverallText = attention ? "需要注意" : "一切正常";
        OverallDetail = attention ? "部分状态尚未检查或可能已过期，请按需打开诊断中心复核。" :
            "核心区服文件状态可用，网络与 Drops 状态显示最近一次检查时间。";
        return Task.CompletedTask;
    }

    private string FormatAnnouncementStatus()
    {
        var checkedAt = _announcements?.LastCheckAt;
        if (checkedAt is null) return "未检查";
        var state = string.IsNullOrWhiteSpace(_announcements?.LastFailureMessage) ? "正常" :
            $"检查失败 · {_announcements.LastFailureMessage}";
        return FormatStatus(state, checkedAt);
    }

    private string FormatUpdateStatus()
    {
        var checkedAt = _main.UpdateChecks.LastCheckAt ?? _main.Settings.LastUpdateCheckAt;
        if (checkedAt is null) return "未检查";
        var result = _main.UpdateChecks.LastResult;
        var state = result?.Status == UpdateCheckResultStatus.Failed
            ? $"检查失败 · {result.ErrorMessage ?? "更新服务暂时不可用"}"
            : result is null ? "未载入结果" : "正常";
        return FormatStatus(state, checkedAt);
    }

    private string FormatLatestVersion()
    {
        var checkedAt = _main.UpdateChecks.LastCheckAt ?? _main.Settings.LastUpdateCheckAt;
        var result = _main.UpdateChecks.LastResult;
        if (checkedAt is null || result is null) return "未检查";
        var state = result.Status == UpdateCheckResultStatus.Success
            ? result.HasUpdate ? $"有新版本 {result.LatestVersion}" : result.LatestVersion
            : $"检查失败 · {result.ErrorMessage ?? "更新服务暂时不可用"}";
        return FormatStatus(state, checkedAt);
    }

    private static string FormatDropsStatus(string status,
        IReadOnlyList<DropsPlatformRecoveryDiagnostic> platforms, string platform)
    {
        var checkedAt = platforms.FirstOrDefault(item =>
            string.Equals(item.Platform, platform, StringComparison.OrdinalIgnoreCase))?.LastHeartbeatAt;
        return checkedAt is null ? "未检查" : FormatStatus(status, checkedAt);
    }

    internal static string FormatStatus(string status, DateTimeOffset? checkedAt,
        DateTimeOffset? now = null)
    {
        if (checkedAt is null) return "未检查";
        var stale = IsStale(checkedAt, now);
        var text = $"{status} · {checkedAt.Value.ToLocalTime():HH:mm} 检查";
        return stale ? text + " · 状态可能已过期" : text;
    }

    private static bool IsUnknownOrStale(DateTimeOffset? checkedAt) =>
        checkedAt is null || IsStale(checkedAt);

    private static bool IsStale(DateTimeOffset? checkedAt, DateTimeOffset? now = null) =>
        checkedAt is null || (now ?? DateTimeOffset.Now) - checkedAt.Value > StatusTtl;
}
