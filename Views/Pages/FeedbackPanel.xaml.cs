using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using CloudLightBlizzard.Models;
using CloudLightBlizzard.Services;
using CloudLightBlizzard.Views;

namespace CloudLightBlizzard.Views.Pages;

public partial class FeedbackPanel : UserControl
{
    private readonly FeedbackLogPackager _packager = new();
    private readonly DispatcherTimer _stallTimer = new() { Interval = TimeSpan.FromSeconds(5) };
    private AppSettings? _settings;
    private FeedbackService? _feedbackService;
    private CancellationTokenSource? _uploadCancellation;
    private DateTimeOffset _lastProgressAt;
    private long _lastProgressBytes;
    private bool _stallPromptOpen;
    private string? _reportId;
    private string? _submissionId;
    private string? _submissionFingerprint;
    private bool _serverProcessing;

    public FeedbackPanel()
    {
        InitializeComponent();
        _stallTimer.Tick += OnStallTimerTick;
    }

    public void Initialize(AppSettings settings, FeedbackService feedbackService)
    {
        _settings = settings;
        _feedbackService = feedbackService;
    }

    private void OnPreviewLogs(object sender, RoutedEventArgs e)
    {
        var logs = _packager.Preview();
        var detail = logs.Count == 0 ? "当前没有可提交的诊断日志。" : string.Join(Environment.NewLine,
            logs.Select(log => $"{log.ArchiveName}  ·  {FormatBytes(log.IncludedBytes)}"));
        MessageBox.Show(detail, "将提交的日志", MessageBoxButton.OK, MessageBoxImage.Information);
    }

