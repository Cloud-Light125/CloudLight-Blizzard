using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using CloudLightBlizzard.Services;
using CloudLightBlizzard.Services.OverwatchRegion;
using CloudLightBlizzard.ViewModels;

namespace CloudLightBlizzard.Views.Pages;

public partial class RegionFilesPage : UserControl
{
    private MainViewModel? _vm;

    public RegionFilesPage() => InitializeComponent();

    public void Initialize(MainViewModel vm)
    {
        _vm = vm;
        DataContext = vm;
    }

    public Task RefreshAsync(bool verifyFiles = false) => _vm?.RefreshRegionPageAsync() ?? Task.CompletedTask;

    private async void OnSwitchChina(object sender, RoutedEventArgs e)
    {
        if (_vm?.RegionGuide.CanSwitchChina == true || _vm?.RegionGuide.CanRestore == true)
            await PreviewAndSwitchAsync(OverwatchRegion.China);
    }

    private async void OnSwitchInternational(object sender, RoutedEventArgs e)
    {
        if (_vm?.RegionGuide.CanSwitchInternational == true || _vm?.RegionGuide.CanRestore == true)
            await PreviewAndSwitchAsync(OverwatchRegion.International);
    }

    private async Task PreviewAndSwitchAsync(OverwatchRegion target)
    {
        if (_vm is null) return;
        var plan = await _vm.CreateRegionSwitchPlanAsync(target);
        if (plan is null) return;
        var preview = new SwitchPreviewWindow(plan) { Owner = Window.GetWindow(this) };
        if (preview.ShowDialog() == true)
            await _vm.SwitchGameRegionOnlyAsync(target, plan);
    }

    private void OnChooseGame(object sender, RoutedEventArgs e) => _vm?.SetOverwatchGamePath();

    private void OnChooseStorage(object sender, RoutedEventArgs e) => _vm?.SetRegionStoragePath();

    private void OnDetectGame(object sender, RoutedEventArgs e)
    {
        if (_vm?.AutoDetectOverwatchGamePath() == false)
            MessageBox.Show("未能可靠找到守望先锋安装目录，请手动选择。", "自动查找",
                MessageBoxButton.OK, MessageBoxImage.Information);
    }

    private void OnOpenStorage(object sender, RoutedEventArgs e)
    {
        if (_vm == null) return;
        Directory.CreateDirectory(_vm.RegionBackupRoot);
        Process.Start(new ProcessStartInfo { FileName = _vm.RegionBackupRoot, UseShellExecute = true });
    }

    private void OnOpenLogs(object sender, RoutedEventArgs e)
    {
        Directory.CreateDirectory(AppPaths.Current.LogsDir);
        Process.Start(new ProcessStartInfo { FileName = AppPaths.Current.LogsDir, UseShellExecute = true });
    }

    private async void OnCaptureChina(object sender, RoutedEventArgs e) =>
        await ConfirmAndPrepareAsync(OverwatchRegion.China);

    private async void OnCaptureInternational(object sender, RoutedEventArgs e) =>
        await ConfirmAndPrepareAsync(OverwatchRegion.International);

    private async Task ConfirmAndPrepareAsync(OverwatchRegion region)
    {
        if (_vm?.RegionGuide.CanChooseCurrentRegion != true) return;
        var confirmed = new RegionActionConfirmWindow(RegionActionConfirmKind.Prepare, region,
            _vm.IsVerifiedDifferenceMode ? RegionBackupMode.VerifiedDifference : RegionBackupMode.FullSnapshot)
        {
            Owner = Window.GetWindow(this)
        }.ShowDialog() == true;
        if (confirmed) await _vm.BeginRegionPreparationAsync(region);
    }

    private async void OnComplete(object sender, RoutedEventArgs e)
    {
        if (_vm?.RegionGuide.CanContinueOtherRegion == true)
            await _vm.CompleteRegionBackupAsync();
    }

    private async void OnValidate(object sender, RoutedEventArgs e)
    {
        if (_vm?.RegionGuide.CanValidate == true) await _vm.ValidateRegionBackupAsync();
    }

    private void OnReprepare(object sender, RoutedEventArgs e)
    {
        if (_vm?.RegionGuide.CanRestart != true) return;
        var kind = _vm.HasPendingRegionPreparation
            ? RegionActionConfirmKind.RestartPreparation
            : RegionActionConfirmKind.Reprepare;
        if (new RegionActionConfirmWindow(kind)
            { Owner = Window.GetWindow(this) }.ShowDialog() == true)
            _vm.RequestRegionReprepare();
    }

