using Microsoft.EntityFrameworkCore;
using SplitTrip.BusinessObjects.Entities;
using SplitTrip.BusinessObjects.Enums;

namespace SplitTrip.DataAccessObjects.Daos.Expenses;

public sealed class ExpenseDao(SplitTripDbContext dbContext)
{
    public Task<Expense?> GetByIdAsync(Guid expenseId, CancellationToken ct = default)
        => dbContext.Expenses
            .AsNoTracking()
            .Include(e => e.Trip)
            .Include(e => e.PaidByUser)
            .Include(e => e.Splits)
                .ThenInclude(s => s.User)
            .Include(e => e.ReceiptItems)
                .ThenInclude(i => i.Claims)
                    .ThenInclude(c => c.User)
            .FirstOrDefaultAsync(e => e.Id == expenseId, ct);

    public async Task<IReadOnlyList<Expense>> GetByTripAsync(
        Guid tripId,
        ExpenseCategory? category = null,
        bool includeDrafts = true,
        CancellationToken ct = default)
    {
        var query = dbContext.Expenses
            .AsNoTracking()
            .Include(e => e.PaidByUser)
            .Include(e => e.Splits)
                .ThenInclude(s => s.User)
            .Include(e => e.ReceiptItems)
                .ThenInclude(i => i.Claims)
                    .ThenInclude(c => c.User)
            .Where(e => e.TripId == tripId);

        if (category.HasValue)
        {
            query = query.Where(e => e.Category == category.Value);
        }

        if (!includeDrafts)
        {
            query = query.Where(e => e.ScanStatus != ReceiptScanStatus.DraftReview);
        }

        return await query
            .OrderByDescending(e => e.SpentAtUtc)
            .ThenByDescending(e => e.CreatedAtUtc)
            .ToListAsync(ct);
    }

    public async Task AddExpenseWithSplitsAsync(
        Expense expense,
        IEnumerable<ExpenseSplit> splits,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(expense);
        ArgumentNullException.ThrowIfNull(splits);

        var splitEntities = splits.Select(s => new ExpenseSplit
        {
            Id = s.Id == Guid.Empty ? Guid.NewGuid() : s.Id,
            ExpenseId = expense.Id,
            UserId = s.UserId,
            ShareRatioOrWeight = s.ShareRatioOrWeight,
            OwedAmount = s.OwedAmount
        }).ToList();

        expense.Splits = new List<ExpenseSplit>();
        expense.ReceiptItems = new List<ReceiptItem>();

        dbContext.Expenses.Add(expense);
        if (splitEntities.Count > 0)
        {
            dbContext.ExpenseSplits.AddRange(splitEntities);
        }

        await dbContext.SaveChangesAsync(ct);
    }

    public async Task UpdateExpenseWithSplitsAsync(
        Expense expense,
        IEnumerable<ExpenseSplit> newSplits,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(expense);
        ArgumentNullException.ThrowIfNull(newSplits);

        var tracked = await dbContext.Expenses
            .FirstOrDefaultAsync(e => e.Id == expense.Id, ct)
            ?? throw new KeyNotFoundException("Không tìm thấy khoản chi tiêu cần cập nhật.");

        tracked.PaidByUserId = expense.PaidByUserId;
        tracked.Title = expense.Title;
        tracked.Category = expense.Category;
        tracked.SubtotalAmount = expense.SubtotalAmount;
        tracked.TaxOrTipAmount = expense.TaxOrTipAmount;
        tracked.DiscountAmount = expense.DiscountAmount;
        tracked.TotalAmount = expense.TotalAmount;
        tracked.SplitMode = expense.SplitMode;
        tracked.ReceiptImageUrl = expense.ReceiptImageUrl;
        tracked.ScanStatus = expense.ScanStatus;
        tracked.SpentAtUtc = expense.SpentAtUtc;
        tracked.Notes = expense.Notes;

        var existingSplits = await dbContext.ExpenseSplits
            .Where(s => s.ExpenseId == expense.Id)
            .ToListAsync(ct);

        if (existingSplits.Count > 0)
        {
            dbContext.ExpenseSplits.RemoveRange(existingSplits);
        }

        var replacementSplits = newSplits.Select(s => new ExpenseSplit
        {
            Id = Guid.NewGuid(),
            ExpenseId = expense.Id,
            UserId = s.UserId,
            ShareRatioOrWeight = s.ShareRatioOrWeight,
            OwedAmount = s.OwedAmount
        }).ToList();

        if (replacementSplits.Count > 0)
        {
            dbContext.ExpenseSplits.AddRange(replacementSplits);
        }

        await dbContext.SaveChangesAsync(ct);
    }

    public async Task ReplaceSplitsAsync(
        Guid expenseId,
        IEnumerable<ExpenseSplit> newSplits,
        ReceiptScanStatus? updatedScanStatus = null,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(newSplits);

        if (updatedScanStatus.HasValue)
        {
            var tracked = await dbContext.Expenses
                .FirstOrDefaultAsync(e => e.Id == expenseId, ct)
                ?? throw new KeyNotFoundException("Không tìm thấy hóa đơn chi tiêu.");

            tracked.ScanStatus = updatedScanStatus.Value;
        }

        var existingSplits = await dbContext.ExpenseSplits
            .Where(s => s.ExpenseId == expenseId)
            .ToListAsync(ct);

        if (existingSplits.Count > 0)
        {
            dbContext.ExpenseSplits.RemoveRange(existingSplits);
        }

        var replacementSplits = newSplits.Select(s => new ExpenseSplit
        {
            Id = Guid.NewGuid(),
            ExpenseId = expenseId,
            UserId = s.UserId,
            ShareRatioOrWeight = s.ShareRatioOrWeight,
            OwedAmount = s.OwedAmount
        }).ToList();

        if (replacementSplits.Count > 0)
        {
            dbContext.ExpenseSplits.AddRange(replacementSplits);
        }

        await dbContext.SaveChangesAsync(ct);
    }

    public async Task DeleteAsync(Guid expenseId, CancellationToken ct = default)
    {
        var tracked = await dbContext.Expenses
            .FirstOrDefaultAsync(e => e.Id == expenseId, ct);

        if (tracked is null)
        {
            return;
        }

        var itemIds = await dbContext.ReceiptItems
            .Where(i => i.ExpenseId == expenseId)
            .Select(i => i.Id)
            .ToListAsync(ct);

        if (itemIds.Count > 0)
        {
            var claims = await dbContext.ReceiptItemClaims
                .Where(c => itemIds.Contains(c.ReceiptItemId))
                .ToListAsync(ct);
            if (claims.Count > 0)
            {
                dbContext.ReceiptItemClaims.RemoveRange(claims);
            }

            var items = await dbContext.ReceiptItems
                .Where(i => i.ExpenseId == expenseId)
                .ToListAsync(ct);
            dbContext.ReceiptItems.RemoveRange(items);
        }

        var splits = await dbContext.ExpenseSplits
            .Where(s => s.ExpenseId == expenseId)
            .ToListAsync(ct);
        if (splits.Count > 0)
        {
            dbContext.ExpenseSplits.RemoveRange(splits);
        }

        dbContext.Expenses.Remove(tracked);
        await dbContext.SaveChangesAsync(ct);
    }
}
