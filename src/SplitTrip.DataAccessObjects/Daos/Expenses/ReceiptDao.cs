using Microsoft.EntityFrameworkCore;
using SplitTrip.BusinessObjects.Entities;
using SplitTrip.BusinessObjects.Enums;

namespace SplitTrip.DataAccessObjects.Daos.Expenses;

public sealed class ReceiptDao(SplitTripDbContext dbContext)
{
    public async Task CreateReceiptDraftAsync(
        Expense draftExpense,
        IEnumerable<ReceiptItem> items,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(draftExpense);
        ArgumentNullException.ThrowIfNull(items);

        draftExpense.SplitMode = ExpenseSplitMode.Itemized;
        draftExpense.ScanStatus = ReceiptScanStatus.DraftReview;
        draftExpense.Splits = new List<ExpenseSplit>();
        draftExpense.ReceiptItems = new List<ReceiptItem>();

        var itemEntities = items
            .Select((item, index) => new ReceiptItem
            {
                Id = item.Id == Guid.Empty ? Guid.NewGuid() : item.Id,
                ExpenseId = draftExpense.Id,
                ItemName = item.ItemName,
                Quantity = item.Quantity,
                UnitPrice = item.UnitPrice,
                LineTotal = item.LineTotal,
                SortOrder = item.SortOrder >= 0 ? item.SortOrder : index
            })
            .ToList();

        dbContext.Expenses.Add(draftExpense);
        if (itemEntities.Count > 0)
        {
            dbContext.ReceiptItems.AddRange(itemEntities);
        }

        await dbContext.SaveChangesAsync(ct);
    }

    public Task<Expense?> GetExpenseWithReceiptDetailsAsync(Guid expenseId, CancellationToken ct = default)
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

    public async Task UpdateReceiptDraftAsync(
        Guid expenseId,
        string title,
        ExpenseCategory category,
        Guid paidByUserId,
        DateTime spentAtUtc,
        string? notes,
        decimal subtotalAmount,
        decimal taxOrTipAmount,
        decimal discountAmount,
        decimal totalAmount,
        IEnumerable<ReceiptItem> updatedItems,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(updatedItems);

        var tracked = await dbContext.Expenses
            .FirstOrDefaultAsync(e => e.Id == expenseId, ct)
            ?? throw new KeyNotFoundException("Không tìm thấy bản nháp hóa đơn cần cập nhật.");

        tracked.Title = title;
        tracked.Category = category;
        tracked.PaidByUserId = paidByUserId;
        tracked.SpentAtUtc = spentAtUtc;
        tracked.Notes = notes;
        tracked.SubtotalAmount = subtotalAmount;
        tracked.TaxOrTipAmount = taxOrTipAmount;
        tracked.DiscountAmount = discountAmount;
        tracked.TotalAmount = totalAmount;

        var existingItemIds = await dbContext.ReceiptItems
            .Where(i => i.ExpenseId == expenseId)
            .Select(i => i.Id)
            .ToListAsync(ct);

        if (existingItemIds.Count > 0)
        {
            var existingClaims = await dbContext.ReceiptItemClaims
                .Where(c => existingItemIds.Contains(c.ReceiptItemId))
                .ToListAsync(ct);
            if (existingClaims.Count > 0)
            {
                dbContext.ReceiptItemClaims.RemoveRange(existingClaims);
            }

            var existingItems = await dbContext.ReceiptItems
                .Where(i => i.ExpenseId == expenseId)
                .ToListAsync(ct);
            dbContext.ReceiptItems.RemoveRange(existingItems);
        }

        var replacementItems = updatedItems
            .Select((item, idx) => new ReceiptItem
            {
                Id = Guid.NewGuid(),
                ExpenseId = expenseId,
                ItemName = item.ItemName,
                Quantity = item.Quantity,
                UnitPrice = item.UnitPrice,
                LineTotal = item.LineTotal,
                SortOrder = idx
            })
            .ToList();

        if (replacementItems.Count > 0)
        {
            dbContext.ReceiptItems.AddRange(replacementItems);
        }

        await dbContext.SaveChangesAsync(ct);
    }

    public Task<ReceiptItem?> GetReceiptItemWithExpenseAsync(Guid receiptItemId, CancellationToken ct = default)
        => dbContext.ReceiptItems
            .AsNoTracking()
            .Include(i => i.Expense)
            .Include(i => i.Claims)
                .ThenInclude(c => c.User)
            .FirstOrDefaultAsync(i => i.Id == receiptItemId, ct);

    public async Task<bool> ToggleClaimAsync(Guid receiptItemId, Guid userId, CancellationToken ct = default)
    {
        var existing = await dbContext.ReceiptItemClaims
            .FirstOrDefaultAsync(c => c.ReceiptItemId == receiptItemId && c.UserId == userId, ct);

        if (existing is not null)
        {
            dbContext.ReceiptItemClaims.Remove(existing);
            await dbContext.SaveChangesAsync(ct);
            return false;
        }

        dbContext.ReceiptItemClaims.Add(new ReceiptItemClaim
        {
            Id = Guid.NewGuid(),
            ReceiptItemId = receiptItemId,
            UserId = userId,
            ClaimedAtUtc = DateTime.UtcNow
        });

        await dbContext.SaveChangesAsync(ct);
        return true;
    }

    public async Task<IReadOnlyList<ReceiptItem>> GetItemsWithClaimsAsync(
        Guid expenseId,
        CancellationToken ct = default)
        => await dbContext.ReceiptItems
            .AsNoTracking()
            .Include(i => i.Claims)
                .ThenInclude(c => c.User)
            .Where(i => i.ExpenseId == expenseId)
            .OrderBy(i => i.SortOrder)
            .ThenBy(i => i.Id)
            .ToListAsync(ct);
}
