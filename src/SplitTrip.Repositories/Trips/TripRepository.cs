using SplitTrip.BusinessObjects.Entities;
using SplitTrip.DataAccessObjects.Daos;

namespace SplitTrip.Repositories.Trips;

public sealed class TripRepository(TripDao tripDao) : ITripRepository
{
    public Task<Trip?> GetByIdAsync(Guid id, CancellationToken ct = default)
        => tripDao.GetByIdAsync(id, ct);

    public Task<Trip?> GetByIdWithDetailsAsync(Guid id, CancellationToken ct = default)
        => tripDao.GetByIdWithDetailsAsync(id, ct);

    public Task<Trip?> GetByInviteCodeAsync(string inviteCode, CancellationToken ct = default)
        => tripDao.GetByInviteCodeAsync(inviteCode, ct);

    public Task<bool> InviteCodeExistsAsync(string inviteCode, CancellationToken ct = default)
        => tripDao.InviteCodeExistsAsync(inviteCode, ct);

    public Task<IReadOnlyList<Trip>> GetTripsForUserAsync(Guid userId, CancellationToken ct = default)
        => tripDao.GetTripsForUserAsync(userId, ct);

    public Task AddTripWithOwnerAndDaysAsync(
        Trip trip,
        TripMember ownerMember,
        IEnumerable<TripDay> days,
        CancellationToken ct = default)
        => tripDao.AddTripWithOwnerAndDaysAsync(trip, ownerMember, days, ct);

    public Task UpdateAsync(Trip trip, CancellationToken ct = default)
        => tripDao.UpdateAsync(trip, ct);

    public Task<bool> HasUnconfirmedSettlementsAsync(Guid tripId, CancellationToken ct = default)
        => tripDao.HasUnconfirmedSettlementsAsync(tripId, ct);
}