    private void OnClear(object sender, RoutedEventArgs e)
    {
        if (_vm?.RegionGuide.CanClear != true) return;
        if (new RegionActionConfirmWindow(RegionActionConfirmKind.Clear)
            { Owner = Window.GetWindow(this) }.ShowDialog() == true)
            _vm.ResetRegionBackup();
    }

    private void OnCancel(object sender, RoutedEventArgs e) => _vm?.CancelRegionOperation();

    private async void OnRetry(object sender, RoutedEventArgs e)
    {
        if (_vm?.RegionGuide.CanRetry == true) await _vm.RetryRegionStatusAsync();
    }

    private void OnVerifiedMode(object sender, RoutedEventArgs e) =>
        ChangeBackupMode(RegionBackupMode.VerifiedDifference);

    private void OnFullMode(object sender, RoutedEventArgs e) =>
        ChangeBackupMode(RegionBackupMode.FullSnapshot);

    private void OnUseFullSnapshot(object sender, RoutedEventArgs e) =>
        ChangeBackupMode(RegionBackupMode.FullSnapshot);

    private void ChangeBackupMode(RegionBackupMode mode)
    {
        if (_vm is null) return;
        var alreadySelected = mode == RegionBackupMode.VerifiedDifference
            ? _vm.IsVerifiedDifferenceMode : _vm.IsFullSnapshotMode;
        if (alreadySelected)
        {
            ResetModeChecks();
            return;
        }
        if (_vm.HasPendingRegionPreparation &&
            new RegionActionConfirmWindow(RegionActionConfirmKind.SwitchBackupMode)
                { Owner = Window.GetWindow(this) }.ShowDialog() != true)
        {
            ResetModeChecks();
            return;
        }
        _vm.ChangeRegionBackupMode(mode);
        ResetModeChecks();
    }

    private void ResetModeChecks()
    {
        if (_vm is null) return;
        VerifiedModeRadio.IsChecked = _vm.IsVerifiedDifferenceMode;
        FullModeRadio.IsChecked = _vm.IsFullSnapshotMode;
    }

    private async void OnRedoStep1(object sender, RoutedEventArgs e)
    {
        if (_vm?.RegionGuide.CanRedoStep1 == true) await _vm.RedoVerifiedStep1Async();
    }

    private async void OnRedoStep2(object sender, RoutedEventArgs e)
    {
        if (_vm?.RegionGuide.CanRedoStep2 == true) await _vm.RedoVerifiedStep2Async();
    }

    private void OnReturnStep1(object sender, RoutedEventArgs e)
    {
        if (_vm?.RegionGuide.CanReturnToStep1 != true) return;
        if (new RegionActionConfirmWindow(RegionActionConfirmKind.RestartPreparation)
            { Owner = Window.GetWindow(this) }.ShowDialog() == true)
            _vm.ReturnRegionPreparationToStep1();
    }

    private async void OnCheckCurrentFiles(object sender, RoutedEventArgs e)
    {
        if (_vm?.CanCheckRegionFiles == true) await _vm.CheckCurrentRegionFilesAsync();
    }

    private async void OnClearTemporary(object sender, RoutedEventArgs e)
    {
        if (_vm?.CanClearTemporaryFiles != true) return;
        if (MessageBox.Show(
                $"将只删除本次检查确认的 {_vm.RegionFileCheck?.TemporaryCount ?? 0:N0} 个临时/额外文件候选。\n\n" +
                "永久区服文件、另一地区文件、当前备份和 Battle.net Agent 数据都不会删除。是否继续？",
                "清除临时/额外文件？", MessageBoxButton.YesNo, MessageBoxImage.Warning) == MessageBoxResult.Yes)
            await _vm.ClearTemporaryFilesAsync();
    }

    private async void OnResetCurrentRegion(object sender, RoutedEventArgs e)
    {
        if (_vm?.CanResetCurrentRegion != true) return;
        if (MessageBox.Show(
                "将把当前磁盘状态重新定义为当前区服的新状态，另一个区服的已保存状态和备份保持不变。\n\n" +
                "无法安全建立的新差异只会报告，不会加入可恢复备份。是否继续？",
                "重设当前区服状态？", MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes)
            await _vm.ResetCurrentRegionStateAsync();
    }

    private async void OnStep4(object sender, RoutedEventArgs e)
    {
        if (_vm?.CanRunStep4 == true) await _vm.RunStep4ManuallyAsync();
    }

    private void OnRestoreStep4Reminder(object sender, RoutedEventArgs e) => _vm?.RestoreStep4Reminder();
}
