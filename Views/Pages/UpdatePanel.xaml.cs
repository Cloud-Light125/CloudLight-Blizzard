using System.Windows;
using System.Windows.Controls;
using CloudLightBlizzard.Models;
using CloudLightBlizzard.Services;
using CloudLightBlizzard.ViewModels;
namespace CloudLightBlizzard.Views.Pages;

/// <summary>The single update surface, sharing MainViewModel's check and download services.</summary>
public partial class UpdatePanel : UserControl
{
    private MainViewModel? _vm;
    private UpdateCheckResult? _availableUpdate;
    private bool _isDownloadingUpdate, _isUpdateRunning, _installerStarted;
    private CancellationTokenSource? _updateCts, _checkCts;
    private string? _operationStatus;
    public UpdatePanel()
    {
        InitializeComponent();
        Loaded += (_,_) => { if (_vm is not null) _vm.UpdateChecks.CheckingChanged += OnUpdateCheckingChanged; RefreshUpdateInfo(); };
        Unloaded += (_,_) => { if (_vm is not null) _vm.UpdateChecks.CheckingChanged -= OnUpdateCheckingChanged; CancelUpdateDownload(); };
    }
    public void Initialize(MainViewModel vm)
    {
        if (_vm is not null && IsLoaded) _vm.UpdateChecks.CheckingChanged -= OnUpdateCheckingChanged;
        _vm = vm;
        if (IsLoaded) vm.UpdateChecks.CheckingChanged += OnUpdateCheckingChanged;
        RefreshUpdateInfo();
    }
    private void OnCancelDownload(object sender, RoutedEventArgs e) => CancelUpdateDownload();
    internal void CancelUpdateDownload()
    {
        if (_isUpdateRunning && !_installerStarted)
            _updateCts?.Cancel();
    }

    public void RefreshUpdateInfo()
    {
        if (_vm == null) return;
        CurrentVersionText.Text = $"当前版本 {_vm.UpdateChecks.CurrentVersion}";
        var skipped = UpdateService.NormalizeReleaseVersion(_vm.Settings.SkippedUpdateVersion);
        SkippedUpdatePanel.Visibility = string.IsNullOrWhiteSpace(skipped)
            ? Visibility.Collapsed : Visibility.Visible;
        SkippedVersionText.Text = skipped ?? "";
        var lastCheck = _vm.Settings.LastUpdateCheckAt is { } check
            ? check.ToLocalTime().ToString("yyyy-MM-dd HH:mm") : "暂无";
        var lastUpdate = _vm.Settings.LastSuccessfulUpdateAt is { } updated &&
                         !string.IsNullOrWhiteSpace(_vm.Settings.LastSuccessfulUpdateFrom) &&
                         !string.IsNullOrWhiteSpace(_vm.Settings.LastSuccessfulUpdateTo)
            ? $"最近一次确认升级：{_vm.Settings.LastSuccessfulUpdateFrom} → {_vm.Settings.LastSuccessfulUpdateTo} · {updated.ToLocalTime():yyyy-MM-dd HH:mm}"
            : "最近一次安装器启动：暂无记录";
        UpdateHistoryText.Text = $"{lastUpdate}\n最近一次检查：{lastCheck}";
        UpdateFailureText.Text = _operationStatus ??
            (_vm.Settings.LastUpdateInstallStatus ??
             (_vm.UpdateChecks.LastResult is null ? _vm.Settings.LastUpdateFailure ?? "" : ""));
        UpdateFailureText.Visibility = UpdateFailureText.Text.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
        UpdateCheckingState();
    }

    internal void FocusUpdateSection()
    {
        Dispatcher.BeginInvoke(new Action(() =>
        {
            UpdateSettingsCard.BringIntoView();
            CheckUpdateButton.Focus();
        }), System.Windows.Threading.DispatcherPriority.Loaded);
    }

