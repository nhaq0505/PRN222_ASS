using Microsoft.EntityFrameworkCore;
using SplitTrip.BusinessObjects.Entities;
using SplitTrip.BusinessObjects.Enums;

namespace SplitTrip.DataAccessObjects.Daos;

public sealed class TripMemberDao(SplitTripDbContext dbContext)
{
    public Task<TripMember?> GetMembershipAsync(Guid tripId, Guid userId, CancellationToken ct = default)
        => dbContext.TripMembers
            .Include(m => m.User)
            .FirstOrDefaultAsync(m => m.TripId == tripId && m.UserId == userId, ct);

    public async Task<IReadOnlyList<TripMember>> GetActiveMembersByTripAsync(Guid tripId, CancellationToken ct = default)
        => await dbContext.TripMembers
            .Include(m => m.User)
            .Where(m => m.TripId == tripId && m.IsActive)
            .OrderBy(m => m.Role)
            .ThenBy(m => m.JoinedAtUtc)
            .ToListAsync(ct);

    public async Task<IReadOnlyList<TripMember>> GetAllMembersByTripAsync(Guid tripId, CancellationToken ct = default)
        => await dbContext.TripMembers
            .Include(m => m.User)
            .Where(m => m.TripId == tripId)
            .OrderByDescending(m => m.IsActive)
            .ThenBy(m => m.Role)
            .ThenBy(m => m.JoinedAtUtc)
            .ToListAsync(ct);

    public async Task AddAsync(TripMember member, CancellationToken ct = default)
    {
        dbContext.TripMembers.Add(member);
        await dbContext.SaveChangesAsync(ct);
    }

    public async Task UpdateAsync(TripMember member, CancellationToken ct = default)
    {
        dbContext.TripMembers.Update(member);
        await dbContext.SaveChangesAsync(ct);
    }

    public async Task RemoveAsync(TripMember member, CancellationToken ct = default)
    {
        dbContext.TripMembers.Remove(member);
        await dbContext.SaveChangesAsync(ct);
    }

    public async Task<bool> HasFinancialHistoryAsync(Guid tripId, Guid userId, CancellationToken ct = default)
    {
        if (await dbContext.Expenses.AnyAsync(e => e.TripId == tripId && e.PaidByUserId == userId, ct))
            return true;

        if (await dbContext.ExpenseSplits.AnyAsync(s => s.UserId == userId && s.Expense!.TripId == tripId, ct))
            return true;

        if (await dbContext.ReceiptItemClaims.AnyAsync(c => c.UserId == userId && c.ReceiptItem!.Expense!.TripId == tripId, ct))
            return true;

        if (await dbContext.Settlements.AnyAsync(s => s.TripId == tripId && (s.FromUserId == userId || s.ToUserId == userId), ct))
            return true;

        return false;
    }

    public async Task<(IReadOnlyList<TripMember> Members, IReadOnlyList<Expense> FinalizedExpenses, IReadOnlyList<Settlement> ConfirmedSettlements)> GetTripLedgerInputsAsync(
        Guid tripId,
        CancellationToken ct = default)
    {
        var members = await dbContext.TripMembers
            .AsNoTracking()
            .Include(m => m.User)
            .Where(m => m.TripId == tripId)
            .OrderBy(m => m.Role)
            .ThenBy(m => m.JoinedAtUtc)
            .ToListAsync(ct);

        var finalizedExpenses = await dbContext.Expenses
            .AsNoTracking()
            .Include(e => e.Splits)
            .Where(e => e.TripId == tripId && e.ScanStatus != ReceiptScanStatus.DraftReview)
            .ToListAsync(ct);

        var confirmedSettlements = await dbContext.Settlements
            .AsNoTracking()
            .Where(s => s.TripId == tripId && s.Status == SettlementStatus.Confirmed)
            .ToListAsync(ct);

        return (members, finalizedExpenses, confirmedSettlements);
    }
}
