using System.Windows;
using System.Windows.Controls;
namespace CloudLightBlizzard.Views.Drops.YouTube;
public partial class YouTubeWorkspace:UserControl
{
    public event Action<string,object,RoutedEventArgs>? ActionRequested;
    public YouTubeWorkspace()=>InitializeComponent();
    private void OnBrowseYouTubeBrowser(object sender,RoutedEventArgs e)=>ActionRequested?.Invoke("OnBrowseYouTubeBrowser",sender,e);
    private void OnYouTubeAddChannel(object sender,RoutedEventArgs e)=>ActionRequested?.Invoke("OnYouTubeAddChannel",sender,e);
    private void OnYouTubeAddProfile(object sender,RoutedEventArgs e)=>ActionRequested?.Invoke("OnYouTubeAddProfile",sender,e);
    private void OnYouTubeBrowserChanged(object sender,SelectionChangedEventArgs e)=>ActionRequested?.Invoke("OnYouTubeBrowserChanged",sender,e);
    private void OnYouTubeDeleteChannel(object sender,RoutedEventArgs e)=>ActionRequested?.Invoke("OnYouTubeDeleteChannel",sender,e);
    private void OnYouTubeDeleteProfile(object sender,RoutedEventArgs e)=>ActionRequested?.Invoke("OnYouTubeDeleteProfile",sender,e);
    private void OnYouTubeLogin(object sender,RoutedEventArgs e)=>ActionRequested?.Invoke("OnYouTubeLogin",sender,e);
    private void OnYouTubeModeChanged(object sender,SelectionChangedEventArgs e)=>ActionRequested?.Invoke("OnYouTubeModeChanged",sender,e);
    private void OnYouTubeToggleChannel(object sender,RoutedEventArgs e)=>ActionRequested?.Invoke("OnYouTubeToggleChannel",sender,e);
}