    private async void OnCheckUpdate(object sender, RoutedEventArgs e)
    {
        if (_vm == null || _vm.UpdateChecks.IsChecking || _isDownloadingUpdate) return;
        using var cancellation = new CancellationTokenSource();
        _checkCts = cancellation;
        _operationStatus = null;
        try
        {
            var outcome = await _vm.UpdateChecks.CheckAsync(UpdateCheckMode.Manual, cancellation.Token);
            if (outcome.Kind == UpdateCheckOutcomeKind.UpdateAvailable && outcome.Result is { } result)
            {
                _availableUpdate = result;
                UpdateAvailablePanel.Visibility = Visibility.Visible;
                UpdateAvailableText.Text = $"发现新版本 {result.LatestVersion}";
                UpdateDownloadPanel.Visibility = Visibility.Collapsed;
                OpenUpdateLinkButton.IsEnabled = !string.IsNullOrWhiteSpace(result.ReleaseUrl);
                var canDownload = CanDownloadInstaller(result);
                OnlineUpdateButton.IsEnabled = canDownload;
                InstallerValidationText.Visibility = canDownload ? Visibility.Collapsed : Visibility.Visible;
                InstallerValidationText.Text = UpdateDownloadService.InstallerUnavailableText(result);
            }
            else
            {
                ClearAvailableUpdate();

            }
        }
        catch (OperationCanceledException)
        {
            _operationStatus = "已取消检查更新。";
        }
        finally
        {
            _checkCts = null;
            RefreshUpdateInfo();
        }
    }

    private void OnCancelCheck(object sender, RoutedEventArgs e) => _checkCts?.Cancel();

