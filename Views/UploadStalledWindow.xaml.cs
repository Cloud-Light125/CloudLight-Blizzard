using System.Windows;
using CloudLightBlizzard.Services;

namespace CloudLightBlizzard.Views;

public partial class UploadStalledWindow : Window
{
    public bool CancelUpload { get; private set; }
    public UploadStalledWindow() { InitializeComponent(); ThemeManager.Attach(this); }
    private void OnWait(object sender, RoutedEventArgs e) { CancelUpload = false; DialogResult = true; }
    private void OnCancel(object sender, RoutedEventArgs e) { CancelUpload = true; DialogResult = false; }
}
