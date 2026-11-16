using System.Windows;
using System.Windows.Controls;
using CloudLightBlizzard.ViewModels;
using CloudLightBlizzard.Models;
using CloudLightBlizzard.Services.Drops;
namespace CloudLightBlizzard.Views.Dashboard;
public partial class DashboardView:UserControl
{
    public DashboardView()=>InitializeComponent();
    public void Initialize(MainViewModel main,DropsViewModel drops){DataContext=main;PlatformStatusList.ItemsSource=drops.VisualPlatforms;}
    internal void UpdateAnnouncements(IReadOnlyList<Announcement> values)=>LatestAnnouncements.ItemsSource=values.Where(a=>a.Enabled).OrderByDescending(a=>a.PublishedAt).Take(3).ToArray();
    private void OnNavigate(object sender,RoutedEventArgs e){if(sender is Button{Tag:string section}&&Window.GetWindow(this) is MainWindow main)main.NavigateTo(section);}
    private void OnPlatform(object sender,RoutedEventArgs e){if(sender is Button{Tag:DropsPlatform platform}&&Window.GetWindow(this) is MainWindow main)main.OpenDropsPlatform(platform);}
    private async void OnAnnouncements(object sender,RoutedEventArgs e){if(Window.GetWindow(this) is MainWindow main)await main.OpenAnnouncementsAsync();}
}
