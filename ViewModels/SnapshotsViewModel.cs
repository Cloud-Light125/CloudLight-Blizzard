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

    public Task RefreshAsync()
    {
        var selectedId = SelectedSnapshot?.GenerationId;
        var descriptors = Service.List();
        Items.Clear();
        foreach (var descriptor in descriptors) Items.Add(new SnapshotItemViewModel(descriptor));
        SnapshotCount = Items.Count;
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
            if (deleted)
            {
                Items.Remove(item);
                SnapshotCount = Items.Count;
                if (ReferenceEquals(SelectedSnapshot, item)) SelectedSnapshot = Items.FirstOrDefault();
            }
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
