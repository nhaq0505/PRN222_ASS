using SplitTrip.BusinessObjects.Entities;

namespace SplitTrip.Repositories.Trips;

public interface ITripMemberRepository
{
    Task<TripMember?> GetMembershipAsync(Guid tripId, Guid userId, CancellationToken ct = default);
    Task<IReadOnlyList<TripMember>> GetActiveMembersByTripAsync(Guid tripId, CancellationToken ct = default);
    Task<IReadOnlyList<TripMember>> GetAllMembersByTripAsync(Guid tripId, CancellationToken ct = default);
    Task AddAsync(TripMember member, CancellationToken ct = default);
    Task UpdateAsync(TripMember member, CancellationToken ct = default);
    Task RemoveAsync(TripMember member, CancellationToken ct = default);
    Task<bool> HasFinancialHistoryAsync(Guid tripId, Guid userId, CancellationToken ct = default);
    Task<(IReadOnlyList<TripMember> Members, IReadOnlyList<Expense> FinalizedExpenses, IReadOnlyList<Settlement> ConfirmedSettlements)> GetTripLedgerInputsAsync(Guid tripId, CancellationToken ct = default);
}
