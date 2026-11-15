using SplitTrip.BusinessObjects.Entities;

namespace SplitTrip.Repositories.Itinerary;

public interface IItineraryItemRepository
{
    Task<ItineraryItem?> GetByIdAsync(Guid id, CancellationToken ct = default);
    Task<IReadOnlyList<ItineraryItem>> GetByTripIdAsync(Guid tripId, CancellationToken ct = default);
    Task<IReadOnlyList<ItineraryItem>> GetByTripDayIdAsync(Guid tripDayId, CancellationToken ct = default);
    Task<int> GetMaxSortOrderAsync(Guid tripDayId, CancellationToken ct = default);
    Task AddAsync(ItineraryItem item, CancellationToken ct = default);
    Task AddRangeAsync(IEnumerable<ItineraryItem> items, CancellationToken ct = default);
    Task UpdateAsync(ItineraryItem item, CancellationToken ct = default);
    Task UpdateRangeAsync(IEnumerable<ItineraryItem> items, CancellationToken ct = default);
    Task DeleteAsync(ItineraryItem item, CancellationToken ct = default);
    Task<IReadOnlyList<ItineraryItem>> GetCandidatesForDepartureReminderAsync(
        DateOnly minDate,
        DateOnly maxDate,
        CancellationToken ct = default);
    Task MarkReminderSentWithNotificationsAsync(
        ItineraryItem item,
        DateTime sentAtUtc,
        IEnumerable<Notification> notifications,
        CancellationToken ct = default);
}
