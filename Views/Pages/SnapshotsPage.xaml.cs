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
    private void OnCloseDetails(object sender, RoutedEventArgs e) => _vm?.ClearDetails();
    private void OnOpenRegion(object sender, RoutedEventArgs e) => (Window.GetWindow(this) as MainWindow)?.OpenRegion();

    private void OnSnapshotAction(object sender, RoutedEventArgs e)
    {
        if (_vm is null || sender is not Button { DataContext: SnapshotItemViewModel item, Tag: string action }) return;
        switch (action)
        {
            case "open": _vm.OpenDirectory(item); break;
            case "regenerate":
                if (MessageBox.Show("将返回区服准备流程，重新生成快照不会直接删除游戏文件。是否继续？", "重新生成快照", MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes)
                    _vm.Regenerate();
                break;
            case "delete":
                if (item.IsActive)
                {
                    ShowActiveSnapshotNotice();
                    break;
                }
                if (MessageBox.Show($"确定删除此 CloudLight Blizzard 快照？\n\n模式：{item.ModeText}\n文件：{item.FileCountText}\n大小：{item.SizeText}\n\n只会删除受管理的快照路径，不会删除游戏文件。", "删除快照", MessageBoxButton.YesNo, MessageBoxImage.Warning) == MessageBoxResult.Yes)
                {
                    var deleted = _vm.Delete(item);
                    MessageBox.Show(Window.GetWindow(this), deleted ? "快照已删除" : _vm.StatusText,
                        deleted ? "删除完成" : "未能删除快照", MessageBoxButton.OK,
                        deleted ? MessageBoxImage.Information : MessageBoxImage.Warning);
                }
                break;
            case "details":
                _vm.ShowDetails(item);
                break;
        }
    }

    internal void ShowActiveSnapshotNotice() => CreateActiveSnapshotNotice().ShowDialog();

    internal Window CreateActiveSnapshotNotice()
    {
        var panel = new StackPanel { Margin = new Thickness(24) };
        panel.Children.Add(new TextBlock { Text = "当前使用的快照不能直接删除，请先通过区服准备流程解除引用。", TextWrapping = TextWrapping.Wrap, MaxWidth = 420, Margin = new Thickness(0,0,0,20) });
        var navigate = new Button { Content = "前往区服切换", HorizontalAlignment = HorizontalAlignment.Right };
        navigate.SetResourceReference(StyleProperty, "PrimaryButton");
        panel.Children.Add(navigate);
        var dialog = new Window { Title = "当前快照受保护", Owner = Window.GetWindow(this), Content = panel, SizeToContent = SizeToContent.WidthAndHeight, WindowStartupLocation = WindowStartupLocation.CenterOwner, ResizeMode = ResizeMode.NoResize };
        dialog.SetResourceReference(Window.BackgroundProperty, "App.Background");
        dialog.SetResourceReference(Window.ForegroundProperty, "Text.Primary");
        Services.ThemeManager.Attach(dialog);
        navigate.Click += (_,_) => { dialog.Close(); OnOpenRegion(this, new RoutedEventArgs()); };
        return dialog;
    }
}
