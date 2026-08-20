using System.Windows;

namespace CloudLightBlizzard.Views;

public partial class PlatformErrorWindow : Window
{
    public PlatformErrorWindow(string title, string message)
    {
        InitializeComponent();
        Title = title;
        MessageText.Text = message;
    }

    private void OnViewLogs(object sender, RoutedEventArgs e)
    {
        DialogResult = true;
    }
}
