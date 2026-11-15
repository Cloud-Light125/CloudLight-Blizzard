using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using CloudLightBlizzard.ViewModels;
using CloudLightBlizzard.Services.Diagnostics;
using System.Windows.Data;

namespace CloudLightBlizzard.Views.Pages;

public partial class DiagnosticsPage : UserControl
{
    private DiagnosticsViewModel? _vm;
    private string _filter = "all";

    private void OnDiagnosticFilterChanged(object sender, RoutedEventArgs e)
    {
        if (sender is not RadioButton { Tag: string filter }) return;
        _filter = filter;
        if (_vm is null) return;
        CollectionViewSource.GetDefaultView(_vm.Checks).Filter = value => value is DiagnosticCheck check && (_filter switch
        {
            "issues" => check.Status is DiagnosticSeverity.Error or DiagnosticSeverity.Warning,
            "network" => check.Category.Contains("网络") || check.Category.Contains("代理"),
            "workers" => check.Category.Contains("Drops") || check.Category.Contains("Worker"),
            "data" => check.Category.Contains("区服") || check.Category.Contains("账号") || check.Category.Contains("Battle"),
            "environment" => check.Category.Contains("更新") || check.Category.Contains("环境") || check.Category.Contains("应用"),
            _ => true,
        });
    }

    public DiagnosticsPage() => InitializeComponent();

    public void Initialize(MainViewModel main)
    {
        _vm = new DiagnosticsViewModel(main);
        DataContext = _vm;
    }

    public void Dispose() => _vm?.Dispose();

    private async void OnStart(object sender, RoutedEventArgs e)
    {
        if (_vm is not null) await _vm.StartAsync();
    }

    private void OnCancel(object sender, RoutedEventArgs e) => _vm?.Cancel();

    private void OnCopy(object sender, RoutedEventArgs e)
    {
        if (_vm is null) return;
        try { Clipboard.SetText(_vm.CopyText()); }
        catch { MessageBox.Show("无法写入剪贴板，请稍后重试。", "诊断中心", MessageBoxButton.OK, MessageBoxImage.Information); }
    }

    private async void OnExport(object sender, RoutedEventArgs e)
    {
        if (_vm is null) return;
        var path = await _vm.ExportAsync();
        if (path is null) return;
        var result = MessageBox.Show($"诊断包已生成：\n{path}\n\n是否打开所在目录？", "诊断中心",
            MessageBoxButton.YesNo, MessageBoxImage.Information);
        if (result == MessageBoxResult.Yes)
        {
            try { Process.Start(new ProcessStartInfo { FileName = Path.GetDirectoryName(path)!, UseShellExecute = true }); }
            catch { }
        }
    }
}
