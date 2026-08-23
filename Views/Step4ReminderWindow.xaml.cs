using System.Windows;
using CloudLightBlizzard.Services.OverwatchRegion;

namespace CloudLightBlizzard.Views;

public enum Step4ReminderChoice { SwitchOnly, Verify, Ignore }

public partial class Step4ReminderWindow : Window
{
    public Step4ReminderChoice Choice { get; private set; } = Step4ReminderChoice.SwitchOnly;

    public Step4ReminderWindow(OverwatchRegion target)
    {
        InitializeComponent();
        TargetText.Text = $"这次将切换到{(target == OverwatchRegion.China ? "国服" : "国际服")}。是否在切换完成后进行进一步验证？";
    }

    private void OnSwitchOnly(object sender, RoutedEventArgs e) { Choice = Step4ReminderChoice.SwitchOnly; DialogResult = true; }
    private void OnVerify(object sender, RoutedEventArgs e) { Choice = Step4ReminderChoice.Verify; DialogResult = true; }
    private void OnIgnore(object sender, RoutedEventArgs e) { Choice = Step4ReminderChoice.Ignore; DialogResult = true; }
}
