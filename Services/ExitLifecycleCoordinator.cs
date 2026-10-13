namespace CloudLightBlizzard.Services;

public enum ExitCleanupStage { AccountOperationDrain, DropsPageCleanup, DropsHostShutdown, SettingsPersistence, NonCriticalCleanup }
public enum ExitCleanupState { NotStarted, Cleaning, Completed, Failed, TimedOut }
public sealed record ExitStageResult(ExitCleanupStage Stage, bool Critical, Exception? Error);

/// <summary>
/// Coordinates the two-phase WPF exit path.
///
/// WPF raises <c>Window.Closing</c> synchronously.  A caller must cancel
/// that first notification and run asynchronous cleanup before asking the
/// window to close again.  This type provides the one-shot gate for that
/// hand-off and a cooperative cancellation budget. File transactions must
/// finish/roll back rather than be abandoned; worker waits have their own bounds.
/// </summary>
public sealed class ExitLifecycleCoordinator
{
    private readonly object _sync = new();
    private readonly TimeSpan _budget;
    private Task? _runTask;
    private bool _completed;
    private Exception? _error;
    private readonly List<ExitStageResult> _stages = [];
    private bool _timedOut;

    public ExitCleanupState State
    {
        get { lock (_sync) return _completed ? (_timedOut ? ExitCleanupState.TimedOut :
            _error is not null ? ExitCleanupState.Failed : ExitCleanupState.Completed) :
            _runTask is null ? ExitCleanupState.NotStarted : ExitCleanupState.Cleaning; }
    }
    public IReadOnlyList<ExitStageResult> Stages { get { lock (_sync) return _stages.ToArray(); } }
    public bool InstallerReady
    {
        get
        {
            lock (_sync) return _completed && !_timedOut && _error is null &&
                new[] { ExitCleanupStage.AccountOperationDrain, ExitCleanupStage.DropsPageCleanup,
                    ExitCleanupStage.DropsHostShutdown, ExitCleanupStage.SettingsPersistence }
                .All(stage => _stages.Any(result => result.Stage == stage && result.Critical && result.Error is null));
        }
    }

    // Continue other cleanup stages even after a failure, but never lose critical failure information.
    public async Task RunStageAsync(ExitCleanupStage stage, Func<Task> cleanup, bool critical = true)
    {
        Exception? error = null;
        try { await cleanup(); }
        catch (Exception ex) { error = ex; if (critical) SetError(ex); }
        lock (_sync) _stages.Add(new(stage, critical, error));
    }

    public ExitLifecycleCoordinator(TimeSpan? budget = null)
    {
        _budget = budget.GetValueOrDefault(TimeSpan.FromSeconds(60));
        if (_budget <= TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(budget));
    }

    public bool IsStarted
    {
        get { lock (_sync) return _runTask is not null; }
    }

    public bool IsCompleted
    {
        get { lock (_sync) return _completed; }
    }

    /// <summary>Gets the first cleanup error, if one was observed.</summary>
    public Exception? Error
    {
        get { lock (_sync) return _error; }
    }

    /// <summary>
    /// Starts cleanup once.  Repeated calls return the original task and do
    /// not invoke the callback again.  The callback receives a cancellation
    /// token that is cancelled at the lifecycle budget; individual cleanup
    /// stages should use it for their own bounded waits.
    /// </summary>
    public Task BeginAsync(Func<CancellationToken, Task> cleanup)
    {
        ArgumentNullException.ThrowIfNull(cleanup);
        lock (_sync)
            return _runTask ??= RunAsync(cleanup);
    }

    private async Task RunAsync(Func<CancellationToken, Task> cleanup)
    {
        using var budget = new CancellationTokenSource(_budget);
        try
        {
            await cleanup(budget.Token);
            if (budget.IsCancellationRequested)
                SetTimeout();
        }
        catch (OperationCanceledException) when (budget.IsCancellationRequested)
        {
            SetTimeout();
        }
        catch (Exception ex)
        {
            SetError(ex);
        }
        finally
        {
            lock (_sync) _completed = true;
        }
    }

    private void SetError(Exception error)
    {
        lock (_sync) _error ??= error;
    }

    private void SetTimeout()
    {
        lock (_sync) _timedOut = true;
        SetError(new TimeoutException("退出清理超过有界时间；禁止启动安装器。"));
    }
}
