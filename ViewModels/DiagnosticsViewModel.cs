using System.Collections.ObjectModel;
using System.IO;
using System.Windows;
using CloudLightBlizzard.Services.Diagnostics;

namespace CloudLightBlizzard.ViewModels;

public sealed class DiagnosticsViewModel : ObservableObject, IDisposable
{
    private readonly MainViewModel _main;
    private readonly DiagnosticService _service;
    private readonly SynchronizationContext? _context;
    private CancellationTokenSource? _runCancellation;
    private DiagnosticRunReport? _report;
    private bool _isRunning;
    private string _statusText = "尚未运行诊断。建议遇到区服、Drops 或更新问题时先执行一次。";
    private string _progressText = "准备就绪";
    private int _completed;
    private int _total;

    public DiagnosticsViewModel(MainViewModel main)
    {
        _main = main ?? throw new ArgumentNullException(nameof(main));
        _service = new DiagnosticService(main);
        _context = SynchronizationContext.Current;
        _service.ProgressChanged += OnProgress;
    }

    public ObservableCollection<DiagnosticCheck> Checks { get; } = new();
    public DiagnosticRunReport? Report { get => _report; private set => Set(ref _report, value); }
    public bool IsRunning { get => _isRunning; private set { Set(ref _isRunning, value); Raise(nameof(NotRunning)); Raise(nameof(CanExport)); } }
    public bool NotRunning => !IsRunning;
    public bool CanExport => !IsRunning && Report is not null;
    public string StatusText { get => _statusText; private set => Set(ref _statusText, value); }
    public string ProgressText { get => _progressText; private set => Set(ref _progressText, value); }
    public int Completed { get => _completed; private set { Set(ref _completed, value); Raise(nameof(ProgressPercent)); } }
    public int Total { get => _total; private set { Set(ref _total, value); Raise(nameof(ProgressPercent)); } }
    public double ProgressPercent => Total > 0 ? Math.Clamp(Completed * 100d / Total, 0, 100) : 0;
    public string SummaryText => Report is null ? "" :
        $"{Report.OverallText} · 正常 {Report.HealthyCount} · 警告 {Report.WarningCount} · 错误 {Report.ErrorCount}";

    public async Task StartAsync()
    {
        if (IsRunning) return;
        var cts = new CancellationTokenSource();
        _runCancellation = cts;
        IsRunning = true;
        Report = null;
        Checks.Clear();
        Completed = 0;
        Total = 0;
        StatusText = "正在检查 CloudLight Blizzard、Battle.net、网络、更新、Drops 与区服数据状态。";
        ProgressText = "正在准备诊断…";
        try
        {
            var report = await _service.RunAsync(cts.Token);
            Report = report;
            Checks.Clear();
            foreach (var check in report.Checks) Checks.Add(check);
            Completed = report.Checks.Count;
            Total = Math.Max(Total, report.Checks.Count);
            ProgressText = report.Cancelled
                ? $"已完成 {Completed} / {Total} · 可重新开始"
                : $"已完成 {Completed} / {Total}";
            StatusText = report.Cancelled ? "诊断已取消，已保留已完成项目。" : report.OverallText;
            Raise(nameof(SummaryText));
        }
        catch (OperationCanceledException) when (cts.IsCancellationRequested)
        {
            StatusText = "诊断已取消，可以重新开始。";
            ProgressText = $"已完成 {Completed} / {Total}";
        }
        catch (Exception ex)
        {
            StatusText = "诊断未能完成，请查看日志后重试。";
            ProgressText = ex.Message;
        }
        finally
        {
            IsRunning = false;
            if (ReferenceEquals(_runCancellation, cts)) _runCancellation = null;
            cts.Dispose();
            Raise(nameof(SummaryText));
        }
    }

    public void Cancel()
    {
        if (IsRunning) _runCancellation?.Cancel();
    }

    public async Task<string?> ExportAsync()
    {
        if (IsRunning || Report is null) return null;
        var cts = new CancellationTokenSource();
        try
        {
            StatusText = "正在生成脱敏诊断包…";
            var path = await _service.ExportBundleAsync(Report, cts.Token);
            StatusText = $"诊断包已生成：{Path.GetFileName(path)}";
            return path;
        }
        catch (Exception ex)
        {
            StatusText = "诊断包生成失败，请稍后重试。";
            ProgressText = ex.Message;
            return null;
        }
        finally { cts.Dispose(); }
    }

    public string CopyText() => Report is null ? "尚未生成诊断结果。" : _service.BuildCopyText(Report);

    private void OnProgress(DiagnosticProgress progress)
    {
        void Apply()
        {
            Completed = progress.Completed;
            Total = progress.Total;
            if (progress.Current is { } current)
            {
                ProgressText = progress.IsCompleted
                    ? $"已完成 {progress.Completed} / {progress.Total}：{current.Name}"
                    : $"正在检查 {Math.Min(progress.Completed + 1, progress.Total)} / {progress.Total}：{current.Name}";
                var index = progress.IsCompleted ? Math.Max(0, progress.Completed - 1) : progress.Completed;
                if (index < Checks.Count) Checks[index] = current;
                else if (index == Checks.Count) Checks.Add(current);
            }
        }
        if (_context is not null) _context.Post(_ => Apply(), null); else Apply();
    }

    public void Dispose()
    {
        _service.ProgressChanged -= OnProgress;
        // Cancellation is synchronous, but disposal belongs to StartAsync's finally block.
        // Keeping the CTS alive until the worker observes cancellation avoids a race during
        // application shutdown (Cancel != Dispose).
        _runCancellation?.Cancel();
    }
}
