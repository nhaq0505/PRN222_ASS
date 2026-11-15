using SplitTrip.BusinessObjects.Entities;

namespace SplitTrip.Repositories.Settlements;

public interface ISettlementRepository
{
    Task<Settlement?> GetByIdAsync(Guid id, CancellationToken ct = default);
    Task<IReadOnlyList<Settlement>> GetByTripIdAsync(Guid tripId, CancellationToken ct = default);
    Task ReplacePendingSettlementsAsync(
        Guid tripId,
        IReadOnlyCollection<Settlement> newPendingSettlements,
        CancellationToken ct = default);
    Task AddAsync(Settlement settlement, CancellationToken ct = default);
    Task UpdateAsync(Settlement settlement, CancellationToken ct = default);
    Task<int> GetRawExpenseObligationCountAsync(Guid tripId, CancellationToken ct = default);
    Task<IReadOnlyList<Trip>> GetAutoSettleableTripsAsync(DateOnly todayLocal, CancellationToken ct = default);
    Task<IReadOnlyList<Settlement>> GetOverdueSettlementsForNudgeAsync(DateTime cutoffUtc, CancellationToken ct = default);
}
