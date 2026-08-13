using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using BnetSwitch.Services.OverwatchRegion;
using BnetSwitch.ViewModels;

namespace BnetSwitch.Views.Pages;

public partial class RegionFilesPage : UserControl
{
    private MainViewModel? _vm;
    private CancellationTokenSource? _cancellation;
    public RegionFilesPage() => InitializeComponent();
    public void Initialize(MainViewModel vm) { _vm = vm; DataContext = vm; }
    public Task RefreshAsync(bool verifyFiles = false) => RefreshCoreAsync(verifyFiles);

    private async Task RefreshCoreAsync(bool verifyFiles = false)
    {
        if (_vm == null) return;
        GamePathText.Text = _vm.Settings.OverwatchGamePath ?? "尚未设置";
        StoragePathText.Text = _vm.RegionBackupRoot;
        CurrentRegionText.Text = "正在读取…";
        StateText.Text = "后台刷新中";
        StatusText.Text = "正在后台读取本地区服文件状态…";
        SwitchChinaButton.IsEnabled = false;
        SwitchInternationalButton.IsEnabled = false;
        await System.Windows.Threading.Dispatcher.Yield(
            System.Windows.Threading.DispatcherPriority.Render);
        try
        {
            var s = await _vm.GetRegionStatusAsync(verifyFiles);
            ApplyStatus(s);
            if (!verifyFiles && s.State is RegionBackupState.Ready or RegionBackupState.Stale)
            {
                StatusText.Text = "页面已显示，正在后台校验本地区服文件…";
                await System.Windows.Threading.Dispatcher.Yield(
                    System.Windows.Threading.DispatcherPriority.Background);
                ApplyStatus(await _vm.GetRegionStatusAsync(verifyFiles: true));
            }
            StatusText.Text = "状态已刷新。";
        }
        catch (Exception ex) { StatusText.Text = "读取状态失败：" + ex.Message; }
    }

    private void ApplyStatus(RegionSnapshotStatus s)
    {
            CurrentRegionText.Text = s.State == RegionBackupState.Stale ? "游戏已更新，需要重新准备" : RegionName(s.CurrentRegion);
            ChinaText.Text = s.ChinaBackupComplete ? "已准备" : s.ChinaCaptured ? "已保存，等待另一端" : "尚未准备";
            InternationalText.Text = s.InternationalBackupComplete ? "已准备" : s.InternationalCaptured ? "已保存，等待另一端" : "尚未准备";
            StateText.Text = StateName(s.State);
            SizeText.Text = $"{s.DifferenceCount} 个文件 · {FormatBytes(s.BackupBytes)}";
            SwitchChinaButton.IsEnabled = s.State == RegionBackupState.Ready && s.CurrentRegion != CurrentGameRegion.China;
            SwitchInternationalButton.IsEnabled = s.State == RegionBackupState.Ready && s.CurrentRegion != CurrentGameRegion.International;
    }
    private async void OnSwitchChina(object sender, RoutedEventArgs e) { if (_vm != null) await _vm.SwitchGameRegionOnlyAsync(OverwatchRegion.China); await RefreshCoreAsync(); }
    private async void OnSwitchInternational(object sender, RoutedEventArgs e) { if (_vm != null) await _vm.SwitchGameRegionOnlyAsync(OverwatchRegion.International); await RefreshCoreAsync(); }
    private void OnChooseGame(object sender, RoutedEventArgs e) { _vm?.SetOverwatchGamePath(); _ = RefreshCoreAsync(); }
    private void OnChooseStorage(object sender, RoutedEventArgs e) { _vm?.SetRegionStoragePath(); _ = RefreshCoreAsync(); }
    private void OnDetectGame(object sender, RoutedEventArgs e)
    {
        if (_vm?.AutoDetectOverwatchGamePath() == false) MessageBox.Show("未能可靠找到守望先锋安装目录，请手动选择。", "自动查找", MessageBoxButton.OK, MessageBoxImage.Information);
        _ = RefreshCoreAsync();
    }
    private void OnOpenStorage(object sender, RoutedEventArgs e)
    {
        if (_vm == null) return; Directory.CreateDirectory(_vm.RegionBackupRoot);
        Process.Start(new ProcessStartInfo { FileName = _vm.RegionBackupRoot, UseShellExecute = true });
    }
    private async void OnCaptureChina(object sender, RoutedEventArgs e) => await Run(token => _vm!.CaptureRegionAsync(OverwatchRegion.China, Progress(), token));
    private async void OnCaptureInternational(object sender, RoutedEventArgs e) => await Run(token => _vm!.CaptureRegionAsync(OverwatchRegion.International, Progress(), token));
    private async void OnComplete(object sender, RoutedEventArgs e) => await Run(token => _vm!.CompleteRegionBackupAsync(Progress(), token));
    private async void OnValidate(object sender, RoutedEventArgs e)
    {
        StatusText.Text = "正在后台完整检查本地文件…";
        await RefreshCoreAsync(verifyFiles: true);
    }
    private void OnClear(object sender, RoutedEventArgs e)
    {
        if (_vm == null || MessageBox.Show("清除已保存的国服和国际服文件？\n\n不会删除游戏，但以后切换前需要重新准备。", "清除区服文件备份", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes) return;
        _vm.ResetRegionBackup(); _ = RefreshCoreAsync();
    }
    private IProgress<RegionProgress> Progress() => new Progress<RegionProgress>(p => { ProgressText.Text = p.Message; ProgressBar.Maximum = Math.Max(1, p.Total); ProgressBar.Value = Math.Min(p.Current, ProgressBar.Maximum); });
    private async Task Run(Func<CancellationToken, Task> action)
    {
        _cancellation = new(); IdleActions.Visibility = Visibility.Collapsed; BusyActions.Visibility = Visibility.Visible;
        try { await action(_cancellation.Token); StatusText.Text = "操作完成。"; }
        catch (OperationCanceledException) { StatusText.Text = "已取消，本次未完成的临时文件已清理。"; }
        catch (Exception ex) { StatusText.Text = "操作失败：" + ex.Message; MessageBox.Show(ex.Message, "区服文件", MessageBoxButton.OK, MessageBoxImage.Error); }
        finally { _cancellation.Dispose(); _cancellation = null; BusyActions.Visibility = Visibility.Collapsed; IdleActions.Visibility = Visibility.Visible; }
        await RefreshCoreAsync();
    }
    private void OnCancel(object sender, RoutedEventArgs e) => _cancellation?.Cancel();
    private static string RegionName(CurrentGameRegion value) => value switch { CurrentGameRegion.China => "国服", CurrentGameRegion.International => "国际服", CurrentGameRegion.Mixed => "混合状态", _ => "未知" };
    private static string StateName(RegionBackupState value) => value switch { RegionBackupState.Ready => "可以使用", RegionBackupState.Preparing => "正在准备，请完成一次跨区更新", RegionBackupState.Stale => "游戏已更新，需要重新准备", RegionBackupState.Empty => "尚未准备", RegionBackupState.Legacy => "需要重新准备", _ => "本地文件不完整" };
    private static string FormatBytes(long bytes) => bytes >= 1024L * 1024 * 1024 ? $"{bytes / 1024d / 1024 / 1024:0.0} GB" : bytes >= 1024L * 1024 ? $"{bytes / 1024d / 1024:0.0} MB" : $"{bytes / 1024d:0.0} KB";
}
