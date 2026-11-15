using SplitTrip.BusinessObjects.Entities;

namespace SplitTrip.Repositories.Itinerary;

public interface ITripDayRepository
{
    Task<IReadOnlyList<TripDay>> GetByTripIdWithItemsAsync(Guid tripId, CancellationToken ct = default);
    Task<TripDay?> GetByIdAsync(Guid id, CancellationToken ct = default);
    Task<TripDay?> GetByTripAndDayNumberAsync(Guid tripId, int dayNumber, CancellationToken ct = default);
    Task AddRangeAsync(IEnumerable<TripDay> days, CancellationToken ct = default);
    Task UpdateAsync(TripDay day, CancellationToken ct = default);
    Task UpdateRangeAsync(IEnumerable<TripDay> days, CancellationToken ct = default);
}
