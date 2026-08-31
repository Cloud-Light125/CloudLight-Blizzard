using System.Windows;
using System.Windows.Controls;
using CloudLightBlizzard.ViewModels;

namespace CloudLightBlizzard.Views.Pages;

public partial class OverviewPage : UserControl
{
    private MainViewModel? _main;
    private OverviewViewModel? _vm;
    public OverviewPage() => InitializeComponent();
    public void Initialize(MainViewModel main)
    {
        _main = main;
        _vm = new OverviewViewModel(main);
        DataContext = _vm;
    }
    public Task RefreshAsync() => _vm?.RefreshAsync() ?? Task.CompletedTask;
    private async void OnRefresh(object sender, RoutedEventArgs e) => await RefreshAsync();
    private void OnOpenRegion(object sender, RoutedEventArgs e) => (Window.GetWindow(this) as MainWindow)?.OpenRegion();
    private void OnOpenDrops(object sender, RoutedEventArgs e) => (Window.GetWindow(this) as MainWindow)?.OpenDrops();
    private void OnOpenDiagnostics(object sender, RoutedEventArgs e) => (Window.GetWindow(this) as MainWindow)?.OpenDiagnostics();
    private void OnOpenSnapshots(object sender, RoutedEventArgs e) => (Window.GetWindow(this) as MainWindow)?.OpenSnapshots();
    private async void OnCheckUpdate(object sender, RoutedEventArgs e) { if (Window.GetWindow(this) is MainWindow window) await window.CheckForUpdatesManuallyAsync(); await RefreshAsync(); }
}
