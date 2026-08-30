using System.ComponentModel;
using System.Windows;
using System.Windows.Input;
using CloudLightBlizzard.Models;
using CloudLightBlizzard.Services;

namespace CloudLightBlizzard.Views;

public enum UpdateDialogAction
{
    Later,
    OpenRelease,
    InstallDownloaded,
}

public partial class UpdateDialog : Window
{
    private readonly UpdateCheckResult _result;
    private readonly UpdateDownloadService _downloader;
    private CancellationTokenSource? _downloadCancellation;
    private bool _isDownloading;

    public UpdateDialog(UpdateCheckResult result, UpdateDownloadService downloader)
    {
        _result = result ?? throw new ArgumentNullException(nameof(result));
        _downloader = downloader ?? throw new ArgumentNullException(nameof(downloader));
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
        OnlineUpdateButton.IsEnabled = !string.IsNullOrWhiteSpace(result.InstallerDownloadUrl);
    }

    public UpdateDialogAction Action { get; private set; } = UpdateDialogAction.Later;
    public bool SkipVersion => SkipVersionBox.IsChecked == true;
    public string? DownloadedInstallerPath { get; private set; }

    private void OnDrag(object sender, MouseButtonEventArgs e)
    {
        if (e.ButtonState == MouseButtonState.Pressed && !_isDownloading) DragMove();
    }

    private void OnOpenRelease(object sender, RoutedEventArgs e)
    {
        if (_isDownloading) return;
        Action = UpdateDialogAction.OpenRelease;
        DialogResult = true;
    }

    private async void OnOnlineUpdate(object sender, RoutedEventArgs e)
    {
        if (_isDownloading || string.IsNullOrWhiteSpace(_result.InstallerDownloadUrl)) return;

        _downloadCancellation?.Dispose();
        _downloadCancellation = new CancellationTokenSource();
        SetDownloading(true);
        DownloadProgressPanel.Visibility = Visibility.Visible;
        DownloadProgressBar.IsIndeterminate = _result.InstallerSize <= 0;
        DownloadProgressBar.Value = 0;
        DownloadProgressText.Text = "正在下载安装包…";

        try
        {
            var progress = new Progress<UpdateDownloadProgress>(RenderDownloadProgress);
            var path = await _downloader.DownloadInstallerAsync(
                _result, progress, _downloadCancellation.Token);
            DownloadedInstallerPath = path;
            Action = UpdateDialogAction.InstallDownloaded;
            DialogResult = true;
        }
        catch (OperationCanceledException) when (_downloadCancellation.IsCancellationRequested)
        {
            DownloadProgressText.Text = "下载已取消。";
            SetDownloading(false);
        }
        catch (Exception ex)
        {
            DownloadProgressText.Text = "在线更新下载失败。";
            MessageBox.Show(ex.Message, "在线更新失败", MessageBoxButton.OK, MessageBoxImage.Warning);
            SetDownloading(false);
        }
    }

    private void RenderDownloadProgress(UpdateDownloadProgress value)
    {
        if (value.Percentage is { } percentage)
        {
            DownloadProgressBar.IsIndeterminate = false;
            DownloadProgressBar.Value = percentage;
            var total = value.TotalBytes is > 0
                ? $" / {UpdateDownloadService.FormatBytes(value.TotalBytes.Value)}"
                : "";
            DownloadProgressText.Text =
                $"正在下载：{percentage}% · {UpdateDownloadService.FormatBytes(value.BytesReceived)}{total}";
        }
        else
        {
            DownloadProgressBar.IsIndeterminate = true;
            DownloadProgressText.Text =
                $"正在下载：{UpdateDownloadService.FormatBytes(value.BytesReceived)}";
        }
    }

    private void SetDownloading(bool value)
    {
        _isDownloading = value;
        LaterButton.IsEnabled = !value;
        CloseButton.IsEnabled = !value;
        SkipVersionBox.IsEnabled = !value;
        OpenReleaseButton.IsEnabled = !value && !string.IsNullOrWhiteSpace(_result.ReleaseUrl);
        OnlineUpdateButton.IsEnabled = !value && !string.IsNullOrWhiteSpace(_result.InstallerDownloadUrl);
        if (value) OnlineUpdateButton.Content = "正在下载…";
        else OnlineUpdateButton.Content = "在线更新";
    }

    private void OnLater(object sender, RoutedEventArgs e)
    {
        if (_isDownloading) return;
        Action = UpdateDialogAction.Later;
        DialogResult = false;
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        if (_isDownloading)
        {
            e.Cancel = true;
            return;
        }
        base.OnClosing(e);
    }

    protected override void OnClosed(EventArgs e)
    {
        _downloadCancellation?.Cancel();
        _downloadCancellation?.Dispose();
        base.OnClosed(e);
    }
}