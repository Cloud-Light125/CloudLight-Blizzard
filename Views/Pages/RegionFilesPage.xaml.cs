using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
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
            await _vm.SwitchGameRegionOnlyAsync(OverwatchRegion.China);
    }

    private async void OnSwitchInternational(object sender, RoutedEventArgs e)
    {
        if (_vm?.RegionGuide.CanSwitchInternational == true || _vm?.RegionGuide.CanRestore == true)
            await _vm.SwitchGameRegionOnlyAsync(OverwatchRegion.International);
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

    private async void OnCaptureChina(object sender, RoutedEventArgs e) =>
        await ConfirmAndPrepareAsync(OverwatchRegion.China);

    private async void OnCaptureInternational(object sender, RoutedEventArgs e) =>
        await ConfirmAndPrepareAsync(OverwatchRegion.International);

    private async Task ConfirmAndPrepareAsync(OverwatchRegion region)
    {
        if (_vm?.RegionGuide.CanChooseCurrentRegion != true) return;
        var confirmed = new RegionActionConfirmWindow(RegionActionConfirmKind.Prepare, region)
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
        if (new RegionActionConfirmWindow(RegionActionConfirmKind.Reprepare)
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
}
