using SplitTrip.BusinessObjects.Entities;
using SplitTrip.BusinessObjects.Enums;
using SplitTrip.DataAccessObjects.Daos.Expenses;

namespace SplitTrip.Repositories.Expenses;

public sealed class ReceiptRepository(ReceiptDao receiptDao) : IReceiptRepository
{
    public Task CreateReceiptDraftAsync(
        Expense draftExpense,
        IEnumerable<ReceiptItem> items,
        CancellationToken ct = default)
        => receiptDao.CreateReceiptDraftAsync(draftExpense, items, ct);

    public Task<Expense?> GetExpenseWithReceiptDetailsAsync(Guid expenseId, CancellationToken ct = default)
        => receiptDao.GetExpenseWithReceiptDetailsAsync(expenseId, ct);

    public Task UpdateReceiptDraftAsync(
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
        => receiptDao.UpdateReceiptDraftAsync(
            expenseId,
            title,
            category,
            paidByUserId,
            spentAtUtc,
            notes,
            subtotalAmount,
            taxOrTipAmount,
            discountAmount,
            totalAmount,
            updatedItems,
            ct);

    public Task<ReceiptItem?> GetReceiptItemWithExpenseAsync(Guid receiptItemId, CancellationToken ct = default)
        => receiptDao.GetReceiptItemWithExpenseAsync(receiptItemId, ct);

    public Task<bool> ToggleClaimAsync(Guid receiptItemId, Guid userId, CancellationToken ct = default)
        => receiptDao.ToggleClaimAsync(receiptItemId, userId, ct);

    public Task<IReadOnlyList<ReceiptItem>> GetItemsWithClaimsAsync(Guid expenseId, CancellationToken ct = default)
        => receiptDao.GetItemsWithClaimsAsync(expenseId, ct);
}
