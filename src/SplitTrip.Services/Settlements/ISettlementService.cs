using SplitTrip.Services.Settlements.Dtos;

namespace SplitTrip.Services.Settlements;

public interface ISettlementService
{
    Task<TripSettlementWorkspaceDto> GetWorkspaceAsync(
        Guid tripId,
        Guid viewerUserId,
        bool isAdmin = false,
        bool syncPendingPlan = true,
        CancellationToken ct = default);

    Task<IReadOnlyList<SettlementCardDto>> SyncSettlementPlanAsync(
        Guid tripId,
        CancellationToken ct = default);

    Task<SettlementActionResultDto> MarkAsPaidAsync(
        Guid settlementId,
        Guid actorUserId,
        string? proofNote = null,
        CancellationToken ct = default);

    Task<SettlementActionResultDto> ConfirmReceivedAsync(
        Guid settlementId,
        Guid actorUserId,
        CancellationToken ct = default);

    Task<SettlementActionResultDto> DisputeAsync(
        Guid settlementId,
        Guid actorUserId,
        string disputeReason,
        CancellationToken ct = default);

    Task<SettlementNudgeResultDto> NudgeDebtorAsync(
        Guid settlementId,
        Guid actorUserId,
        CancellationToken ct = default);

    Task<SettlementAutomationCycleResultDto> RunAutomationCycleAsync(
        DateTime utcNow,
        CancellationToken ct = default);
}
