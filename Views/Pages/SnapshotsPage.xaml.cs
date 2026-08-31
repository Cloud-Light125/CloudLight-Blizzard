using System.Windows;
using System.Windows.Controls;
using CloudLightBlizzard.ViewModels;

namespace CloudLightBlizzard.Views.Pages;

public partial class SnapshotsPage : UserControl
{
    private SnapshotsViewModel? _vm;
    public SnapshotsPage() => InitializeComponent();

    public void Initialize(MainViewModel main)
    {
        _vm = new SnapshotsViewModel(main);
        DataContext = _vm;
    }

    public Task RefreshAsync() => _vm?.RefreshAsync() ?? Task.CompletedTask;
    private async void OnRefresh(object sender, RoutedEventArgs e) => await RefreshAsync();
    private void OnCancel(object sender, RoutedEventArgs e) => _vm?.Cancel();
    private void OnCloseDetails(object sender, RoutedEventArgs e) => _vm?.ClearDetails();
    private void OnOpenRegion(object sender, RoutedEventArgs e) => (Window.GetWindow(this) as MainWindow)?.OpenRegion();

    private async void OnSnapshotAction(object sender, RoutedEventArgs e)
    {
        if (_vm is null || sender is not Button { DataContext: SnapshotItemViewModel item, Tag: string action }) return;
        switch (action)
        {
            case "verify": await _vm.VerifyAsync(item); break;
            case "open": _vm.OpenDirectory(item); break;
            case "regenerate":
                if (MessageBox.Show("将返回区服准备流程，重新生成快照不会直接删除游戏文件。是否继续？", "重新生成快照", MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes)
                    _vm.Regenerate();
                break;
            case "delete":
                if (MessageBox.Show($"确定删除此 CloudLight Blizzard 快照？\n\n模式：{item.ModeText}\n文件：{item.FileCountText}\n大小：{item.SizeText}\n\n只会删除受管理的快照路径，不会删除游戏文件。", "删除快照", MessageBoxButton.YesNo, MessageBoxImage.Warning) == MessageBoxResult.Yes)
                    _vm.Delete(item);
                break;
            case "details":
                _vm.ShowDetails(item);
                break;
        }
    }

    public void Dispose() => _vm?.Dispose();
}
