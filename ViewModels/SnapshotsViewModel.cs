using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Windows;
using CloudLightBlizzard.Services;
using CloudLightBlizzard.Services.OverwatchRegion;

namespace CloudLightBlizzard.ViewModels;

public sealed class SnapshotItemViewModel : ObservableObject
{
    private SnapshotDescriptor _descriptor;

    public SnapshotItemViewModel(SnapshotDescriptor descriptor) => _descriptor = descriptor;
    public string GenerationId => _descriptor.GenerationId;
    public string ModeText => _descriptor.Mode == RegionBackupMode.VerifiedDifference ? "VerifiedDifference" : "FullSnapshot";
    public string SourceText => MainViewModel.RegionDisplayName(_descriptor.SourceRegion);
    public string TargetText => MainViewModel.RegionDisplayName(_descriptor.TargetRegion);
    public string CreatedText => _descriptor.CreatedAtUtc.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss");
    public string UsedText => _descriptor.LastUsedAtUtc is { } value ? value.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss") : "未使用";
    public string FileCountText => $"{_descriptor.FileCount:N0} 个文件";
    public string SizeText => UpdateDownloadService.FormatBytes(_descriptor.TotalBytes);
    public SnapshotDisplayState State => _descriptor.State;
    public string StateText => _descriptor.State switch
    {
        SnapshotDisplayState.Normal => "正常",
        SnapshotDisplayState.Corrupt => "损坏",
        SnapshotDisplayState.Expired => "过期",
        SnapshotDisplayState.Missing => "缺失",
        _ => "未知",
    };
    public string StateReason => _descriptor.StateReason;
    public Visibility StateReasonVisibility => string.IsNullOrWhiteSpace(StateReason)
        ? Visibility.Collapsed : Visibility.Visible;
    public bool IsActive => _descriptor.IsActive;
    public Visibility ActiveVisibility => IsActive ? Visibility.Visible : Visibility.Collapsed;
    public string RootPath => _descriptor.RootPath;
    public void Update(SnapshotDescriptor descriptor)
    {
        _descriptor = descriptor;
        Raise(nameof(ModeText)); Raise(nameof(SourceText)); Raise(nameof(TargetText)); Raise(nameof(CreatedText));
        Raise(nameof(UsedText)); Raise(nameof(FileCountText)); Raise(nameof(SizeText));
        Raise(nameof(State)); Raise(nameof(StateText)); Raise(nameof(StateReason)); Raise(nameof(StateReasonVisibility));
        Raise(nameof(IsActive)); Raise(nameof(ActiveVisibility)); Raise(nameof(RootPath));
    }
}

public sealed class SnapshotsViewModel : ObservableObject
{
    private readonly MainViewModel _main;
    private SnapshotManagerService? _service;
    private string _statusText = "正在读取 CloudLight Blizzard 管理的区服快照。";
    private SnapshotItemViewModel? _selectedSnapshot;
    private int _snapshotCount;
    private long _snapshotBytes;
    public string SnapshotBytesText => $"快照文件合计 {UpdateDownloadService.FormatBytes(_snapshotBytes)}";

    public SnapshotsViewModel(MainViewModel main)
    {
        _main = main ?? throw new ArgumentNullException(nameof(main));
    }

    private SnapshotManagerService Service
    {
        get
        {
            if (_service is null || !string.Equals(_service.BackupRoot, _main.RegionBackupRoot,
                    StringComparison.OrdinalIgnoreCase))
                _service = new SnapshotManagerService(_main.RegionManager);
            return _service;
        }
    }

    public ObservableCollection<SnapshotItemViewModel> Items { get; } = new();
    public string StatusText { get => _statusText; private set => Set(ref _statusText, value); }
    public SnapshotItemViewModel? SelectedSnapshot
    {
        get => _selectedSnapshot;
        set
        {
            if (ReferenceEquals(_selectedSnapshot, value)) return;
            Set(ref _selectedSnapshot, value);
            Raise(nameof(HasSelectedSnapshot));
        }
    }
    public bool HasSelectedSnapshot => SelectedSnapshot is not null;
    public int SnapshotCount { get => _snapshotCount; private set => Set(ref _snapshotCount, value); }

    public sealed record RetentionOption(int Count, string Label)
    {
        public override string ToString() => Label;
    }
    public IReadOnlyList<RetentionOption> RetentionOptions { get; } =
        [new(0, "0 份"), new(1, "1 份"), new(2, "2 份"), new(3, "3 份"), new(5, "5 份"), new(-1, "不限制")];

    public bool AutoCleanup
    {
        get => _main.Settings.RegionSnapshotAutoCleanup;
        set => SavePolicy(value, HistoryLimit);
    }
    public int HistoryLimit
    {
        get => SnapshotCleanupPolicy.NormalizeLimit(_main.Settings.RegionSnapshotHistoryLimit);
        set => SavePolicy(AutoCleanup, value);
    }

