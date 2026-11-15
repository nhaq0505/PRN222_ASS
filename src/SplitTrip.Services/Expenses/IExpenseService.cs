using SplitTrip.BusinessObjects.Enums;
using SplitTrip.Services.Expenses.Dtos;

namespace SplitTrip.Services.Expenses;

public interface IExpenseService
{
    Task<TripExpensesOverviewDto> GetTripExpensesOverviewAsync(
        Guid tripId,
        Guid viewerUserId,
        ExpenseCategory? categoryFilter = null,
        bool isAdmin = false,
        CancellationToken ct = default);

    Task<ExpenseSummaryDto> GetExpenseByIdAsync(
        Guid expenseId,
        Guid viewerUserId,
        bool isAdmin = false,
        CancellationToken ct = default);

    Task<ExpenseSummaryDto> CreateExpenseAsync(
        Guid actorUserId,
        UpsertExpenseRequest request,
        CancellationToken ct = default);

    Task<ExpenseSummaryDto> UpdateExpenseAsync(
        Guid expenseId,
        Guid actorUserId,
        UpsertExpenseRequest request,
        CancellationToken ct = default);

    Task<Guid> DeleteExpenseAsync(
        Guid expenseId,
        Guid actorUserId,
        CancellationToken ct = default);

    Task<ReceiptDraftDto> GetReceiptDraftAsync(
        Guid expenseId,
        Guid viewerUserId,
        bool isAdmin = false,
        CancellationToken ct = default);

    Task<ReceiptDraftDto> UpdateReceiptDraftAsync(
        Guid actorUserId,
        UpdateReceiptDraftRequest request,
        CancellationToken ct = default);

    Task<ClaimingBoardDto> ConfirmDraftAndOpenClaimingAsync(
        Guid expenseId,
        Guid actorUserId,
        UpdateReceiptDraftRequest? finalEdits = null,
        CancellationToken ct = default);

    Task<ClaimingBoardDto> GetClaimingBoardAsync(
        Guid expenseId,
        Guid viewerUserId,
        bool isAdmin = false,
        CancellationToken ct = default);

    Task<ClaimingBoardDto> ToggleItemClaimAsync(
        Guid expenseId,
        Guid receiptItemId,
        Guid userId,
        CancellationToken ct = default);

    Task<ClaimingBoardDto> FinalizeReceiptClaimsAsync(
        Guid expenseId,
        Guid actorUserId,
        CancellationToken ct = default);
}
