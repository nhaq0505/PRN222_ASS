using SplitTrip.BusinessObjects.Entities;
using SplitTrip.BusinessObjects.Enums;

namespace SplitTrip.Repositories.Expenses;

public interface IReceiptRepository
{
    Task CreateReceiptDraftAsync(Expense draftExpense, IEnumerable<ReceiptItem> items, CancellationToken ct = default);
    Task<Expense?> GetExpenseWithReceiptDetailsAsync(Guid expenseId, CancellationToken ct = default);
    Task UpdateReceiptDraftAsync(
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
        CancellationToken ct = default);
    Task<ReceiptItem?> GetReceiptItemWithExpenseAsync(Guid receiptItemId, CancellationToken ct = default);
    Task<bool> ToggleClaimAsync(Guid receiptItemId, Guid userId, CancellationToken ct = default);
    Task<IReadOnlyList<ReceiptItem>> GetItemsWithClaimsAsync(Guid expenseId, CancellationToken ct = default);
}
