using System.Diagnostics;
using System.IO;

namespace CloudLightBlizzard.Services.Notifications;

public enum NotificationCategory
{
    RegionSwitch,
    Drops,
    Updates,
    Announcements,
}

public sealed record NotificationRequest(
    string Title,
    string Message,
    NotificationCategory Category,
    string Action = "accounts",
    string? DedupeKey = null);

public interface INotificationService : IDisposable
{
    bool IsAvailable { get; }
    void Initialize(Action<string>? actionRouter = null);
    bool TryNotify(NotificationRequest request);
}

public static class NotificationSafety
{
    public static bool TryNotifySafely(INotificationService service, NotificationRequest request)
    {
        try
        {
            return service.TryNotify(request);
        }
        catch (Exception ex)
        {
            var line = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] [notification] {request.Category} failed: {ex.GetType().Name}{Environment.NewLine}";
            Trace.Write(line);
            try
            {
                Directory.CreateDirectory(AppPaths.Current.LogsDir);
                File.AppendAllText(Path.Combine(AppPaths.Current.LogsDir, "notifications.log"), line);
            }
            catch { }
            return false;
        }
    }
}