    private async void OnSubmit(object sender, RoutedEventArgs e)
    {
        if (_settings is null || _feedbackService is null || _uploadCancellation is not null) return;
        var title = TitleBox.Text.Trim();
        var description = DescriptionBox.Text.Trim();
        if (title.Length == 0 || description.Length == 0)
        {
            MessageBox.Show("请填写错误主题和错误描述。", "提交反馈", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        FeedbackPackage? package = null;
        SetUploading(true);
        try
        {
            _uploadCancellation = new CancellationTokenSource();
            if (AttachLogsBox.IsChecked == true)
            {
                ProgressStatusText.Text = "正在整理日志……";
                try { package = await _packager.CreateAsync("用户反馈", _uploadCancellation.Token); }
                catch (FeedbackPackageTooLargeException)
                {
                    ShowFailure(FeedbackFailureKind.PayloadTooLarge);
                    return;
                }
                catch (OperationCanceledException)
                {
                    ShowFailure(FeedbackFailureKind.Cancelled);
                    return;
                }
                catch
                {
                    ShowFailure(FeedbackFailureKind.PackageFailed);
                    return;
                }
            }

            ProgressStatusText.Text = "正在上传反馈……";
            _lastProgressAt = DateTimeOffset.Now;
            _lastProgressBytes = 0;
            _stallPromptOpen = false;
            _stallTimer.Start();
            var progress = new Progress<FeedbackUploadProgress>(UpdateProgress);
            var version = Assembly.GetEntryAssembly()?.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "unknown";
            var fingerprint = string.Join("\u001f", title, description, ContactBox.Text.Trim(),
                AttachLogsBox.IsChecked == true ? "logs" : "no-logs");
            if (!string.Equals(_submissionFingerprint, fingerprint, StringComparison.Ordinal))
            {
                _submissionFingerprint = fingerprint;
                _submissionId = Guid.NewGuid().ToString("D");
            }
            var request = new FeedbackSubmitRequest(title, description, version,
                Environment.OSVersion.VersionString, ContactBox.Text.Trim(), _submissionId!, package?.FilePath);
            var result = await _feedbackService.SubmitAsync(request, progress, _uploadCancellation.Token);
            if (result.Success)
            {
                _reportId = result.ReportId;
                _submissionId = _submissionFingerprint = null;
                TitleBox.Clear();
                DescriptionBox.Clear();
                ShowSuccess(result.ReportId!);
            }
            else ShowFailure(result.Failure);
        }
        finally
        {
            _stallTimer.Stop();
            package?.Delete();
            _uploadCancellation?.Dispose();
            _uploadCancellation = null;
            SetUploading(false);
        }
    }

    private void UpdateProgress(FeedbackUploadProgress progress)
    {
        if (progress.BytesSent > _lastProgressBytes)
        {
            _lastProgressBytes = progress.BytesSent;
            _lastProgressAt = DateTimeOffset.Now;
        }
        UploadProgressBar.Value = progress.Percentage;
        UploadPercentText.Text = $"{progress.Percentage}%";
        UploadBytesText.Text = $"{FormatBytes(progress.BytesSent)} / {FormatBytes(progress.TotalBytes)}";
        if (progress.Stage == FeedbackUploadStage.ServerProcessing) EnterServerProcessing();
    }

    private void EnterServerProcessing()
    {
        if (_serverProcessing) return;
        _serverProcessing = true;
        _stallTimer.Stop();
        ProgressStatusText.Text = "正在提交反馈到 GitHub……";
        UploadProgressBar.IsIndeterminate = true;
        UploadPercentText.Visibility = UploadBytesText.Visibility = Visibility.Collapsed;
        CancelUploadButton.Visibility = Visibility.Collapsed;
    }

    private void OnStallTimerTick(object? sender, EventArgs e)
    {
        if (_uploadCancellation is null || _stallPromptOpen || _lastProgressBytes == 0 ||
            DateTimeOffset.Now - _lastProgressAt < TimeSpan.FromSeconds(45)) return;
        _stallPromptOpen = true;
        var dialog = new UploadStalledWindow { Owner = Window.GetWindow(this) };
        dialog.ShowDialog();
        _stallPromptOpen = false;
        if (dialog.CancelUpload) _uploadCancellation.Cancel();
        else _lastProgressAt = DateTimeOffset.Now;
    }

    private void OnCancelUpload(object sender, RoutedEventArgs e)
    {
        if (!_serverProcessing) _uploadCancellation?.Cancel();
    }

    private void SetUploading(bool uploading)
    {
        SubmitButton.IsEnabled = PreviewButton.IsEnabled = TitleBox.IsEnabled = DescriptionBox.IsEnabled =
            ContactBox.IsEnabled = AttachLogsBox.IsEnabled = !uploading;
        SubmitButton.Content = uploading ? "正在上传……" : "提交反馈";
        ProgressPanel.Visibility = uploading ? Visibility.Visible : Visibility.Collapsed;
        if (uploading)
        {
            _serverProcessing = false;
            ResultPanel.Visibility = Visibility.Collapsed;
            UploadProgressBar.Value = 0;
            UploadProgressBar.IsIndeterminate = false;
            UploadPercentText.Visibility = UploadBytesText.Visibility = CancelUploadButton.Visibility = Visibility.Visible;
            UploadPercentText.Text = "0%";
            UploadBytesText.Text = "0 B / --";
        }
    }

    private void ShowSuccess(string reportId)
    {
        ResultPanel.Visibility = Visibility.Visible;
        ResultTitleText.Text = "反馈已提交";
        ResultDetailText.Text = $"反馈编号：{reportId}\n日志已安全提交，可保存此编号用于后续沟通。";
        CopyReportButton.Visibility = Visibility.Visible;
        RetryButton.Visibility = CopyQqButton.Visibility = Visibility.Collapsed;
    }

    private void ShowFailure(FeedbackFailureKind failure)
    {
        var message = failure switch
        {
            FeedbackFailureKind.NetworkUnavailable => "暂时无法连接反馈服务器。",
            FeedbackFailureKind.ProxyUnavailable => "无法通过当前代理连接反馈服务器。",
            FeedbackFailureKind.ProxyAndDirectUnavailable => "代理和直连均无法连接服务器。",
            FeedbackFailureKind.InvalidProxy => "当前代理地址无效。",
            FeedbackFailureKind.Timeout => "反馈服务器连接超时。",
            FeedbackFailureKind.Cancelled => "反馈上传已取消。",
            FeedbackFailureKind.PackageFailed => "日志整理失败，你仍可以不附带日志重新提交。",
            FeedbackFailureKind.PayloadTooLarge => "日志文件过大，请清理旧日志或取消附加日志后重试。",
            FeedbackFailureKind.ServerUnavailable => "反馈服务暂时不可用。",
            FeedbackFailureKind.RateLimited => "反馈提交过于频繁，请稍后重试。",
            FeedbackFailureKind.GithubUnavailable => "反馈服务暂时无法提交到 GitHub。",
            FeedbackFailureKind.GithubTimeout => "反馈提交到 GitHub 超时。",
            FeedbackFailureKind.GithubConfiguration => "反馈服务配置异常，请稍后重试。",
            FeedbackFailureKind.GithubRateLimited => "GitHub 反馈服务暂时繁忙，请稍后重试。",
            FeedbackFailureKind.GithubAssetUploadFailed => "运行日志暂时无法提交到 GitHub。你也可以取消附加日志后重试。",
            FeedbackFailureKind.GithubIssueCreateFailed => "反馈服务暂时无法建立反馈记录。",
            _ => "反馈上传失败。",
        };
        ResultPanel.Visibility = Visibility.Visible;
        ResultTitleText.Text = message;
        ResultDetailText.Text = $"如果暂时无法提交反馈，也可以加入 QQ 群 {CloudServiceConfiguration.QqGroup} 反馈。";
        CopyReportButton.Visibility = Visibility.Collapsed;
        RetryButton.Visibility = failure == FeedbackFailureKind.Cancelled ? Visibility.Collapsed : Visibility.Visible;
        CopyQqButton.Visibility = Visibility.Visible;
    }

    private async void OnCopyReportId(object sender, RoutedEventArgs e)
    {
        if (_reportId is not null) await ClipboardService.CopyTextAsync(_reportId);
    }

    private async void OnCopyQqGroup(object sender, RoutedEventArgs e) =>
        await ClipboardService.CopyTextAsync(CloudServiceConfiguration.QqGroup);

    private static string FormatBytes(long bytes) => bytes >= 1024 * 1024
        ? $"{bytes / 1024d / 1024:0.0} MB" : bytes >= 1024 ? $"{bytes / 1024d:0.0} KB" : $"{bytes} B";
}
