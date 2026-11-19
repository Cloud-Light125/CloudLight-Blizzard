using System.Windows;
using System.Windows.Controls;
namespace CloudLightBlizzard.Views.Drops.Soop;
public partial class SoopWorkspace:UserControl
{
    public event Action<string,object,RoutedEventArgs>? ActionRequested;
    public SoopWorkspace()
    {
        InitializeComponent();
        SoopClaimButton.SetBinding(ContentControl.ContentProperty, new System.Windows.Data.Binding("SelectedItem.SoopClaimButtonText")
        {
            Source = SoopInventoryList, TargetNullValue = "领取选中奖励", FallbackValue = "领取选中奖励",
        });
    }
    private void OnNavigateSettings(object sender,RoutedEventArgs e)
    {
        var section = (sender as Button)?.Tag as string;
        (section == "priority" ? (FrameworkElement)PrioritySection : section == "preferences" ? PreferencesSection : SoopSettingsCard).BringIntoView();
    }
    private void OnAutoStartChanged(object sender,RoutedEventArgs e)=>ActionRequested?.Invoke("OnAutoStartChanged",sender,e);
    private void OnClaimSoopReward(object sender,RoutedEventArgs e)=>ActionRequested?.Invoke("OnClaimSoopReward",sender,e);
    private void OnCopySoopCode(object sender,RoutedEventArgs e)=>ActionRequested?.Invoke("OnCopySoopCode",sender,e);
    private void OnOpenSoopInventory(object sender,RoutedEventArgs e)=>ActionRequested?.Invoke("OnOpenSoopInventory",sender,e);
    private void OnSoopAddAccount(object sender,RoutedEventArgs e)=>ActionRequested?.Invoke("OnSoopAddAccount",sender,e);
    private void OnSoopChannelModeChanged(object sender,SelectionChangedEventArgs e)=>ActionRequested?.Invoke("OnSoopChannelModeChanged",sender,e);
    private void OnSoopDeleteAccount(object sender,RoutedEventArgs e)=>ActionRequested?.Invoke("OnSoopDeleteAccount",sender,e);
    private void OnSoopPasswordChanged(object sender,RoutedEventArgs e)=>ActionRequested?.Invoke("OnSoopPasswordChanged",sender,e);
    private void OnSoopRefresh(object sender,RoutedEventArgs e)=>ActionRequested?.Invoke("OnSoopRefresh",sender,e);
    private void OnSoopSetPrimaryAccount(object sender,RoutedEventArgs e)=>ActionRequested?.Invoke("OnSoopSetPrimaryAccount",sender,e);
    private void OnSoopStartAccount(object sender,RoutedEventArgs e)=>ActionRequested?.Invoke("OnSoopStartAccount",sender,e);
    private void OnSoopStopAccount(object sender,RoutedEventArgs e)=>ActionRequested?.Invoke("OnSoopStopAccount",sender,e);
    private void OnStart(object sender,RoutedEventArgs e)=>ActionRequested?.Invoke("OnStart",sender,e);
    private void OnStop(object sender,RoutedEventArgs e)=>ActionRequested?.Invoke("OnStop",sender,e);
}
