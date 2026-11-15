using SplitTrip.BusinessObjects.Entities;
using SplitTrip.BusinessObjects.Enums;

namespace SplitTrip.Repositories.Expenses;

public interface IExpenseRepository
{
    Task<Expense?> GetByIdAsync(Guid expenseId, CancellationToken ct = default);
    Task<IReadOnlyList<Expense>> GetByTripAsync(
        Guid tripId,
        ExpenseCategory? category = null,
        bool includeDrafts = true,
        CancellationToken ct = default);
    Task AddExpenseWithSplitsAsync(Expense expense, IEnumerable<ExpenseSplit> splits, CancellationToken ct = default);
    Task UpdateExpenseWithSplitsAsync(Expense expense, IEnumerable<ExpenseSplit> newSplits, CancellationToken ct = default);
    Task ReplaceSplitsAsync(
        Guid expenseId,
        IEnumerable<ExpenseSplit> newSplits,
        ReceiptScanStatus? updatedScanStatus = null,
        CancellationToken ct = default);
    Task DeleteAsync(Guid expenseId, CancellationToken ct = default);
}
