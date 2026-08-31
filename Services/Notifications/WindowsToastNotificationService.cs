using System.IO;
using System.Text.Json;
using CommunityToolkit.WinUI.Notifications;
using CloudLightBlizzard.Services;

namespace CloudLightBlizzard.Services.Notifications;

/// <summary>
/// Windows 10/11 Toast implementation for the unpackaged Inno Setup desktop app.
/// The product-specific code depends only on INotificationService so tests never send real Toasts.
/// </summary>
public sealed class WindowsToastNotificationService : INotificationService
{
    private static readonly TimeSpan MinimumInterval = TimeSpan.FromSeconds(5);
    private readonly AppSettings _settings;
    private readonly object _gate = new();
    private Action<string>? _actionRouter;
    private DateTimeOffset _lastShownAt = DateTimeOffset.MinValue;
    private string? _lastDedupeKey;
    private bool _initialized;
    private bool _disposed;

    public WindowsToastNotificationService(AppSettings settings) => _settings = settings;

    public bool IsAvailable => OperatingSystem.IsWindowsVersionAtLeast(10, 0, 10240);

    public void Initialize(Action<string>? actionRouter = null)
    {
        if (_disposed || !IsAvailable) return;
        _actionRouter = actionRouter;
        if (_initialized) return;
        try
        {
            ToastNotificationManagerCompat.OnActivated += OnActivated;
            _initialized = true;
            WriteLog("initialized");
        }
        catch (Exception ex)
        {
            WriteLog($"initialize failed: {ex.GetType().Name}");
        }
    }

    public bool TryNotify(NotificationRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (_disposed || !IsAvailable || !IsEnabled(request.Category)) return false;
        lock (_gate)
        {
            var now = DateTimeOffset.Now;
            if (now - _lastShownAt < MinimumInterval) return false;
            if (!string.IsNullOrWhiteSpace(request.DedupeKey) &&
                string.Equals(_lastDedupeKey, request.DedupeKey, StringComparison.Ordinal)) return false;
            _lastShownAt = now;
            _lastDedupeKey = request.DedupeKey;
        }

        try
        {
            new ToastContentBuilder()
                .AddText(request.Title)
                .AddText(request.Message)
                .AddArgument("action", request.Action)
                .Show();
            WriteLog($"shown: {request.Category}");
            return true;
        }
        catch (Exception ex)
        {
            WriteLog($"show failed: {ex.GetType().Name}");
            return false;
        }
    }

    private bool IsEnabled(NotificationCategory category) =>
        _settings.EnableWindowsNotifications && category switch
        {
            NotificationCategory.RegionSwitch => _settings.NotifyRegionSwitch,
            NotificationCategory.Drops => _settings.NotifyDrops,
            NotificationCategory.Updates => _settings.NotifyUpdates,
            NotificationCategory.Announcements => _settings.NotifyAnnouncements,
            _ => false,
        };

    private void OnActivated(ToastNotificationActivatedEventArgsCompat args)
    {
        var action = args.Argument;
        if (string.IsNullOrWhiteSpace(action)) return;
        try { _actionRouter?.Invoke(action); }
        catch (Exception ex) { WriteLog($"activation failed: {ex.GetType().Name}"); }
    }

    private static void WriteLog(string message)
    {
        try
        {
            var directory = AppPaths.Current.LogsDir;
            Directory.CreateDirectory(directory);
            File.AppendAllText(Path.Combine(directory, "notifications.log"),
                $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] [notification] {message}{Environment.NewLine}");
        }
        catch { }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        if (!_initialized) return;
        try { ToastNotificationManagerCompat.OnActivated -= OnActivated; }
        catch { }
        _initialized = false;
    }
}

public sealed class RecordingNotificationService : INotificationService
{
    public List<NotificationRequest> Requests { get; } = [];
    public bool IsAvailable => true;
    public void Initialize(Action<string>? actionRouter = null) { }
    public bool TryNotify(NotificationRequest request) { Requests.Add(request); return true; }
    public void Dispose() { }
}