    private SnapshotCleanupPlan? _cleanupPlan;
    private bool _cleanupBusy;
    public bool CanScan => !_cleanupBusy;
    public bool CanClean => CanScan && _cleanupPlan is { BlockedReason: null } && _cleanupPlan.ToDelete.Any();
    public IReadOnlyList<SnapshotCleanupEntry> CleanupEntries => _cleanupPlan?.Entries ?? [];
    public string CleanupSummary => _cleanupPlan is null ? "" :
        $"共 {_cleanupPlan.Entries.Count} 份 · 受保护 {_cleanupPlan.ProtectedCount} 份 · 可清理 {_cleanupPlan.ToDelete.Count()} 份 · " +
        $"预计释放 {UpdateDownloadService.FormatBytes(_cleanupPlan.ReclaimableBytes)}" +
        (_cleanupPlan.BlockedReason is { } reason ? $"\n{reason}" : "");
    public string CleanupConfirmation => CleanupSummary + "\n\n" +
        string.Join("\n", (_cleanupPlan?.ToDelete ?? []).Select(item => item.GenerationId));

    private void SavePolicy(bool enabled, int limit)
    {
        var settings = _main.Settings;
        var previousEnabled = settings.RegionSnapshotAutoCleanup;
        var previousLimit = settings.RegionSnapshotHistoryLimit;
        if (previousEnabled == enabled && previousLimit == limit) return;
        settings.RegionSnapshotAutoCleanup = enabled;
        settings.RegionSnapshotHistoryLimit = SnapshotCleanupPolicy.NormalizeLimit(limit);
        if (!settings.Save())
        {
            settings.RegionSnapshotAutoCleanup = previousEnabled;
            settings.RegionSnapshotHistoryLimit = previousLimit;
            StatusText = "无法保存自动清理设置，已保留此前设置。";
        }
        else
            StatusText = "快照保留设置已保存。";
        _cleanupPlan = null;
        Raise(nameof(AutoCleanup)); Raise(nameof(HistoryLimit));
        NotifyCleanup();
    }

    public async Task ScanCleanupAsync()
    {
        if (!CanScan) return;
        var service = Service;
        var limit = HistoryLimit;
        _cleanupBusy = true;
        _cleanupPlan = null;
        NotifyCleanup();
        try
        {
            _cleanupPlan = await Task.Run(() => service.ScanCleanup(limit));
            StatusText = "旧快照扫描完成。";
        }
        catch (Exception ex) { StatusText = $"无法扫描快照：{ex.Message}"; }
        finally { _cleanupBusy = false; NotifyCleanup(); }
    }

    public async Task CleanSnapshotsAsync()
    {
        if (!CanClean || _cleanupPlan is not { } preview) return;
        var service = Service;
        var limit = HistoryLimit;
        _cleanupBusy = true;
        NotifyCleanup();
        try
        {
            var result = await Task.Run(() => service.Clean(preview, limit));
            await RefreshAsync();
            StatusText = $"已删除 {result.Removed} 份快照，回收 {UpdateDownloadService.FormatBytes(result.Bytes)}。" +
                (result.Failures.Count > 0 ? "\n无法删除的快照\n" + string.Join("\n", result.Failures) : "");
        }
        catch (Exception ex) { StatusText = $"无法清理快照：{ex.Message}"; }
        finally { _cleanupPlan = null; _cleanupBusy = false; NotifyCleanup(); }
    }

    private void NotifyCleanup()
    {
        Raise(nameof(CanScan)); Raise(nameof(CanClean)); Raise(nameof(CleanupEntries)); Raise(nameof(CleanupSummary));
    }

    public Task RefreshAsync()
    {
        var selectedId = SelectedSnapshot?.GenerationId;
        IReadOnlyList<SnapshotDescriptor> descriptors;
        try { descriptors = Service.List(); }
        catch (Exception ex)
        {
            StatusText = $"无法刷新快照列表：{ex.Message}";
            return Task.CompletedTask;
        }
        Items.Clear();
        foreach (var descriptor in descriptors) Items.Add(new SnapshotItemViewModel(descriptor));
        SnapshotCount = Items.Count;
        _snapshotBytes = descriptors.Sum(item => item.TotalBytes);
        Raise(nameof(SnapshotBytesText));
        SelectedSnapshot = Items.FirstOrDefault(item => string.Equals(item.GenerationId, selectedId,
            StringComparison.OrdinalIgnoreCase)) ?? Items.FirstOrDefault();
        StatusText = Items.Count == 0 ? "尚未生成区服快照。请先在“区服切换”完成一次国服与国际服文件准备。" : "快照列表已更新。";
        return Task.CompletedTask;
    }

    public void ShowDetails(SnapshotItemViewModel item)
    {
        SelectedSnapshot = item;
        StatusText = $"正在查看 {item.ModeText} 快照详情。";
    }

    public void ClearDetails() => SelectedSnapshot = null;

    public bool Delete(SnapshotItemViewModel item)
    {
        try
        {
            var deleted = Service.Delete(item.GenerationId);
            // Re-read disk after deletion rather than treating removal of a UI row as success.
            RefreshAsync().GetAwaiter().GetResult();
            StatusText = deleted ? "快照已删除。" : "快照不存在或已经删除。";
            return deleted;
        }
        catch (Exception ex)
        {
            StatusText = $"无法删除该快照，已阻止操作：{ex.Message}";
            return false;
        }
    }

    public void Regenerate()
    {
        _main.RequestRegionReprepare();
        StatusText = "已返回区服切换流程，请重新准备区服文件以生成新快照。";
    }

    public void OpenDirectory(SnapshotItemViewModel item)
    {
        try { Process.Start(new ProcessStartInfo { FileName = item.RootPath, UseShellExecute = true }); }
        catch { StatusText = "无法打开快照目录。"; }
    }

}
