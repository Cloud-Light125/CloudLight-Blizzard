using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using CloudLightBlizzard.Services;
using CloudLightBlizzard.Services.Overwatch;
using CloudLightBlizzard.ViewModels;

namespace CloudLightBlizzard.Views.Pages;

public partial class SettingsPage : UserControl
{
    private MainViewModel? _vm;
    private bool _loading = true;
    public SettingsPage() => InitializeComponent();
    public void Initialize(MainViewModel vm)
    {
        _vm = vm; CloseToTrayBox.IsChecked = vm.Settings.CloseToTray; StartMinimizedBox.IsChecked = vm.Settings.StartMinimized;
        StartupBox.IsChecked = StartupService.IsEnabled(); DarkModeBox.IsChecked = vm.Settings.DarkMode;
        DataPathText.Text = AppPaths.Current.Root;
        CacheText.Text = "打开设置页后统计";
        IsVisibleChanged += async (_, _) =>
        {
            if (!IsVisible) return;
            await Task.Yield();
            await RefreshCacheSizeAsync();
        };
        Refresh(); _loading = false;
    }
    private void Refresh()
    {
        if (_vm == null) return; ExePathText.Text = _vm.Settings.ClientExe ?? "自动检测（未手动指定）";
    }
    private async Task RefreshCacheSizeAsync()
    {
        var bytes = await Task.Run(OwImageCache.CacheSizeBytes);
        CacheText.Text = bytes >= 1024 * 1024 ? $"当前缓存约 {bytes / 1024d / 1024:0.0} MB" : $"当前缓存约 {bytes / 1024d:0.0} KB";
    }
    private void OnSettingChanged(object sender, RoutedEventArgs e) => Save();
    private void OnThemeChanged(object sender, RoutedEventArgs e) { if (_loading || _vm == null) return; _vm.Settings.DarkMode = DarkModeBox.IsChecked == true; _vm.Settings.Save(); ThemeManager.Apply(_vm.Settings.DarkMode); }
    private void Save()
    {
        if (_loading || _vm == null) return; _vm.Settings.CloseToTray = CloseToTrayBox.IsChecked == true; _vm.Settings.StartMinimized = StartMinimizedBox.IsChecked == true; _vm.Settings.Save(); StartupService.SetEnabled(StartupBox.IsChecked == true);
    }
    private void OnChooseExe(object sender, RoutedEventArgs e) { _vm?.SetExePath(); Refresh(); }
    private void OnAutoDetect(object sender, RoutedEventArgs e) { if (_vm == null) return; _vm.Settings.ClientExe = null; _vm.Settings.Save(); Refresh(); }
    private void OnOpenData(object sender, RoutedEventArgs e) { Directory.CreateDirectory(AppPaths.Current.Root); Process.Start(new ProcessStartInfo { FileName = AppPaths.Current.Root, UseShellExecute = true }); }
    private void OnClearCache(object sender, RoutedEventArgs e) { OwImageCache.ClearCache(); Refresh(); }
}
