using System.Windows;
using System.Windows.Controls;
using CloudLightBlizzard.Controls;
namespace CloudLightBlizzard.Views.Accounts;
public partial class AccountTile:UserControl
{
    public AccountTile()=>InitializeComponent();
    private void OnMore(object sender,RoutedEventArgs e)
    {
        if(sender is Button{ContextMenu:{} menu} button){menu.DataContext=button.DataContext;menu.PlacementTarget=button;menu.IsOpen=true;}
    }
    private void OnMenuAction(object sender,RoutedEventArgs e)
    {
        if(sender is MenuItem{Tag:string action} item&&ItemsControl.ItemsControlFromItemContainer(item) is ContextMenu{PlacementTarget:{} target})
            target.RaiseEvent(new WorkspaceActionEventArgs(action,item));
    }
}
