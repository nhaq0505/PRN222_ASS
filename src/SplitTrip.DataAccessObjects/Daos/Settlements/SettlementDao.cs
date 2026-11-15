using Microsoft.EntityFrameworkCore;
using SplitTrip.BusinessObjects.Entities;
using SplitTrip.BusinessObjects.Enums;

namespace SplitTrip.DataAccessObjects.Daos.Settlements;

public sealed class SettlementDao(SplitTripDbContext dbContext)
{
    public Task<Settlement?> GetByIdAsync(Guid id, CancellationToken ct = default)
        => dbContext.Settlements
            .Include(s => s.Trip)
                .ThenInclude(t => t!.Members)
            .Include(s => s.FromUser)
            .Include(s => s.ToUser)
            .FirstOrDefaultAsync(s => s.Id == id, ct);

    public async Task<IReadOnlyList<Settlement>> GetByTripIdAsync(Guid tripId, CancellationToken ct = default)
        => await dbContext.Settlements
            .AsNoTracking()
            .Include(s => s.FromUser)
            .Include(s => s.ToUser)
            .Where(s => s.TripId == tripId)
            .OrderByDescending(s => s.CreatedAtUtc)
            .ThenByDescending(s => s.Amount)
            .ToListAsync(ct);

    public async Task ReplacePendingSettlementsAsync(
        Guid tripId,
        IReadOnlyCollection<Settlement> newPendingSettlements,
        CancellationToken ct = default)
    {
        var existingPending = await dbContext.Settlements
            .Where(s => s.TripId == tripId && s.Status == SettlementStatus.Pending)
            .ToListAsync(ct);

        if (existingPending.Count > 0)
        {
            dbContext.Settlements.RemoveRange(existingPending);
        }

        if (newPendingSettlements.Count > 0)
        {
            dbContext.Settlements.AddRange(newPendingSettlements);
        }

        await dbContext.SaveChangesAsync(ct);
    }

    public async Task AddAsync(Settlement settlement, CancellationToken ct = default)
    {
        dbContext.Settlements.Add(settlement);
        await dbContext.SaveChangesAsync(ct);
    }

    public async Task UpdateAsync(Settlement settlement, CancellationToken ct = default)
    {
        dbContext.Settlements.Update(settlement);
        await dbContext.SaveChangesAsync(ct);
    }

    public async Task<int> GetRawExpenseObligationCountAsync(Guid tripId, CancellationToken ct = default)
    {
        var finalizedExpenses = await dbContext.Expenses
            .AsNoTracking()
            .Include(e => e.Splits)
            .Where(e => e.TripId == tripId && e.ScanStatus != ReceiptScanStatus.DraftReview)
            .ToListAsync(ct);

        return finalizedExpenses.Sum(e =>
            e.Splits.Count(s => s.UserId != e.PaidByUserId && s.OwedAmount > 0m));
    }

    public async Task<IReadOnlyList<Trip>> GetAutoSettleableTripsAsync(
        DateOnly todayLocal,
        CancellationToken ct = default)
        => await dbContext.Trips
            .Include(t => t.Members)
                .ThenInclude(m => m.User)
            .Where(t => t.Status == TripStatus.Ongoing
                        && t.EndDate < todayLocal
                        && !t.Expenses.Any(e => e.ScanStatus == ReceiptScanStatus.DraftReview
                                                || e.ScanStatus == ReceiptScanStatus.ClaimingOpen))
            .ToListAsync(ct);

    public async Task<IReadOnlyList<Settlement>> GetOverdueSettlementsForNudgeAsync(
        DateTime cutoffUtc,
        CancellationToken ct = default)
        => await dbContext.Settlements
            .Include(s => s.Trip)
            .Include(s => s.FromUser)
            .Include(s => s.ToUser)
            .Where(s => s.Trip != null
                        && s.Trip.Status == TripStatus.Settling
                        && (s.Status == SettlementStatus.Pending || s.Status == SettlementStatus.Disputed)
                        && s.CreatedAtUtc <= cutoffUtc
                        && (s.LastNudgedAtUtc == null || s.LastNudgedAtUtc <= cutoffUtc))
            .ToListAsync(ct);
}
