using SplitTrip.BusinessObjects.Entities;
using SplitTrip.BusinessObjects.Enums;
using SplitTrip.Repositories.Notifications;
using SplitTrip.Services.Notifications.Dtos;

namespace SplitTrip.Services.Notifications;

public sealed class NotificationService(INotificationRepository notificationRepository) : INotificationService
{
    public async Task<IReadOnlyList<NotificationDto>> GetRecentAsync(
        Guid userId,
        int limit = 15,
        CancellationToken ct = default)
    {
        var items = await notificationRepository.GetByUserAsync(userId, limit, ct);
        return items.Select(MapToDto).ToList();
    }

    public Task<int> GetUnreadCountAsync(Guid userId, CancellationToken ct = default)
        => notificationRepository.GetUnreadCountAsync(userId, ct);

    public async Task<NotificationDto> CreateAsync(
        Guid userId,
        Guid? tripId,
        NotificationType type,
        string title,
        string message,
        string? actionUrl = null,
        CancellationToken ct = default)
    {
        var entity = new Notification
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            TripId = tripId,
            Type = type,
            Title = title.Trim(),
            Message = message.Trim(),
            ActionUrl = actionUrl,
            IsRead = false,
            CreatedAtUtc = DateTime.UtcNow
        };

        await notificationRepository.AddAsync(entity, ct);
        return MapToDto(entity);
    }

    public Task<bool> MarkAsReadAsync(Guid notificationId, Guid userId, CancellationToken ct = default)
        => notificationRepository.MarkAsReadAsync(notificationId, userId, ct);

    public Task<int> MarkAllAsReadAsync(Guid userId, CancellationToken ct = default)
        => notificationRepository.MarkAllAsReadAsync(userId, ct);

    private static NotificationDto MapToDto(Notification n)
        => new(n.Id, n.TripId, n.Type, n.Title, n.Message, n.ActionUrl, n.IsRead, n.CreatedAtUtc);
}
