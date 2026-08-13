using System.Windows;
using System.Windows.Controls;

namespace BnetSwitch.Stats;

public partial class HeroDetailWindow : Window
{
    private HeroDetailWindow(HeroStat h)
    {
        InitializeComponent();
        DataContext = h;
        if (h.DetailStats.Count == 0) EmptyHint.Visibility = Visibility.Visible;
    }

    public static void ShowFor(Window owner, HeroStat h)
        => new HeroDetailWindow(h) { Owner = owner }.Show();

    // 场均 / 每10分钟 切换
    private void OnStatModeChanged(object sender, RoutedEventArgs e)
    {
        if (StatList == null || DataContext is not HeroStat h) return;
        StatList.ItemsSource = RbTen.IsChecked == true ? h.DetailStatsPerTen : h.DetailStats;
    }
}
