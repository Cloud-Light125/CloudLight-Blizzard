using System.Windows;
using System.Windows.Input;

namespace CloudLightBlizzard.Stats;

public partial class FriendsWindow : Window
{
    private readonly StatsService _svc;
    private readonly long _roleId;
    private readonly int _season;

    private FriendsWindow(StatsService svc, long roleId, int season, string name)
    {
        InitializeComponent();
        _svc = svc;
        _roleId = roleId;
        _season = season;
        if (!string.IsNullOrEmpty(name)) TitleText.Text = $"{name} 的好友";
    }

    public static void ShowFor(Window owner, StatsService svc, long roleId, int season, string name)
    {
        var win = new FriendsWindow(svc, roleId, season, name) { Owner = owner };
        win.Show();
        _ = win.LoadAsync();
    }

    private async Task LoadAsync()
    {
        try
        {
            var data = await _svc.LoadFriendsAsync(_roleId, _season);
            if (data.Rows.Count == 0)
            {
                OverlayTitle.Text = "没有好友数据";
                OverlayHint.Text = "该玩家暂无守望先锋好友,或好友本赛季无排位数据";
                return;
            }
            SubText.Text = $"{data.Total} 位有守望战绩的好友 · 点击查(无战绩的好友接口不返回)";
            FriendList.ItemsSource = data.Rows;
            OverlayPanel.Visibility = Visibility.Collapsed;
        }
        catch
        {
            OverlayTitle.Text = "加载失败";
            OverlayHint.Text = "网络开小差,关掉重开试试";
        }
    }

    // 点好友 → 查该好友战绩(bnetId 就是 roleId)
    private void OnFriendClick(object sender, MouseButtonEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: FriendRow f } && f.BnetId > 0)
        {
            Close();
            if (Application.Current.MainWindow is MainWindow main)
                main.OpenStatsAccount(f.BnetId);
        }
    }
}
