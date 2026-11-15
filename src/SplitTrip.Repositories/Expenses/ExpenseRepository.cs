using SplitTrip.BusinessObjects.Entities;
using SplitTrip.BusinessObjects.Enums;
using SplitTrip.DataAccessObjects.Daos.Expenses;

namespace SplitTrip.Repositories.Expenses;

public sealed class ExpenseRepository(ExpenseDao expenseDao) : IExpenseRepository
{
    public Task<Expense?> GetByIdAsync(Guid expenseId, CancellationToken ct = default)
        => expenseDao.GetByIdAsync(expenseId, ct);

    public Task<IReadOnlyList<Expense>> GetByTripAsync(
        Guid tripId,
        ExpenseCategory? category = null,
        bool includeDrafts = true,
        CancellationToken ct = default)
        => expenseDao.GetByTripAsync(tripId, category, includeDrafts, ct);

    public Task AddExpenseWithSplitsAsync(
        Expense expense,
        IEnumerable<ExpenseSplit> splits,
        CancellationToken ct = default)
        => expenseDao.AddExpenseWithSplitsAsync(expense, splits, ct);

    public Task UpdateExpenseWithSplitsAsync(
        Expense expense,
        IEnumerable<ExpenseSplit> newSplits,
        CancellationToken ct = default)
        => expenseDao.UpdateExpenseWithSplitsAsync(expense, newSplits, ct);

    public Task ReplaceSplitsAsync(
        Guid expenseId,
        IEnumerable<ExpenseSplit> newSplits,
        ReceiptScanStatus? updatedScanStatus = null,
        CancellationToken ct = default)
        => expenseDao.ReplaceSplitsAsync(expenseId, newSplits, updatedScanStatus, ct);

    public Task DeleteAsync(Guid expenseId, CancellationToken ct = default)
        => expenseDao.DeleteAsync(expenseId, ct);
}
