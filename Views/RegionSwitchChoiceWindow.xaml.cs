using System.Windows;
using BnetSwitch.Services.OverwatchRegion;

namespace BnetSwitch;

public enum RegionSwitchChoice { Cancel, AccountOnly, Settings }

public partial class RegionSwitchChoiceWindow : Window
{
    private RegionSwitchChoice _choice;
    public RegionSwitchChoiceWindow(string targetRegion, RegionBackupState state)
    {
        InitializeComponent();
        TitleText.Text = $"目标账号使用{targetRegion}，但对应的游戏文件尚未准备好。";
        StateText.Text = state switch
        {
            RegionBackupState.Empty => "尚未设置区服文件。",
            RegionBackupState.Stale => "游戏已经更新，需要重新准备区服文件。",
            RegionBackupState.Legacy => "区服文件功能已经升级，需要重新准备一次。",
            RegionBackupState.Preparing => "区服文件正在准备中，请先按引导完成一次跨区更新。",
            _ => "区服文件仍在准备中。",
        };
    }
    public RegionSwitchChoice ShowDialogChoice() { ShowDialog(); return _choice; }
    private void OnCancel(object sender, RoutedEventArgs e) { _choice = RegionSwitchChoice.Cancel; Close(); }
    private void OnAccountOnly(object sender, RoutedEventArgs e) { _choice = RegionSwitchChoice.AccountOnly; Close(); }
    private void OnSettings(object sender, RoutedEventArgs e) { _choice = RegionSwitchChoice.Settings; Close(); }
}
