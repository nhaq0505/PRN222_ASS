using Microsoft.EntityFrameworkCore;
using SplitTrip.BusinessObjects.Entities;

namespace SplitTrip.DataAccessObjects.Daos;

public sealed class NotificationDao(SplitTripDbContext dbContext)
{
    public async Task<IReadOnlyList<Notification>> GetByUserAsync(
        Guid userId,
        int limit = 20,
        CancellationToken ct = default)
        => await dbContext.Notifications
            .AsNoTracking()
            .Where(n => n.UserId == userId)
            .OrderByDescending(n => n.CreatedAtUtc)
            .Take(limit)
            .ToListAsync(ct);

    public Task<int> GetUnreadCountAsync(Guid userId, CancellationToken ct = default)
        => dbContext.Notifications.CountAsync(n => n.UserId == userId && !n.IsRead, ct);

    public Task<Notification?> GetByIdAsync(Guid id, CancellationToken ct = default)
        => dbContext.Notifications.FirstOrDefaultAsync(n => n.Id == id, ct);

    public async Task AddAsync(Notification notification, CancellationToken ct = default)
    {
        dbContext.Notifications.Add(notification);
        await dbContext.SaveChangesAsync(ct);
    }

    public async Task AddRangeAsync(IEnumerable<Notification> notifications, CancellationToken ct = default)
    {
        dbContext.Notifications.AddRange(notifications);
        await dbContext.SaveChangesAsync(ct);
    }

    public async Task<bool> MarkAsReadAsync(Guid notificationId, Guid userId, CancellationToken ct = default)
    {
        var notification = await dbContext.Notifications
            .FirstOrDefaultAsync(n => n.Id == notificationId && n.UserId == userId, ct);

        if (notification is null)
            return false;

        if (!notification.IsRead)
        {
            notification.IsRead = true;
            await dbContext.SaveChangesAsync(ct);
        }

        return true;
    }

    public async Task<int> MarkAllAsReadAsync(Guid userId, CancellationToken ct = default)
    {
        var unread = await dbContext.Notifications
            .Where(n => n.UserId == userId && !n.IsRead)
            .ToListAsync(ct);

        if (unread.Count == 0)
            return 0;

        foreach (var item in unread)
        {
            item.IsRead = true;
        }

        await dbContext.SaveChangesAsync(ct);
        return unread.Count;
    }
}
