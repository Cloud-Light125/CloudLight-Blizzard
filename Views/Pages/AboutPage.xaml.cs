using System.Windows.Controls;
using System.Diagnostics;
using System.Windows;
using CloudLightBlizzard.ViewModels;

namespace CloudLightBlizzard.Views.Pages;

public partial class AboutPage : UserControl
{
    public AboutPage() => InitializeComponent();
    public void Initialize(MainViewModel main) => DataContext = main;

    private void OnOpenBilibiliSource(object sender, RoutedEventArgs e)
    {
        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = "https://github.com/mi0e/BiliBiliDropsMiner",
                UseShellExecute = true,
            });
        }
        catch { }
    }
}
