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
    private CancellationTokenSource? _updateCts;
    private bool _isUpdateRunning;
    private bool _installerStarted;

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
        var canOnlineUpdate = CanDownloadInstaller(result);
        OnlineUpdateButton.IsEnabled = canOnlineUpdate;
        if (!string.IsNullOrWhiteSpace(result.InstallerDownloadUrl) && !canOnlineUpdate)
        {
            InstallerValidationText.Text = "在线安装已禁用：更新服务未提供有效 SHA-256 摘要，请打开 Release 页面手动核对。";
            InstallerValidationText.Visibility = Visibility.Visible;
        }
    }

    public UpdateDialogAction Action { get; private set; } = UpdateDialogAction.Later;
    public bool SkipVersion => SkipVersionBox.IsChecked == true;
    public string? DownloadedInstallerPath { get; private set; }

    internal void MarkInstallerStarted()
    {
        _installerStarted = true;
        DownloadProgressText.Text = "安装程序已启动，正在退出 CloudLight Blizzard…";
    }

    private void OnDrag(object sender, MouseButtonEventArgs e)
    {
        if (e.ButtonState == MouseButtonState.Pressed && !_isUpdateRunning) DragMove();
    }

    private void OnOpenRelease(object sender, RoutedEventArgs e)
    {
        if (_isUpdateRunning) return;
        Action = UpdateDialogAction.OpenRelease;
        DialogResult = true;
    }

    private async void OnOnlineUpdate(object sender, RoutedEventArgs e)
    {
        if (_isUpdateRunning || !CanDownloadInstaller(_result)) return;

        var cts = new CancellationTokenSource();
        _updateCts = cts;
        var token = cts.Token;
        SetDownloading(true);
        DownloadProgressPanel.Visibility = Visibility.Visible;
        DownloadProgressBar.IsIndeterminate = _result.InstallerSize <= 0;
        DownloadProgressBar.Value = 0;
        DownloadProgressText.Text = "正在下载安装包…";

        try
        {
            var progress = new Progress<UpdateDownloadProgress>(RenderDownloadProgress);
            var path = await _downloader.DownloadInstallerAsync(
                _result, progress, token);
            token.ThrowIfCancellationRequested();
            DownloadedInstallerPath = path;
            DownloadProgressText.Text = "正在启动安装程序…";
            SetDownloading(false);
            Action = UpdateDialogAction.InstallDownloaded;
            DialogResult = true;
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
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
        finally
        {
            if (ReferenceEquals(_updateCts, cts))
                _updateCts = null;
            cts.Dispose();
        }
    }

    private void RenderDownloadProgress(UpdateDownloadProgress value)
    {
        if (value.Phase == UpdateDownloadPhase.WaitingRetry)
        {
            DownloadProgressBar.IsIndeterminate = true;
            var delay = value.RetryDelay is { } retry ? $"{Math.Max(1, (int)Math.Ceiling(retry.TotalSeconds))} 秒后" : "稍后";
            DownloadProgressText.Text = $"下载中断，{delay}重试（{value.RetryAttempt}/{value.MaxRetries}）";
            return;
        }
        if (value.Phase == UpdateDownloadPhase.Verifying)
        {
            DownloadProgressBar.IsIndeterminate = false;
            DownloadProgressBar.Value = value.Percentage ?? 100;
            DownloadProgressText.Text = "正在校验安装包…";
            return;
        }

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
        _isUpdateRunning = value;
        LaterButton.IsEnabled = !value;
        CloseButton.IsEnabled = !value;
        SkipVersionBox.IsEnabled = !value;
        OpenReleaseButton.IsEnabled = !value && !string.IsNullOrWhiteSpace(_result.ReleaseUrl);
        OnlineUpdateButton.IsEnabled = !value && CanDownloadInstaller(_result);
        CancelDownloadButton.Visibility = value ? Visibility.Visible : Visibility.Collapsed;
        CancelDownloadButton.IsEnabled = value;
        if (value) OnlineUpdateButton.Content = "正在下载…";
        else OnlineUpdateButton.Content = "在线更新";
    }

    private void OnCancelDownload(object sender, RoutedEventArgs e)
    {
        if (!_isUpdateRunning) return;
        DownloadProgressText.Text = "正在取消下载，已保留可安全续传的断点…";
        CancelDownloadButton.IsEnabled = false;
        _updateCts?.Cancel();
    }

    private void OnLater(object sender, RoutedEventArgs e)
    {
        if (_isUpdateRunning) return;
        Action = UpdateDialogAction.Later;
        DialogResult = false;
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        if (_isUpdateRunning)
        {
            _updateCts?.Cancel();
            e.Cancel = true;
            return;
        }
        base.OnClosing(e);
    }

    protected override void OnClosed(EventArgs e)
    {
        if (_isUpdateRunning && !_installerStarted)
            _updateCts?.Cancel();
        base.OnClosed(e);
    }

    private static bool CanDownloadInstaller(UpdateCheckResult result) =>
        !string.IsNullOrWhiteSpace(result.InstallerDownloadUrl) &&
        UpdateService.IsValidSha256Digest(result.InstallerDigest);
}
