using SplitTrip.BusinessObjects.Entities;

namespace SplitTrip.Repositories.Trips;

public interface ITripRepository
{
    Task<Trip?> GetByIdAsync(Guid id, CancellationToken ct = default);
    Task<Trip?> GetByIdWithDetailsAsync(Guid id, CancellationToken ct = default);
    Task<Trip?> GetByInviteCodeAsync(string inviteCode, CancellationToken ct = default);
    Task<bool> InviteCodeExistsAsync(string inviteCode, CancellationToken ct = default);
    Task<IReadOnlyList<Trip>> GetTripsForUserAsync(Guid userId, CancellationToken ct = default);
    Task AddTripWithOwnerAndDaysAsync(Trip trip, TripMember ownerMember, IEnumerable<TripDay> days, CancellationToken ct = default);
    Task UpdateAsync(Trip trip, CancellationToken ct = default);
    Task<bool> HasUnconfirmedSettlementsAsync(Guid tripId, CancellationToken ct = default);
}
