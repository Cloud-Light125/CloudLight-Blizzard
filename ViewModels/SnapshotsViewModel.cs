using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Windows;
using CloudLightBlizzard.Services;
using CloudLightBlizzard.Services.OverwatchRegion;

namespace CloudLightBlizzard.ViewModels;

public sealed class SnapshotItemViewModel : ObservableObject
{
    private SnapshotDescriptor _descriptor;
    private bool _isVerifying;

    public SnapshotItemViewModel(SnapshotDescriptor descriptor) => _descriptor = descriptor;
    public string GenerationId => _descriptor.GenerationId;
    public string ModeText => _descriptor.Mode == RegionBackupMode.VerifiedDifference ? "VerifiedDifference" : "FullSnapshot";
    public string SourceText => MainViewModel.RegionDisplayName(_descriptor.SourceRegion);
    public string TargetText => MainViewModel.RegionDisplayName(_descriptor.TargetRegion);
    public string CreatedText => _descriptor.CreatedAtUtc.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss");
    public string VerifiedText => _descriptor.LastVerifiedAtUtc is { } value ? value.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss") : "尚未验证";
    public string UsedText => _descriptor.LastUsedAtUtc is { } value ? value.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss") : "未使用";
    public string FileCountText => $"{_descriptor.FileCount:N0} 个文件";
    public string SizeText => UpdateDownloadService.FormatBytes(_descriptor.TotalBytes);
    public int FileCount => _descriptor.FileCount;
    public SnapshotDisplayState State => _descriptor.State;
    public string StateText => _isVerifying ? "验证中" : _descriptor.State switch
    {
        SnapshotDisplayState.Normal => "正常",
        SnapshotDisplayState.Unverified => "未验证",
        SnapshotDisplayState.Verifying => "验证中",
        SnapshotDisplayState.Corrupt => "损坏",
        SnapshotDisplayState.Expired => "过期",
        SnapshotDisplayState.Missing => "缺失",
        _ => "未知",
    };
    public string StateReason => _descriptor.StateReason;
    public bool IsActive => _descriptor.IsActive;
    public string RootPath => _descriptor.RootPath;
    public bool IsVerifying { get => _isVerifying; set { Set(ref _isVerifying, value); Raise(nameof(StateText)); } }

    public void Update(SnapshotDescriptor descriptor)
    {
        _descriptor = descriptor;
        Raise(nameof(ModeText)); Raise(nameof(SourceText)); Raise(nameof(TargetText)); Raise(nameof(CreatedText));
        Raise(nameof(VerifiedText)); Raise(nameof(UsedText)); Raise(nameof(FileCountText)); Raise(nameof(SizeText));
        Raise(nameof(State)); Raise(nameof(StateText)); Raise(nameof(StateReason)); Raise(nameof(IsActive)); Raise(nameof(RootPath));
    }
}

public sealed class SnapshotsViewModel : ObservableObject, IDisposable
{
    private readonly MainViewModel _main;
    private SnapshotManagerService? _service;
    private CancellationTokenSource? _verificationCancellation;
    private string _statusText = "正在读取 CloudLight Blizzard 管理的区服快照。";
    private string _progressText = "";
    private bool _isBusy;
    private string _detailsText = "";
    private double _progressValue;
    private double _progressMaximum = 1;

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
    public string ProgressText { get => _progressText; private set => Set(ref _progressText, value); }
    public bool IsBusy { get => _isBusy; private set { Set(ref _isBusy, value); Raise(nameof(NotBusy)); } }
    public bool NotBusy => !IsBusy;
    public double ProgressValue { get => _progressValue; private set => Set(ref _progressValue, value); }
    public double ProgressMaximum { get => _progressMaximum; private set => Set(ref _progressMaximum, value); }
    public string DetailsText { get => _detailsText; private set { Set(ref _detailsText, value); Raise(nameof(HasDetails)); } }
    public bool HasDetails => !string.IsNullOrWhiteSpace(DetailsText);

