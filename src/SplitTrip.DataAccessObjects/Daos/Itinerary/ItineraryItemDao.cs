using Microsoft.EntityFrameworkCore;
using SplitTrip.BusinessObjects.Entities;
using SplitTrip.BusinessObjects.Enums;

namespace SplitTrip.DataAccessObjects.Daos.Itinerary;

public sealed class ItineraryItemDao(SplitTripDbContext dbContext)
{
    public Task<ItineraryItem?> GetByIdAsync(Guid id, CancellationToken ct = default)
        => dbContext.ItineraryItems
            .Include(i => i.TripDay!)
                .ThenInclude(d => d.Trip)
            .Include(i => i.CreatedByUser)
            .Include(i => i.Votes)
                .ThenInclude(v => v.User)
            .FirstOrDefaultAsync(i => i.Id == id, ct);

    public async Task<IReadOnlyList<ItineraryItem>> GetByTripIdAsync(
        Guid tripId,
        CancellationToken ct = default)
        => await dbContext.ItineraryItems
            .Include(i => i.TripDay)
            .Include(i => i.CreatedByUser)
            .Include(i => i.Votes)
                .ThenInclude(v => v.User)
            .Where(i => i.TripDay != null && i.TripDay.TripId == tripId)
            .OrderBy(i => i.TripDay!.DayNumber)
            .ThenBy(i => i.SortOrder)
            .ThenBy(i => i.CreatedAtUtc)
            .ToListAsync(ct);

    public async Task<IReadOnlyList<ItineraryItem>> GetByTripDayIdAsync(
        Guid tripDayId,
        CancellationToken ct = default)
        => await dbContext.ItineraryItems
            .Include(i => i.CreatedByUser)
            .Include(i => i.Votes)
                .ThenInclude(v => v.User)
            .Where(i => i.TripDayId == tripDayId)
            .OrderBy(i => i.SortOrder)
            .ThenBy(i => i.CreatedAtUtc)
            .ToListAsync(ct);

    public async Task<int> GetMaxSortOrderAsync(Guid tripDayId, CancellationToken ct = default)
    {
        var maxSort = await dbContext.ItineraryItems
            .Where(i => i.TripDayId == tripDayId)
            .Select(i => (int?)i.SortOrder)
            .MaxAsync(ct);

        return maxSort ?? -1;
    }

    public async Task AddAsync(ItineraryItem item, CancellationToken ct = default)
    {
        dbContext.ItineraryItems.Add(item);
        await dbContext.SaveChangesAsync(ct);
    }

    public async Task AddRangeAsync(IEnumerable<ItineraryItem> items, CancellationToken ct = default)
    {
        dbContext.ItineraryItems.AddRange(items);
        await dbContext.SaveChangesAsync(ct);
    }

    public async Task UpdateAsync(ItineraryItem item, CancellationToken ct = default)
    {
        dbContext.ItineraryItems.Update(item);
        await dbContext.SaveChangesAsync(ct);
    }

    public async Task UpdateRangeAsync(IEnumerable<ItineraryItem> items, CancellationToken ct = default)
    {
        dbContext.ItineraryItems.UpdateRange(items);
        await dbContext.SaveChangesAsync(ct);
    }

    public async Task DeleteAsync(ItineraryItem item, CancellationToken ct = default)
    {
        var dayId = item.TripDayId;
        dbContext.ItineraryItems.Remove(item);

        var remaining = await dbContext.ItineraryItems
            .Where(i => i.TripDayId == dayId && i.Id != item.Id)
            .OrderBy(i => i.SortOrder)
            .ThenBy(i => i.CreatedAtUtc)
            .ToListAsync(ct);

        for (var idx = 0; idx < remaining.Count; idx++)
        {
            remaining[idx].SortOrder = idx;
        }

        await dbContext.SaveChangesAsync(ct);
    }

    public async Task<IReadOnlyList<ItineraryItem>> GetCandidatesForDepartureReminderAsync(
        DateOnly minDate,
        DateOnly maxDate,
        CancellationToken ct = default)
        => await dbContext.ItineraryItems
            .Include(i => i.TripDay!)
                .ThenInclude(d => d.Trip!)
                    .ThenInclude(t => t.Members)
            .Where(i => i.StartTime != null
                        && i.ReminderSentAtUtc == null
                        && i.TripDay != null
                        && i.TripDay.Date >= minDate
                        && i.TripDay.Date <= maxDate
                        && i.TripDay.Trip != null
                        && i.TripDay.Trip.Status == TripStatus.Ongoing)
            .ToListAsync(ct);

    public async Task MarkReminderSentWithNotificationsAsync(
        ItineraryItem item,
        DateTime sentAtUtc,
        IEnumerable<Notification> notifications,
        CancellationToken ct = default)
    {
        item.ReminderSentAtUtc = sentAtUtc;
        dbContext.ItineraryItems.Update(item);
        dbContext.Notifications.AddRange(notifications);
        await dbContext.SaveChangesAsync(ct);
    }
}