    private void OnOpenProjectLink(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: string url } && Window.GetWindow(this) is MainWindow main)
            main.OpenUpdateRelease(url);
    }

    private void OnOpenUpdateLink(object sender, RoutedEventArgs e)
    {
        if (_isDownloadingUpdate || _availableUpdate is null) return;
        if (Window.GetWindow(this) is MainWindow mainWindow)
            mainWindow.OpenUpdateRelease(_availableUpdate.ReleaseUrl);
    }

    private async void OnOnlineUpdate(object sender, RoutedEventArgs e)
    {
        if (_vm == null || _availableUpdate is null || _isDownloadingUpdate ||
            !CanDownloadInstaller(_availableUpdate))
            return;

        var update = _availableUpdate;
        var cts = new CancellationTokenSource();
        _updateCts = cts;
        var token = cts.Token;
        _isUpdateRunning = true;
        _installerStarted = false;
        _isDownloadingUpdate = true;
        UpdateDownloadPanel.Visibility = Visibility.Visible;
        UpdateDownloadProgressBar.Value = 0;
        UpdateDownloadProgressBar.IsIndeterminate = update.InstallerSize <= 0;
        UpdateDownloadText.Text = "正在下载安装包…";
        UpdateCheckingState();

        try
        {
            var progress = new Progress<UpdateDownloadProgress>(RenderUpdateDownloadProgress);
            var path = await _vm.UpdateDownloader.DownloadInstallerAsync(
                update, progress, token);
            token.ThrowIfCancellationRequested();
            UpdateDownloadText.Text = "正在启动安装程序…";
            _isUpdateRunning = false;
            if (Window.GetWindow(this) is MainWindow mainWindow &&
                await mainWindow.InstallDownloadedUpdate(path, () =>
                {
                    _installerStarted = true;
                    UpdateDownloadText.Text = "安装程序已启动，正在退出 CloudLight Blizzard…";
                }, update))
                return;

            UpdateDownloadText.Text = $"安装阶段失败 · 更新安装程序未启动。{UpdateService.ManualDownloadText}";
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
            UpdateDownloadText.Text = "下载已取消。";
        }
        catch (Exception ex)
        {
            UpdateDownloadText.Text = $"{(_isUpdateRunning ? "下载或校验失败" : "安装阶段失败")} · {ex.Message} {UpdateService.ManualDownloadText}";
        }
        finally
        {
            UpdateDownloadProgressBar.IsIndeterminate = false;
            _isUpdateRunning = false;
            if (ReferenceEquals(_updateCts, cts))
                _updateCts = null;
            cts.Dispose();
            if (!_installerStarted && !Dispatcher.HasShutdownStarted)
            {
                _isDownloadingUpdate = false;
                UpdateCheckingState();
            }
        }
    }

    private void RenderUpdateDownloadProgress(UpdateDownloadProgress value)
    {
        if (!_isUpdateRunning) return;
        if (value.Phase == UpdateDownloadPhase.WaitingRetry)
        {
            UpdateDownloadProgressBar.IsIndeterminate = true;
            var delay = value.RetryDelay is { } retry ? $"{Math.Max(1, (int)Math.Ceiling(retry.TotalSeconds))} 秒后" : "稍后";
            UpdateDownloadText.Text = $"下载中断，{delay}重试（{value.RetryAttempt}/{value.MaxRetries}）";
            return;
        }
        if (value.Phase == UpdateDownloadPhase.Verifying)
        {
            UpdateDownloadProgressBar.IsIndeterminate = false;
            UpdateDownloadProgressBar.Value = value.Percentage ?? 100;
            UpdateDownloadText.Text = "正在校验安装包…";
            return;
        }

        if (value.Percentage is { } percentage)
        {
            UpdateDownloadProgressBar.IsIndeterminate = false;
            UpdateDownloadProgressBar.Value = percentage;
            var total = value.TotalBytes is > 0
                ? $" / {UpdateDownloadService.FormatBytes(value.TotalBytes.Value)}"
                : "";
            UpdateDownloadText.Text =
                $"正在下载：{percentage}% · {UpdateDownloadService.FormatBytes(value.BytesReceived)}{total}{value.SpeedText}";
        }
        else
        {
            UpdateDownloadProgressBar.IsIndeterminate = true;
            UpdateDownloadText.Text =
                $"正在下载：{UpdateDownloadService.FormatBytes(value.BytesReceived)}{value.SpeedText}";
        }
    }

    private void ClearAvailableUpdate()
    {
        _availableUpdate = null;
        UpdateAvailablePanel.Visibility = Visibility.Collapsed;
        UpdateDownloadPanel.Visibility = Visibility.Collapsed;
        InstallerValidationText.Visibility = Visibility.Collapsed;
    }

    private void OnRestoreSkippedUpdate(object sender, RoutedEventArgs e)
    {
        _vm?.UpdateChecks.RestoreSkippedVersion();
        RefreshUpdateInfo();
    }

    internal void RenderCheckStatus(UpdateCheckResult? result, bool checking)
    {
        UpdateStatusText.Text = checking ? "正在检查最新版本…" : result?.CheckStatusText ?? "启动后自动检查正式更新。";
        if (!checking && result?.Status == UpdateCheckResultStatus.Failed)
            UpdateStatusText.Text += "\n自动检查更新失败，" + UpdateService.ManualDownloadText;
    }

    private void OnUpdateCheckingChanged()
    {
        if (Dispatcher.HasShutdownStarted) return;
        Dispatcher.BeginInvoke(RefreshUpdateInfo);
    }

    private void UpdateCheckingState()
    {
        if (_vm == null) return;
        RenderCheckStatus(_vm.UpdateChecks.LastResult, _vm.UpdateChecks.IsChecking);
        CancelCheckButton.Visibility = _vm.UpdateChecks.IsChecking && _checkCts is not null ? Visibility.Visible : Visibility.Collapsed;
        CancelDownloadButton.Visibility = _isUpdateRunning ? Visibility.Visible : Visibility.Collapsed;
        CheckUpdateButton.IsEnabled = !_vm.UpdateChecks.IsChecking && !_isDownloadingUpdate;
        CheckUpdateButton.Content = _vm.UpdateChecks.IsChecking ? "正在检查…" : "检查更新";
        if (_availableUpdate is not null)
        {
            OpenUpdateLinkButton.IsEnabled = !_isDownloadingUpdate &&
                !string.IsNullOrWhiteSpace(_availableUpdate.ReleaseUrl);
            OnlineUpdateButton.IsEnabled = !_isDownloadingUpdate &&
                CanDownloadInstaller(_availableUpdate);
            OnlineUpdateButton.Content = _isDownloadingUpdate ? "正在下载…" : "在线更新";
        }
    }

    private static bool CanDownloadInstaller(UpdateCheckResult result) =>
        !string.IsNullOrWhiteSpace(result.InstallerDownloadUrl) &&
        UpdateService.IsValidSha256Digest(result.InstallerDigest);
}
