using SplitTrip.BusinessObjects.Entities;
using SplitTrip.DataAccessObjects.Daos.Itinerary;

namespace SplitTrip.Repositories.Itinerary;

public sealed class ItineraryItemRepository(ItineraryItemDao itineraryItemDao) : IItineraryItemRepository
{
    public Task<ItineraryItem?> GetByIdAsync(Guid id, CancellationToken ct = default)
        => itineraryItemDao.GetByIdAsync(id, ct);

    public Task<IReadOnlyList<ItineraryItem>> GetByTripIdAsync(Guid tripId, CancellationToken ct = default)
        => itineraryItemDao.GetByTripIdAsync(tripId, ct);

    public Task<IReadOnlyList<ItineraryItem>> GetByTripDayIdAsync(Guid tripDayId, CancellationToken ct = default)
        => itineraryItemDao.GetByTripDayIdAsync(tripDayId, ct);

    public Task<int> GetMaxSortOrderAsync(Guid tripDayId, CancellationToken ct = default)
        => itineraryItemDao.GetMaxSortOrderAsync(tripDayId, ct);

    public Task AddAsync(ItineraryItem item, CancellationToken ct = default)
        => itineraryItemDao.AddAsync(item, ct);

    public Task AddRangeAsync(IEnumerable<ItineraryItem> items, CancellationToken ct = default)
        => itineraryItemDao.AddRangeAsync(items, ct);

    public Task UpdateAsync(ItineraryItem item, CancellationToken ct = default)
        => itineraryItemDao.UpdateAsync(item, ct);

    public Task UpdateRangeAsync(IEnumerable<ItineraryItem> items, CancellationToken ct = default)
        => itineraryItemDao.UpdateRangeAsync(items, ct);

    public Task DeleteAsync(ItineraryItem item, CancellationToken ct = default)
        => itineraryItemDao.DeleteAsync(item, ct);

    public Task<IReadOnlyList<ItineraryItem>> GetCandidatesForDepartureReminderAsync(
        DateOnly minDate,
        DateOnly maxDate,
        CancellationToken ct = default)
        => itineraryItemDao.GetCandidatesForDepartureReminderAsync(minDate, maxDate, ct);

    public Task MarkReminderSentWithNotificationsAsync(
        ItineraryItem item,
        DateTime sentAtUtc,
        IEnumerable<Notification> notifications,
        CancellationToken ct = default)
        => itineraryItemDao.MarkReminderSentWithNotificationsAsync(item, sentAtUtc, notifications, ct);
}
