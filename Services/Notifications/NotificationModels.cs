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
    string Action = "overview",
    string? DedupeKey = null);

public interface INotificationService : IDisposable
{
    bool IsAvailable { get; }
    void Initialize(Action<string>? actionRouter = null);
    bool TryNotify(NotificationRequest request);
}
