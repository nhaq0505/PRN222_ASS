using SplitTrip.BusinessObjects.Entities;
using SplitTrip.DataAccessObjects.Daos.Settlements;

namespace SplitTrip.Repositories.Settlements;

public sealed class SettlementRepository(SettlementDao settlementDao) : ISettlementRepository
{
    public Task<Settlement?> GetByIdAsync(Guid id, CancellationToken ct = default)
        => settlementDao.GetByIdAsync(id, ct);

    public Task<IReadOnlyList<Settlement>> GetByTripIdAsync(Guid tripId, CancellationToken ct = default)
        => settlementDao.GetByTripIdAsync(tripId, ct);

    public Task ReplacePendingSettlementsAsync(
        Guid tripId,
        IReadOnlyCollection<Settlement> newPendingSettlements,
        CancellationToken ct = default)
        => settlementDao.ReplacePendingSettlementsAsync(tripId, newPendingSettlements, ct);

    public Task AddAsync(Settlement settlement, CancellationToken ct = default)
        => settlementDao.AddAsync(settlement, ct);

    public Task UpdateAsync(Settlement settlement, CancellationToken ct = default)
        => settlementDao.UpdateAsync(settlement, ct);

    public Task<int> GetRawExpenseObligationCountAsync(Guid tripId, CancellationToken ct = default)
        => settlementDao.GetRawExpenseObligationCountAsync(tripId, ct);

    public Task<IReadOnlyList<Trip>> GetAutoSettleableTripsAsync(DateOnly todayLocal, CancellationToken ct = default)
        => settlementDao.GetAutoSettleableTripsAsync(todayLocal, ct);

    public Task<IReadOnlyList<Settlement>> GetOverdueSettlementsForNudgeAsync(DateTime cutoffUtc, CancellationToken ct = default)
        => settlementDao.GetOverdueSettlementsForNudgeAsync(cutoffUtc, ct);
}
