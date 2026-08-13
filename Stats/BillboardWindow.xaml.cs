using System.Windows;

namespace CloudLightBlizzard.Stats;

public partial class BillboardWindow : Window
{
    private readonly StatsService _svc;
    private readonly long _roleId;
    private readonly bool _useOpen;

    private BillboardWindow(StatsService svc, long roleId, bool useOpen)
    {
        InitializeComponent();
        _svc = svc;
        _roleId = roleId;
        _useOpen = useOpen;
    }

    public static void ShowFor(Window owner, StatsService svc, long roleId, bool useOpen)
    {
        var win = new BillboardWindow(svc, roleId, useOpen) { Owner = owner };
        win.Show();
        _ = win.LoadAsync();
    }

    private async Task LoadAsync()
    {
        try
        {
            var data = await _svc.LoadBillboardAsync(_roleId, _useOpen);
            if (data.Rows.Count == 0)
            {
                OverlayTitle.Text = "暂无排行榜数据";
                OverlayHint.Text = "该玩家本赛季暂未上榜";
                return;
            }
            TitleText.Text = data.Title;
            SubText.Text = data.SubTitle;
            BoardList.ItemsSource = data.Rows;
            OverlayPanel.Visibility = Visibility.Collapsed;
        }
        catch
        {
            OverlayTitle.Text = "加载失败";
            OverlayHint.Text = "网络开小差,关掉重开试试";
        }
    }
}
