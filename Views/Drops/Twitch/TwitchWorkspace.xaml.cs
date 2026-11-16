using System.Windows;
using System.Windows.Controls;
namespace CloudLightBlizzard.Views.Drops.Twitch;
public partial class TwitchWorkspace:UserControl
{
    public event Action<string,object,RoutedEventArgs>? ActionRequested;
    public TwitchWorkspace()=>InitializeComponent();
    private void OnAddExcludedGame(object sender,RoutedEventArgs e)=>ActionRequested?.Invoke("OnAddExcludedGame",sender,e);
    private void OnAddPriorityGame(object sender,RoutedEventArgs e)=>ActionRequested?.Invoke("OnAddPriorityGame",sender,e);
    private void OnAutoStartChanged(object sender,RoutedEventArgs e)=>ActionRequested?.Invoke("OnAutoStartChanged",sender,e);
    private void OnOpenTwitchInventory(object sender,RoutedEventArgs e)=>ActionRequested?.Invoke("OnOpenTwitchInventory",sender,e);
    private void OnPriorityDown(object sender,RoutedEventArgs e)=>ActionRequested?.Invoke("OnPriorityDown",sender,e);
    private void OnPriorityUp(object sender,RoutedEventArgs e)=>ActionRequested?.Invoke("OnPriorityUp",sender,e);
    private void OnRemoveExcludedGame(object sender,RoutedEventArgs e)=>ActionRequested?.Invoke("OnRemoveExcludedGame",sender,e);
    private void OnRemovePriorityGame(object sender,RoutedEventArgs e)=>ActionRequested?.Invoke("OnRemovePriorityGame",sender,e);
    private void OnRepairTwitchSettings(object sender,RoutedEventArgs e)=>ActionRequested?.Invoke("OnRepairTwitchSettings",sender,e);
    private void OnTwitchCampaignScopeChanged(object sender,SelectionChangedEventArgs e)=>ActionRequested?.Invoke("OnTwitchCampaignScopeChanged",sender,e);
    private void OnTwitchLogout(object sender,RoutedEventArgs e)=>ActionRequested?.Invoke("OnTwitchLogout",sender,e);
    private void OnTwitchSelectChannel(object sender,RoutedEventArgs e)=>ActionRequested?.Invoke("OnTwitchSelectChannel",sender,e);
}
