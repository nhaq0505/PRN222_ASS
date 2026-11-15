using SplitTrip.BusinessObjects.Enums;
using SplitTrip.Services.Notifications.Dtos;

namespace SplitTrip.Services.Notifications;

public interface INotificationService
{
    Task<IReadOnlyList<NotificationDto>> GetRecentAsync(Guid userId, int limit = 15, CancellationToken ct = default);
    Task<int> GetUnreadCountAsync(Guid userId, CancellationToken ct = default);
    Task<NotificationDto> CreateAsync(
        Guid userId,
        Guid? tripId,
        NotificationType type,
        string title,
        string message,
        string? actionUrl = null,
        CancellationToken ct = default);
    Task<bool> MarkAsReadAsync(Guid notificationId, Guid userId, CancellationToken ct = default);
    Task<int> MarkAllAsReadAsync(Guid userId, CancellationToken ct = default);
}
