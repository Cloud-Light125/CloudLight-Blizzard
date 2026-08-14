using System.Windows;
using System.Windows.Input;
using CloudLightBlizzard.Models;
using CloudLightBlizzard.Services;

namespace CloudLightBlizzard.Views;

public enum UpdateDialogAction
{
    Later,
    OpenRelease,
}

public partial class UpdateDialog : Window
{
    public UpdateDialog(UpdateCheckResult result)
    {
        InitializeComponent();
        ThemeManager.Attach(this);
        IntroText.Text = $"CloudLight Blizzard {result.LatestVersion} 已发布。";
        CurrentVersionText.Text = result.CurrentVersion;
        LatestVersionText.Text = result.LatestVersion;
        PublishedPanel.Visibility = result.PublishedAt.HasValue ? Visibility.Visible : Visibility.Collapsed;
        PublishedText.Text = result.PublishedAt?.ToLocalTime().ToString("yyyy-MM-dd") ?? "";
        ReleaseNotesPanel.Visibility = string.IsNullOrWhiteSpace(result.ReleaseNotes)
            ? Visibility.Collapsed : Visibility.Visible;
        ReleaseNotesText.Text = result.ReleaseNotes;
        OpenReleaseButton.IsEnabled = !string.IsNullOrWhiteSpace(result.ReleaseUrl);
    }

    public UpdateDialogAction Action { get; private set; } = UpdateDialogAction.Later;
    public bool SkipVersion => SkipVersionBox.IsChecked == true;

    private void OnDrag(object sender, MouseButtonEventArgs e)
    {
        if (e.ButtonState == MouseButtonState.Pressed) DragMove();
    }

    private void OnOpenRelease(object sender, RoutedEventArgs e)
    {
        Action = UpdateDialogAction.OpenRelease;
        DialogResult = true;
    }

    private void OnLater(object sender, RoutedEventArgs e)
    {
        Action = UpdateDialogAction.Later;
        DialogResult = false;
    }
}
