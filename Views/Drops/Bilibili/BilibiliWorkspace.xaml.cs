using System.Windows;
using System.Windows.Controls;
namespace CloudLightBlizzard.Views.Drops.Bilibili;
public partial class BilibiliWorkspace:UserControl
{
    public event Action<string,object,RoutedEventArgs>? ActionRequested;
    public BilibiliWorkspace()=>InitializeComponent();
    private void OnAutoStartChanged(object sender,RoutedEventArgs e)=>ActionRequested?.Invoke("OnAutoStartChanged",sender,e);
    private void OnNotifierDraftChanged(object sender,RoutedEventArgs e)=>ActionRequested?.Invoke("OnNotifierDraftChanged",sender,e);
    private void OnOpenProxySettings(object sender,RoutedEventArgs e)=>ActionRequested?.Invoke("OnOpenProxySettings",sender,e);
}
