using System.Windows;

namespace CloudLightBlizzard.Views;

public partial class SoopNoticeWindow : Window
{
    public SoopNoticeWindow(string title, string message, string confirmText)
    {
        InitializeComponent();
        Title = title;
        HeadingText.Text = title;
        MessageText.Text = message;
        ConfirmButton.Content = confirmText;
    }

    private void OnConfirm(object sender, RoutedEventArgs e) => DialogResult = true;
    private void OnCancel(object sender, RoutedEventArgs e) => DialogResult = false;
}
