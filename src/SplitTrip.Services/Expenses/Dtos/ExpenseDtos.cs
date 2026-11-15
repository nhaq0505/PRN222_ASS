using SplitTrip.BusinessObjects.Enums;

namespace SplitTrip.Services.Expenses.Dtos;

public sealed record ParticipantShareInput(
    Guid UserId,
    decimal Value = 1m);

public sealed record ComputedSplitShare(
    Guid UserId,
    decimal ShareRatioOrWeight,
    decimal OwedAmount);

public sealed record UpsertExpenseRequest(
    Guid TripId,
    Guid PaidByUserId,
    string Title,
    ExpenseCategory Category,
    decimal TotalAmount,
    ExpenseSplitMode SplitMode,
    IReadOnlyList<ParticipantShareInput> Participants,
    DateTime? SpentAtUtc = null,
    string? Notes = null,
    decimal? SubtotalAmount = null,
    decimal TaxOrTipAmount = 0m,
    decimal DiscountAmount = 0m);

public sealed record ExpenseSplitDto(
    Guid UserId,
    string FullName,
    decimal ShareRatioOrWeight,
    decimal OwedAmount);

public sealed record ExpenseSummaryDto(
    Guid Id,
    Guid TripId,
    Guid PaidByUserId,
    string PaidByName,
    string Title,
    ExpenseCategory Category,
    decimal SubtotalAmount,
    decimal TaxOrTipAmount,
    decimal DiscountAmount,
    decimal TotalAmount,
    ExpenseSplitMode SplitMode,
    ReceiptScanStatus ScanStatus,
    string? ReceiptImageUrl,
    DateTime SpentAtUtc,
    string? Notes,
    DateTime CreatedAtUtc,
    int ReceiptItemCount,
    int UnclaimedItemCount,
    IReadOnlyList<ExpenseSplitDto> Splits);

public sealed record TripExpensesOverviewDto(
    Guid TripId,
    decimal TotalConfirmedSpend,
    int ConfirmedExpenseCount,
    int DraftReceiptCount,
    int OpenClaimingCount,
    IReadOnlyDictionary<ExpenseCategory, decimal> SpendByCategory,
    IReadOnlyList<ExpenseSummaryDto> Expenses);
