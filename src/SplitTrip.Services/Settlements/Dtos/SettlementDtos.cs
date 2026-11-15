using SplitTrip.BusinessObjects.Enums;
using SplitTrip.Services.Notifications.Dtos;
using SplitTrip.Services.Trips.Dtos;

namespace SplitTrip.Services.Settlements.Dtos;

public sealed record VietQrPaymentInfoDto(
    bool IsBankConfigured,
    string? QrImageUrl,
    string? BankBin,
    string? BankCode,
    string? BankShortName,
    string? BankFullName,
    string? AccountNumber,
    string? AccountHolder,
    decimal Amount,
    string TransferNote,
    string? MissingBankMessage);

public sealed record SettlementCardDto(
    Guid Id,
    Guid TripId,
    Guid FromUserId,
    string FromUserName,
    string FromUserEmail,
    Guid ToUserId,
    string ToUserName,
    string ToUserEmail,
    decimal Amount,
    SettlementStatus Status,
    string TransferNote,
    string? ProofNote,
    string? DisputeReason,
    DateTime CreatedAtUtc,
    DateTime? MarkedPaidAtUtc,
    DateTime? ConfirmedAtUtc,
    DateTime? LastNudgedAtUtc,
    VietQrPaymentInfoDto VietQr);

public sealed record TripSettlementWorkspaceDto(
    TripDetailsDto Trip,
    decimal LedgerNetSum,
    bool IsZeroSumBalanced,
    int RawExpenseObligationCount,
    int NaivePairwiseCount,
    int SimplifiedTransferCount,
    int EliminatedTransfersCount,
    double ReductionPercentage,
    decimal TotalConfirmedAmount,
    decimal TotalRemainingAmount,
    IReadOnlyList<SettlementCardDto> ActiveSettlements,
    IReadOnlyList<SettlementCardDto> ConfirmedHistory);

public sealed record SettlementActionResultDto(
    SettlementCardDto Settlement,
    TripStatus NewTripStatus,
    bool TripCompleted,
    Guid NotificationTargetUserId,
    NotificationDto Notification);

public sealed record SettlementNudgeResultDto(
    Guid SettlementId,
    Guid TripId,
    Guid TargetUserId,
    DateTime NudgedAtUtc,
    NotificationDto Notification);

public sealed record AutoSettledTripEventDto(
    Guid TripId,
    string TripName,
    int GeneratedSettlementCount,
    IReadOnlyList<(Guid UserId, NotificationDto Notification)> MemberNotifications);

public sealed record SettlementAutomationCycleResultDto(
    IReadOnlyList<AutoSettledTripEventDto> AutoSettledTrips,
    IReadOnlyList<SettlementNudgeResultDto> OverdueNudges);
