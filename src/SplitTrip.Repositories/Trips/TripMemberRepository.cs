using SplitTrip.BusinessObjects.Entities;
using SplitTrip.DataAccessObjects.Daos;

namespace SplitTrip.Repositories.Trips;

public sealed class TripMemberRepository(TripMemberDao tripMemberDao) : ITripMemberRepository
{
    public Task<TripMember?> GetMembershipAsync(Guid tripId, Guid userId, CancellationToken ct = default)
        => tripMemberDao.GetMembershipAsync(tripId, userId, ct);

    public Task<IReadOnlyList<TripMember>> GetActiveMembersByTripAsync(Guid tripId, CancellationToken ct = default)
        => tripMemberDao.GetActiveMembersByTripAsync(tripId, ct);

    public Task<IReadOnlyList<TripMember>> GetAllMembersByTripAsync(Guid tripId, CancellationToken ct = default)
        => tripMemberDao.GetAllMembersByTripAsync(tripId, ct);

    public Task AddAsync(TripMember member, CancellationToken ct = default)
        => tripMemberDao.AddAsync(member, ct);

    public Task UpdateAsync(TripMember member, CancellationToken ct = default)
        => tripMemberDao.UpdateAsync(member, ct);

    public Task RemoveAsync(TripMember member, CancellationToken ct = default)
        => tripMemberDao.RemoveAsync(member, ct);

    public Task<bool> HasFinancialHistoryAsync(Guid tripId, Guid userId, CancellationToken ct = default)
        => tripMemberDao.HasFinancialHistoryAsync(tripId, userId, ct);

    public Task<(IReadOnlyList<TripMember> Members, IReadOnlyList<Expense> FinalizedExpenses, IReadOnlyList<Settlement> ConfirmedSettlements)> GetTripLedgerInputsAsync(
        Guid tripId,
        CancellationToken ct = default)
        => tripMemberDao.GetTripLedgerInputsAsync(tripId, ct);
}
