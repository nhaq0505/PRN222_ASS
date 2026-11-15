using SplitTrip.BusinessObjects.Entities;
using SplitTrip.DataAccessObjects.Daos;

namespace SplitTrip.Repositories.Notifications;

public sealed class NotificationRepository(NotificationDao notificationDao) : INotificationRepository
{
    public Task<IReadOnlyList<Notification>> GetByUserAsync(Guid userId, int limit = 20, CancellationToken ct = default)
        => notificationDao.GetByUserAsync(userId, limit, ct);

    public Task<int> GetUnreadCountAsync(Guid userId, CancellationToken ct = default)
        => notificationDao.GetUnreadCountAsync(userId, ct);

    public Task<Notification?> GetByIdAsync(Guid id, CancellationToken ct = default)
        => notificationDao.GetByIdAsync(id, ct);

    public Task AddAsync(Notification notification, CancellationToken ct = default)
        => notificationDao.AddAsync(notification, ct);

    public Task AddRangeAsync(IEnumerable<Notification> notifications, CancellationToken ct = default)
        => notificationDao.AddRangeAsync(notifications, ct);

    public Task<bool> MarkAsReadAsync(Guid notificationId, Guid userId, CancellationToken ct = default)
        => notificationDao.MarkAsReadAsync(notificationId, userId, ct);

    public Task<int> MarkAllAsReadAsync(Guid userId, CancellationToken ct = default)
        => notificationDao.MarkAllAsReadAsync(userId, ct);
}
