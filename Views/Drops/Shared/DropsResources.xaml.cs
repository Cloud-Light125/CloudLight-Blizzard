using System.Windows;
using System.Windows.Controls;
using CloudLightBlizzard.Controls;
namespace CloudLightBlizzard.Views.Drops.Shared;
public partial class DropsResources:ResourceDictionary
{
    public DropsResources()=>InitializeComponent();
    private void OnClaimTwitchDrop(object sender,RoutedEventArgs e){if(sender is UIElement element)element.RaiseEvent(new WorkspaceActionEventArgs("legacy:OnClaimTwitchDrop",sender));}
    private void OnQuickStartAction(object sender,RoutedEventArgs e){if(sender is UIElement element)element.RaiseEvent(new WorkspaceActionEventArgs("legacy:OnQuickStartAction",sender));}
}
