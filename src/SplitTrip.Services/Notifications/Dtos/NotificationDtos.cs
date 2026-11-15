using SplitTrip.BusinessObjects.Enums;

namespace SplitTrip.Services.Notifications.Dtos;

public sealed record NotificationDto(
    Guid Id,
    Guid? TripId,
    NotificationType Type,
    string Title,
    string Message,
    string? ActionUrl,
    bool IsRead,
    DateTime CreatedAtUtc);
