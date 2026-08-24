using System.Windows;
using System.Windows.Controls;

namespace CloudLightBlizzard.Controls;

public partial class AnnouncementBell : UserControl
{
    public AnnouncementBell() => InitializeComponent();

    private async void OnOpenAnnouncements(object sender, RoutedEventArgs e)
    {
        if (Window.GetWindow(this) is MainWindow window)
            await window.OpenAnnouncementsAsync();
    }
}
