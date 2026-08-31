using System.Windows;
using System.Windows.Input;
using CloudLightBlizzard.Services.OverwatchRegion;

namespace CloudLightBlizzard.Views;

public partial class SwitchPreviewWindow : Window
{
    public SwitchPlan Plan { get; }
    public SwitchPreviewWindow(SwitchPlan plan)
    {
        Plan = plan ?? throw new ArgumentNullException(nameof(plan));
        InitializeComponent();
        DataContext = plan;
        RouteText.Text = $"{RegionName(plan.SourceRegion)} → {RegionName(plan.TargetRegion)}";
        RestoreText.Text = $"{plan.CopyCount:N0}";
        OverwriteText.Text = $"{plan.OverwriteCount:N0}";
        DeleteText.Text = $"{plan.DeleteCount:N0}";
        KeepText.Text = $"{plan.KeepCount:N0}";
        BytesText.Text = plan.EstimatedBytesText;
        StateText.Text = $"Battle.net：{plan.CurrentBattleNetState}";
        SnapshotText.Text = $"快照：{plan.BackupMode} · {plan.SnapshotState} · 需要磁盘空间约 {plan.RequiredDiskSpaceText}";
        WarningText.Text = string.Join("\n", plan.Warnings.Select(value => "• " + value));
        WarningBox.Visibility = plan.Warnings.Count == 0 ? Visibility.Collapsed : Visibility.Visible;
        BlockerText.Text = string.Join("\n", plan.Blockers.Select(value => "• " + value));
        BlockerBox.Visibility = plan.Blockers.Count == 0 ? Visibility.Collapsed : Visibility.Visible;
        StartButton.IsEnabled = plan.CanExecute;
        if (!plan.CanExecute) StartButton.ToolTip = "安全检查未通过，不能继续。";
    }

    private static string RegionName(OverwatchRegion? region) => region switch
    {
        OverwatchRegion.China => "国服",
        OverwatchRegion.International => "国际服",
        _ => "当前区服",
    };
    private void OnDrag(object sender, MouseButtonEventArgs e) { if (e.ButtonState == MouseButtonState.Pressed) DragMove(); }
    private void OnStart(object sender, RoutedEventArgs e) => DialogResult = true;
    private void OnCancel(object sender, RoutedEventArgs e) => DialogResult = false;
}
