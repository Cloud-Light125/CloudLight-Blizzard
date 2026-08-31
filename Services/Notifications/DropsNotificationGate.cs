using CloudLightBlizzard.Services.Drops;

namespace CloudLightBlizzard.Services.Notifications;

/// <summary>
/// Collapses a noisy worker connection sequence into one failure toast and one
/// debounced recovery toast per platform. Notification failures are deliberately
/// swallowed so the worker state machine remains the source of truth.
/// </summary>
internal sealed class DropsNotificationGate : IDisposable
{
    private readonly object _gate = new();
    private readonly TimeSpan _recoveryDelay;
    private readonly Action<NotificationRequest> _notify;
    private readonly HashSet<DropsPlatform> _degraded = [];
    private readonly Dictionary<DropsPlatform, CancellationTokenSource> _recoveryCancellations = [];
    private readonly Dictionary<DropsPlatform, Task> _recoveryTasks = [];
    private bool _disposed;

    public DropsNotificationGate(Action<NotificationRequest> notify, TimeSpan? recoveryDelay = null)
    {
        _notify = notify;
        _recoveryDelay = recoveryDelay ?? TimeSpan.FromSeconds(5);
    }

    public void ReportFailure(DropsPlatform platform, string title, string message)
    {
        NotificationRequest? request = null;
        lock (_gate)
        {
            if (_disposed) return;
            CancelRecoveryLocked(platform);
            if (_degraded.Add(platform))
                request = new NotificationRequest(title, message, NotificationCategory.Drops,
                    "drops", $"drops-degraded:{platform}");
        }
        if (request is not null) SafeNotify(request);
    }

    public void ReportRecovery(DropsPlatform platform, string title, string message)
    {
        CancellationTokenSource cancellation;
        lock (_gate)
        {
            if (_disposed || !_degraded.Contains(platform) ||
                _recoveryTasks.TryGetValue(platform, out var existing) && !existing.IsCompleted) return;
            cancellation = new CancellationTokenSource();
            _recoveryCancellations[platform] = cancellation;
            _recoveryTasks[platform] = NotifyRecoveryAsync(platform, title, message, cancellation);
        }
    }

    internal async Task FlushForSelfTestAsync()
    {
        Task[] tasks;
        lock (_gate) tasks = _recoveryTasks.Values.ToArray();
        await Task.WhenAll(tasks).ConfigureAwait(false);
    }

    private async Task NotifyRecoveryAsync(DropsPlatform platform, string title, string message,
        CancellationTokenSource cancellation)
    {
        try
        {
            await Task.Delay(_recoveryDelay, cancellation.Token).ConfigureAwait(false);
            var shouldNotify = false;
            lock (_gate)
            {
                if (!_disposed && _recoveryCancellations.TryGetValue(platform, out var current) &&
                    ReferenceEquals(current, cancellation))
                {
                    _recoveryCancellations.Remove(platform);
                    _recoveryTasks.Remove(platform);
                    shouldNotify = _degraded.Remove(platform);
                }
            }
            if (shouldNotify)
                SafeNotify(new NotificationRequest(title, message, NotificationCategory.Drops,
                    "drops", $"drops-recovered:{platform}"));
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested) { }
        finally
        {
            lock (_gate)
            {
                if (_recoveryCancellations.TryGetValue(platform, out var current) &&
                    ReferenceEquals(current, cancellation))
                    _recoveryCancellations.Remove(platform);
                if (_recoveryTasks.TryGetValue(platform, out var task) &&
                    task.IsCompleted)
                    _recoveryTasks.Remove(platform);
            }
            cancellation.Dispose();
        }
    }

    private void CancelRecoveryLocked(DropsPlatform platform)
    {
        if (_recoveryCancellations.Remove(platform, out var cancellation))
            cancellation.Cancel();
        _recoveryTasks.Remove(platform);
    }

    private void SafeNotify(NotificationRequest request)
    {
        try { _notify(request); }
        catch { /* Notification is an optional side effect. */ }
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed) return;
            _disposed = true;
            foreach (var cancellation in _recoveryCancellations.Values)
            {
                try { cancellation.Cancel(); } catch { }
            }
            _recoveryCancellations.Clear();
            _recoveryTasks.Clear();
            _degraded.Clear();
        }
    }
}
