using System.Windows;
using System.Windows.Input;
using CloudLightBlizzard.Services.OverwatchRegion;

namespace CloudLightBlizzard.Views;

public enum RegionActionConfirmKind
{
    Prepare,
    Reprepare,
    Clear,
    RestartPreparation,
    SwitchBackupMode,
}

public partial class RegionActionConfirmWindow : Window
{
    public RegionActionConfirmWindow(RegionActionConfirmKind kind, OverwatchRegion? region = null,
        RegionBackupMode backupMode = RegionBackupMode.VerifiedDifference)
    {
        InitializeComponent();
        switch (kind)
        {
            case RegionActionConfirmKind.Prepare:
                var regionName = region == OverwatchRegion.International ? "国际服" : "国服";
                Title = TitleText.Text = $"准备{regionName}文件";
                MessageText.Text = "请确认：";
                BulletText.Text = $"• Battle.net 当前选择的是{regionName}\n• 游戏已经更新完成\n• Battle.net 显示可以正常启动游戏";
                FooterText.Text = backupMode == RegionBackupMode.VerifiedDifference
                    ? "接下来只会记录当前游戏文件的内容状态，不会复制整个游戏目录。"
                    : "接下来软件会先保存当前完整游戏文件到本地临时区域。这个过程只会读取和复制本地文件，不会产生网络流量。";
                ConfirmButton.Content = "开始准备";
                break;
            case RegionActionConfirmKind.Reprepare:
                Title = TitleText.Text = "重新准备区服文件";
                MessageText.Text = "通常只有以下情况才需要重新准备：";
                BulletText.Text = "• 《守望先锋》完成了较大的游戏更新\n• 程序提示当前区服备份已经过期\n• 本地区服备份文件损坏";
                FooterText.Text = "如果当前备份可以正常使用，不需要重新准备。旧备份会保留到新的准备过程完全成功。";
                ConfirmButton.Content = "开始重新准备";
                break;
            case RegionActionConfirmKind.Clear:
                Title = TitleText.Text = "删除所有区服备份？";
                MessageText.Text = "这会删除所有本地区服备份和未完成的准备数据。";
                BulletText.Text = "不会删除《守望先锋》游戏本体，也不会删除 Battle.net 账号备份。";
                FooterText.Text = "清除后，如果以后还需要快速切换国服和国际服，需要重新完成一次区服文件准备。";
                ConfirmButton.Content = "删除所有备份";
                ConfirmButton.Style = (Style)FindResource("DangerButton");
                break;
            case RegionActionConfirmKind.RestartPreparation:
                Title = TitleText.Text = "重新开始准备？";
                MessageText.Text = "这将删除当前未完成的区服文件准备数据，并从第一步重新开始。";
                BulletText.Text = "当前可用的区服备份不会被删除，仍可继续用于区服切换。";
                FooterText.Text = "第一步、第二步记录和候选临时文件会被清理。";
                ConfirmButton.Content = "重新开始";
                break;
            case RegionActionConfirmKind.SwitchBackupMode:
                Title = TitleText.Text = "切换备份模式？";
                MessageText.Text = "切换备份模式需要删除当前未完成的准备数据并重新开始。";
                BulletText.Text = "当前可用的区服备份不会被删除或替换。";
                FooterText.Text = "确认后将返回区服文件准备第一步。";
                ConfirmButton.Content = "切换模式";
                break;
        }
    }

    private void OnDrag(object sender, MouseButtonEventArgs e)
    {
        if (e.ButtonState == MouseButtonState.Pressed) DragMove();
    }

    private void OnConfirm(object sender, RoutedEventArgs e) => DialogResult = true;
    private void OnCancel(object sender, RoutedEventArgs e) => DialogResult = false;
}
