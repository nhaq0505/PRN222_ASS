using SplitTrip.BusinessObjects.Entities;
using SplitTrip.DataAccessObjects.Daos.Itinerary;

namespace SplitTrip.Repositories.Itinerary;

public sealed class TripDayRepository(TripDayDao tripDayDao) : ITripDayRepository
{
    public Task<IReadOnlyList<TripDay>> GetByTripIdWithItemsAsync(Guid tripId, CancellationToken ct = default)
        => tripDayDao.GetByTripIdWithItemsAsync(tripId, ct);

    public Task<TripDay?> GetByIdAsync(Guid id, CancellationToken ct = default)
        => tripDayDao.GetByIdAsync(id, ct);

    public Task<TripDay?> GetByTripAndDayNumberAsync(Guid tripId, int dayNumber, CancellationToken ct = default)
        => tripDayDao.GetByTripAndDayNumberAsync(tripId, dayNumber, ct);

    public Task AddRangeAsync(IEnumerable<TripDay> days, CancellationToken ct = default)
        => tripDayDao.AddRangeAsync(days, ct);

    public Task UpdateAsync(TripDay day, CancellationToken ct = default)
        => tripDayDao.UpdateAsync(day, ct);

    public Task UpdateRangeAsync(IEnumerable<TripDay> days, CancellationToken ct = default)
        => tripDayDao.UpdateRangeAsync(days, ct);
}
