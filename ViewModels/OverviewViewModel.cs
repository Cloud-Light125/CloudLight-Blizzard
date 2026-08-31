using System.Windows;
using CloudLightBlizzard.Services;
using CloudLightBlizzard.Services.Drops;
using CloudLightBlizzard.Services.OverwatchRegion;

namespace CloudLightBlizzard.ViewModels;

public sealed class OverviewViewModel : ObservableObject
{
    private readonly MainViewModel _main;
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

    public OverviewViewModel(MainViewModel main) => _main = main;
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

    public async Task RefreshAsync()
    {
        try { await _main.RefreshHomeRegionAsync(false); } catch { }
        var status = _main.RegionStatusSnapshot;
        RegionText = MainViewModel.RegionDisplayName(status.CurrentRegion);
        BattleNetText = _main.BattleNetPathValid ? "可启动" : "路径未识别";
        RegionActionText = status.CurrentRegion == CurrentGameRegion.China ? "切换到国际服" : "打开区服切换";

        var drops = _main.GetDropsDiagnosticSnapshot();
        SoopText = drops.SoopStatus;
        TwitchText = drops.TwitchStatus;
        DropsProgressText = drops.RecentNetworkError == "无" ? "当前进度：由 Drops 页面显示" : $"最近网络事件：{drops.RecentNetworkError}";

        ProxyText = _main.Settings.EnableProxy ? "已启用" : "直连";
        AnnouncementText = "点击诊断检查";
        NetworkUpdateText = "点击诊断检查";
        CurrentVersionText = _main.UpdateChecks.CurrentVersion;
        LatestVersionText = _main.UpdateChecks.LastResult?.LatestVersion ?? "尚未检查";
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
                        status.State is RegionBackupState.Error or RegionBackupState.Empty;
        OverallText = attention ? "需要注意" : "一切正常";
        OverallDetail = attention ? "区服或快照仍需要进一步检查。" : "核心区服文件状态可用，详细网络与 Drops 状态可在诊断中心查看。";
    }
}
