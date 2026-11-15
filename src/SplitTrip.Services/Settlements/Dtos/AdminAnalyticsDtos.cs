using SplitTrip.BusinessObjects.Enums;

namespace SplitTrip.Services.Settlements.Dtos;

public sealed record AdminKpiSummaryDto(
    int TotalRegisteredUsers,
    int NewUsersInRange,
    int TotalTrips,
    int PlanningTrips,
    int OngoingTrips,
    int SettlingTrips,
    int CompletedTrips,
    int FinalizedExpenseCount,
    decimal TotalFinalizedVolumeVnd,
    decimal ConfirmedSettledVnd,
    decimal PendingSettlementVnd,
    decimal MarkedPaidSettlementVnd,
    decimal DisputedSettlementVnd,
    int AiScannedReceiptsCount,
    int TotalItineraryStops,
    int AiSuggestedStopsCount,
    int TotalRawSplitObligations,
    int TotalSimplifiedTransfers,
    int TotalEliminatedTransfers,
    double SimplificationEfficiencyPercent);

public sealed record AdminCategoryBreakdownDto(
    ExpenseCategory Category,
    string CategoryLabel,
    int ExpenseCount,
    decimal TotalAmountVnd,
    double SharePercentage);

public sealed record AdminSettlementStatusSummaryDto(
    int ConfirmedCount,
    decimal ConfirmedAmountVnd,
    int MarkedPaidCount,
    decimal MarkedPaidAmountVnd,
    int PendingCount,
    decimal PendingAmountVnd,
    int DisputedCount,
    decimal DisputedAmountVnd);

public sealed record AdminTripReportRowDto(
    Guid TripId,
    string TripName,
    string Destination,
    string InviteCode,
    string OwnerName,
    DateOnly StartDate,
    DateOnly EndDate,
    DateTime CreatedAtUtc,
    TripStatus Status,
    int ActiveMemberCount,
    int FinalizedExpenseCount,
    int AiReceiptCount,
    decimal TotalSpendVnd,
    decimal ConfirmedSettledVnd,
    decimal PendingSettlementVnd,
    int RawObligationCount,
    int SimplifiedTransferCount,
    int EliminatedTransfersCount);

public sealed record AdminActivityItemDto(
    string Kind,
    string BadgeLabel,
    string BadgeCss,
    string Title,
    string Subtitle,
    decimal? AmountVnd,
    DateTime OccurredAtUtc);

public sealed record AdminDashboardDto(
    DateOnly? StartDate,
    DateOnly? EndDate,
    DateTime GeneratedAtUtc,
    AdminKpiSummaryDto Kpis,
    AdminSettlementStatusSummaryDto SettlementSummary,
    IReadOnlyList<AdminCategoryBreakdownDto> CategoryBreakdown,
    IReadOnlyList<AdminTripReportRowDto> TripReportRows,
    IReadOnlyList<AdminActivityItemDto> RecentActivities);
