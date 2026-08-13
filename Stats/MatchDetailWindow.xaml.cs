using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using CloudLightBlizzard.Stats;

namespace CloudLightBlizzard.Stats;

public partial class MatchDetailWindow : Window
{
    private readonly StatsService _svc;
    private readonly long _roleId;
    private readonly string _matchId;
    private readonly bool _isFight;

    private MatchDetailWindow(StatsService svc, long roleId, string matchId, bool isFight)
    {
        InitializeComponent();
        _svc = svc;
        _roleId = roleId;
        _matchId = matchId;
        _isFight = isFight;
    }

    /// <summary>演示数据打开(--matchdemo,验证记分板布局不崩)。</summary>
    public static void ShowDemo()
    {
        var win = new MatchDetailWindow(null!, 0, "", false);
        win.DataContext = DemoMatchProvider.Get();
        win.OverlayPanel.Visibility = Visibility.Collapsed;
        win.Show();
    }

    /// <summary>为某局打开记分板。roleId=这局所属玩家,m=对局行(取 MatchId)。isFight=角斗。</summary>
    public static void ShowFor(Window owner, StatsService svc, long roleId, MatchRecord m, bool isFight = false)
    {
        var win = new MatchDetailWindow(svc, roleId, m.MatchId, isFight) { Owner = owner };
        win.Show();
        _ = win.LoadAsync();
    }

    private async Task LoadAsync()
    {
        try
        {
            var md = await _svc.LoadMatchDetailAsync(_roleId, _matchId, _isFight);
            if (md is null || (md.Teammates.Count == 0 && md.Enemies.Count == 0))
            {
                OverlayTitle.Text = "没有查到这局详情";
                OverlayHint.Text = "该对局可能已过期或不可查";
                return;
            }
            DataContext = md;
            ResultText.Foreground = (md.IsWin ? TryFindResource("Stats.OkText")
                : md.ResultText == "失败" ? TryFindResource("Stats.LossText")
                : TryFindResource("Stats.DrawText")) as Brush;
            OverlayPanel.Visibility = Visibility.Collapsed;
        }
        catch
        {
            OverlayTitle.Text = "加载失败";
            OverlayHint.Text = "网络开小差,关掉重开这局试试";
        }
    }

    // 点玩家 → 查该玩家战绩(bnetId 就是 roleId)
    private void OnPlayerClick(object sender, MouseButtonEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: MatchPlayer p } && p.BnetId > 0)
        {
            Close();
            if (Application.Current.MainWindow is MainWindow main)
                main.OpenStatsAccount(p.BnetId);
        }
    }

    // 点技能图标 → 弹窗看大图+效果;e.Handled 阻止冒泡到玩家行(否则会同时触发查战绩)
    private void OnSkillClick(object sender, MouseButtonEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: SkillIcon s })
        {
            SkillPopup.PlacementTarget = (UIElement)sender;
            SkillPopup.DataContext = s;
            SkillPopup.IsOpen = true;
        }
        e.Handled = true;
    }
}
