using System.Runtime.InteropServices;
using System.Windows;

namespace CloudLightBlizzard;

public partial class SnapshotSaveErrorWindow : Window
{
    private readonly string _details;
    private readonly Action _openDirectory;

    public SnapshotSaveErrorWindow(Exception error, Action openDirectory)
    {
        InitializeComponent();
        ErrorMessage.Text = error.Message;
        _details = error.ToString();
        _openDirectory = openDirectory;
    }

    private void OnOpenDirectory(object sender, RoutedEventArgs e) => _openDirectory();

    private void OnCopy(object sender, RoutedEventArgs e)
    {
        try
        {
            Clipboard.SetText(_details);
            CopyButton.Content = "已复制";
        }
        catch (ExternalException)
        {
            CopyButton.Content = "复制失败，请重试";
        }
    }

    private void OnRetry(object sender, RoutedEventArgs e) => DialogResult = true;
}