    public Task RefreshAsync()
    {
        var descriptors = Service.List();
        Items.Clear();
        foreach (var descriptor in descriptors) Items.Add(new SnapshotItemViewModel(descriptor));
        StatusText = Items.Count == 0 ? "尚未生成区服快照。请先在“区服切换”完成一次国服与国际服文件准备。" : $"共 {Items.Count} 个快照 · 仅显示 CloudLight Blizzard 管理的路径。";
        return Task.CompletedTask;
    }

    public async Task VerifyAsync(SnapshotItemViewModel item)
    {
        if (IsBusy) return;
        var cts = new CancellationTokenSource();
        _verificationCancellation = cts;
        item.IsVerifying = true;
        IsBusy = true;
        ProgressValue = 0;
        ProgressMaximum = Math.Max(1, item.FileCount);
        var progress = new Progress<RegionProgress>(value =>
        {
            ProgressMaximum = Math.Max(1, value.Total);
            ProgressValue = Math.Clamp(value.Current, 0, ProgressMaximum);
            ProgressText = value.Total > 0 ? value.Message : "正在验证快照…";
        });
        try
        {
            var result = await Service.VerifyAsync(_main.Settings.OverwatchGamePath,
                item.GenerationId, progress, cts.Token);
            StatusText = result.Summary;
            await RefreshAsync();
        }
        catch (OperationCanceledException) when (cts.IsCancellationRequested)
        {
            StatusText = "快照验证已取消。";
        }
        catch (Exception ex)
        {
            StatusText = "无法验证快照，请检查游戏目录和快照状态。";
            ProgressText = ex.Message;
        }
        finally
        {
            item.IsVerifying = false;
            IsBusy = false;
            if (ReferenceEquals(_verificationCancellation, cts)) _verificationCancellation = null;
            cts.Dispose();
        }
    }

    public void ShowDetails(SnapshotItemViewModel item)
    {
        DetailsText = string.Join(Environment.NewLine,
            $"快照 ID：{item.GenerationId}",
            $"模式：{item.ModeText}",
            $"来源区服：{item.SourceText}",
            $"目标区服：{item.TargetText}",
            $"状态：{item.StateText}",
            $"状态说明：{item.StateReason}",
            $"创建时间：{item.CreatedText}",
            $"最后验证：{item.VerifiedText}",
            $"最后使用：{item.UsedText}",
            $"文件数量：{item.FileCountText}",
            $"总大小：{item.SizeText}",
            $"受管理目录：{item.RootPath}");
        StatusText = $"正在查看 {item.ModeText} 快照详情。";
    }

    public void ClearDetails() => DetailsText = "";

    public bool Delete(SnapshotItemViewModel item)
    {
        if (IsBusy) return false;
        try
        {
            var deleted = Service.Delete(item.GenerationId);
            if (deleted) Items.Remove(item);
            StatusText = deleted ? "快照已删除。" : "快照不存在或已经删除。";
            return deleted;
        }
        catch (Exception ex)
        {
            StatusText = "无法删除该快照，已阻止操作。";
            ProgressText = ex.Message;
            return false;
        }
    }

    public void Regenerate()
    {
        if (IsBusy) return;
        _main.RequestRegionReprepare();
        StatusText = "已返回区服切换流程，请重新准备区服文件以生成新快照。";
    }

    public void OpenDirectory(SnapshotItemViewModel item)
    {
        try { Process.Start(new ProcessStartInfo { FileName = item.RootPath, UseShellExecute = true }); }
        catch { StatusText = "无法打开快照目录。"; }
    }

    public void Cancel() => _verificationCancellation?.Cancel();

    public void Dispose()
    {
        // Let VerifyAsync dispose the CTS in its finally block after cancellation has
        // propagated. Disposing here could race with an in-flight verification.
        _verificationCancellation?.Cancel();
    }

}
