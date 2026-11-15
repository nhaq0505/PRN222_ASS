using SplitTrip.BusinessObjects.Enums;

namespace SplitTrip.Services.Expenses.Dtos;

public sealed record ScanReceiptRequest(
    Guid TripId,
    Guid ActorUserId,
    Guid? PaidByUserId = null,
    byte[]? ImageBytes = null,
    string? FileName = null,
    string? ContentType = null,
    string? ReceiptImageUrl = null,
    string? Notes = null,
    bool UseDemoSample = false);

public sealed record ReceiptDraftItemDto(
    Guid Id,
    string ItemName,
    int Quantity,
    decimal UnitPrice,
    decimal LineTotal,
    int SortOrder);

public sealed record ReceiptDraftDto(
    Guid ExpenseId,
    Guid TripId,
    Guid PaidByUserId,
    string PaidByName,
    string Title,
    ExpenseCategory Category,
    string? ReceiptImageUrl,
    ReceiptScanStatus ScanStatus,
    DateTime SpentAtUtc,
    string? Notes,
    decimal SubtotalAmount,
    decimal TaxOrTipAmount,
    decimal DiscountAmount,
    decimal TotalAmount,
    decimal ItemsSum,
    decimal SubtotalDelta,
    decimal ExpectedTotalAmount,
    decimal TotalDelta,
    bool HasMismatch,
    string? MismatchWarningMessage,
    bool IsDemoFallback,
    string? FallbackNotice,
    IReadOnlyList<ReceiptDraftItemDto> Items);

public sealed record DraftReceiptItemInput(
    string ItemName,
    int Quantity,
    decimal UnitPrice,
    decimal LineTotal);

public sealed record UpdateReceiptDraftRequest(
    Guid ExpenseId,
    string Title,
    ExpenseCategory Category,
    Guid PaidByUserId,
    decimal SubtotalAmount,
    decimal TaxOrTipAmount,
    decimal DiscountAmount,
    decimal TotalAmount,
    IReadOnlyList<DraftReceiptItemInput> Items,
    DateTime? SpentAtUtc = null,
    string? Notes = null);
