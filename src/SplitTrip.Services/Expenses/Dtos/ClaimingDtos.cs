using SplitTrip.BusinessObjects.Enums;

namespace SplitTrip.Services.Expenses.Dtos;

public sealed record ItemClaimerDto(
    Guid UserId,
    string FullName,
    string Initials,
    decimal DishShareVnd,
    DateTime ClaimedAtUtc);

public sealed record ClaimableReceiptItemDto(
    Guid Id,
    string ItemName,
    int Quantity,
    decimal UnitPrice,
    decimal LineTotal,
    int SortOrder,
    bool IsUnclaimed,
    bool IsClaimedByViewer,
    IReadOnlyList<ItemClaimerDto> Claimers);

public sealed record MemberClaimPreviewDto(
    Guid UserId,
    string FullName,
    string Initials,
    bool IsPayer,
    int ClaimedItemCount,
    decimal ClaimedDishSubtotal,
    decimal UnclaimedFallbackSubtotal,
    decimal EffectiveDishSubtotal,
    decimal ProRataAdjustmentVnd,
    decimal LedgerOwedAmount);

public sealed record ClaimingBoardDto(
    Guid ExpenseId,
    Guid TripId,
    string Title,
    ExpenseCategory Category,
    Guid PaidByUserId,
    string PaidByName,
    string? ReceiptImageUrl,
    ReceiptScanStatus ScanStatus,
    DateTime SpentAtUtc,
    string? Notes,
    decimal SubtotalAmount,
    decimal TaxOrTipAmount,
    decimal DiscountAmount,
    decimal TotalAmount,
    int TotalItemCount,
    int UnclaimedItemCount,
    decimal UnclaimedSubtotalAmount,
    bool AllItemsClaimed,
    bool CanManageLock,
    bool IsTripEditable,
    Guid? LastToggledItemId,
    Guid? LastActorUserId,
    string? LastActionMessage,
    IReadOnlyList<ClaimableReceiptItemDto> Items,
    IReadOnlyList<MemberClaimPreviewDto> MemberPreviews);

public sealed record ItemizedAllocationResult(
    IReadOnlyDictionary<Guid, IReadOnlyList<(Guid UserId, decimal DishShareVnd)>> PerItemShares,
    IReadOnlyDictionary<Guid, decimal> ClaimedSubtotalByUser,
    IReadOnlyDictionary<Guid, decimal> UnclaimedFallbackByUser,
    IReadOnlyList<ComputedSplitShare> ReconciledSplits);
