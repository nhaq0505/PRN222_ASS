using SplitTrip.BusinessObjects.Entities;
using SplitTrip.BusinessObjects.Enums;
using SplitTrip.Repositories.Settlements;
using SplitTrip.Repositories.Trips;
using SplitTrip.Services.Notifications;
using SplitTrip.Services.Notifications.Dtos;
using SplitTrip.Services.Settlements.Dtos;
using SplitTrip.Services.Trips;

namespace SplitTrip.Services.Settlements;

public sealed class SettlementService(
    ISettlementRepository settlementRepository,
    ITripRepository tripRepository,
    ITripService tripService,
    IDebtSimplificationService debtSimplificationService,
    IVietQrService vietQrService,
    INotificationService notificationService) : ISettlementService
{
    private const int MaxProofNoteLength = 500;
    private const int MaxDisputeReasonLength = 500;

    public async Task<TripSettlementWorkspaceDto> GetWorkspaceAsync(
        Guid tripId,
        Guid viewerUserId,
        bool isAdmin = false,
        bool syncPendingPlan = true,
        CancellationToken ct = default)
    {
        var tripDetails = await tripService.GetTripDetailsAsync(tripId, viewerUserId, isAdmin, ct);

        if (syncPendingPlan && tripDetails.Status != TripStatus.Completed)
        {
            await SyncSettlementPlanAsync(tripId, ct);
            tripDetails = await tripService.GetTripDetailsAsync(tripId, viewerUserId, isAdmin, ct);
        }

        var rawObligationCount = await settlementRepository.GetRawExpenseObligationCountAsync(tripId, ct);
        var allSettlements = await settlementRepository.GetByTripIdAsync(tripId, ct);

        // Compute gross simplification efficiency across total trip expenses (before confirmed settlements)
        var grossBalanceInputs = tripDetails.MemberBalances
            .Select(m => new MemberBalanceInput(
                m.UserId,
                m.FullName,
                m.TotalPaid - m.TotalOwed))
            .ToList();

        var grossSum = grossBalanceInputs.Sum(x => x.NetBalance);
        var simplificationStats = grossSum == 0m
            ? debtSimplificationService.Simplify(grossBalanceInputs, rawObligationCount)
            : new DebtSimplificationResultDto([], 0, rawObligationCount, 0, 0, 0m);

        var cards = allSettlements
            .Select(MapToCardDto)
            .ToList();

        var activeCards = cards
            .Where(c => c.Status != SettlementStatus.Confirmed)
            .OrderBy(c => StatusSortRank(c.Status))
            .ThenByDescending(c => c.Amount)
            .ThenBy(c => c.CreatedAtUtc)
            .ToList();

        var confirmedHistory = cards
            .Where(c => c.Status == SettlementStatus.Confirmed)
            .OrderByDescending(c => c.ConfirmedAtUtc ?? c.CreatedAtUtc)
            .ToList();

        var ledgerNetSum = tripDetails.MemberBalances.Sum(b => b.NetBalance);
        var totalConfirmed = confirmedHistory.Sum(c => c.Amount);
        var totalRemaining = activeCards.Sum(c => c.Amount);

        return new TripSettlementWorkspaceDto(
            tripDetails,
            ledgerNetSum,
            IsZeroSumBalanced: ledgerNetSum == 0m,
            rawObligationCount,
            simplificationStats.NaivePairwiseCount,
            simplificationStats.SimplifiedTransferCount,
            simplificationStats.EliminatedTransfersCount,
            simplificationStats.ReductionPercentage,
            totalConfirmed,
            totalRemaining,
            activeCards,
            confirmedHistory);
    }

    public async Task<IReadOnlyList<SettlementCardDto>> SyncSettlementPlanAsync(
        Guid tripId,
        CancellationToken ct = default)
    {
        var trip = await tripRepository.GetByIdAsync(tripId, ct)
            ?? throw new KeyNotFoundException("Không tìm thấy chuyến đi.");

        if (trip.Status == TripStatus.Completed)
        {
            var completedRows = await settlementRepository.GetByTripIdAsync(tripId, ct);
            return completedRows.Select(MapToCardDto).ToList();
        }

        var memberBalances = await tripService.GetTripMemberBalancesAsync(tripId, ct);
        var existingSettlements = await settlementRepository.GetByTripIdAsync(tripId, ct);

        // Account for open MarkedPaid and Disputed transfers so nobody is ever double-billed.
        var reservedInFlight = existingSettlements
            .Where(s => s.Status is SettlementStatus.MarkedPaid or SettlementStatus.Disputed)
            .ToList();

        var outgoingInFlight = reservedInFlight
            .GroupBy(s => s.FromUserId)
            .ToDictionary(g => g.Key, g => g.Sum(x => x.Amount));

        var incomingInFlight = reservedInFlight
            .GroupBy(s => s.ToUserId)
            .ToDictionary(g => g.Key, g => g.Sum(x => x.Amount));

        var residualMap = new Dictionary<Guid, (string FullName, decimal Residual)>();
        foreach (var member in memberBalances)
        {
            var outAmt = outgoingInFlight.GetValueOrDefault(member.UserId, 0m);
            var inAmt = incomingInFlight.GetValueOrDefault(member.UserId, 0m);
            residualMap[member.UserId] = (member.FullName, member.NetBalance + outAmt - inAmt);
        }

        // Also cover any inactive user referenced in an in-flight row
        foreach (var inflight in reservedInFlight)
        {
            if (!residualMap.ContainsKey(inflight.FromUserId))
            {
                residualMap[inflight.FromUserId] = (
                    inflight.FromUser?.FullName ?? "Thành viên",
                    outgoingInFlight.GetValueOrDefault(inflight.FromUserId, 0m)
                    - incomingInFlight.GetValueOrDefault(inflight.FromUserId, 0m));
            }

            if (!residualMap.ContainsKey(inflight.ToUserId))
            {
                residualMap[inflight.ToUserId] = (
                    inflight.ToUser?.FullName ?? "Thành viên",
                    outgoingInFlight.GetValueOrDefault(inflight.ToUserId, 0m)
                    - incomingInFlight.GetValueOrDefault(inflight.ToUserId, 0m));
            }
        }

        var residualInputs = residualMap
            .Select(kvp => new MemberBalanceInput(kvp.Key, kvp.Value.FullName, kvp.Value.Residual))
            .ToList();

        var simplified = debtSimplificationService.Simplify(residualInputs);
        var existingPending = existingSettlements
            .Where(s => s.Status == SettlementStatus.Pending)
            .ToList();

        if (!ArePendingPlansIdentical(existingPending, simplified.Transfers))
        {
            var nowUtc = DateTime.UtcNow;
            var reusableByPair = existingPending
                .GroupBy(p => (p.FromUserId, p.ToUserId))
                .ToDictionary(g => g.Key, g => new Queue<Settlement>(g));

            var newPendingEntities = new List<Settlement>(simplified.Transfers.Count);
            foreach (var transfer in simplified.Transfers)
            {
                Settlement? prior = null;
                if (reusableByPair.TryGetValue((transfer.FromUserId, transfer.ToUserId), out var queue) && queue.Count > 0)
                {
                    prior = queue.Dequeue();
                }

                newPendingEntities.Add(new Settlement
                {
                    Id = prior?.Id ?? Guid.NewGuid(),
                    TripId = tripId,
                    FromUserId = transfer.FromUserId,
                    ToUserId = transfer.ToUserId,
                    Amount = transfer.Amount,
                    Status = SettlementStatus.Pending,
                    TransferNote = prior?.TransferNote is { Length: > 0 } existingNote
                        ? existingNote
                        : vietQrService.BuildTransferNote(trip.InviteCode, transfer.FromUserName),
                    CreatedAtUtc = prior?.CreatedAtUtc ?? nowUtc,
                    LastNudgedAtUtc = prior is not null && prior.Amount == transfer.Amount
                        ? prior.LastNudgedAtUtc
                        : null
                });
            }

            await settlementRepository.ReplacePendingSettlementsAsync(tripId, newPendingEntities, ct);
            existingSettlements = await settlementRepository.GetByTripIdAsync(tripId, ct);
        }

        return existingSettlements
            .Where(s => s.Status != SettlementStatus.Confirmed)
            .OrderBy(s => StatusSortRank(s.Status))
            .ThenByDescending(s => s.Amount)
            .Select(MapToCardDto)
            .ToList();
    }

    public async Task<SettlementActionResultDto> MarkAsPaidAsync(
        Guid settlementId,
        Guid actorUserId,
        string? proofNote = null,
        CancellationToken ct = default)
    {
        var settlement = await settlementRepository.GetByIdAsync(settlementId, ct)
            ?? throw new KeyNotFoundException("Không tìm thấy giao dịch tất toán.");

        if (settlement.FromUserId != actorUserId)
        {
            throw new UnauthorizedAccessException("Chỉ người có nghĩa vụ thanh toán mới được đánh dấu đã chuyển khoản.");
        }

        if (settlement.Status is not (SettlementStatus.Pending or SettlementStatus.Disputed))
        {
            throw new InvalidOperationException(
                $"Không thể đánh dấu đã chuyển khoản cho giao dịch đang ở trạng thái '{settlement.Status}'.");
        }

        if (settlement.Trip?.Status == TripStatus.Completed)
        {
            throw new InvalidOperationException("Chuyến đi đã hoàn tất và đóng sổ cái.");
        }

        var trimmedProof = string.IsNullOrWhiteSpace(proofNote) ? null : proofNote.Trim();
        if (trimmedProof is not null && trimmedProof.Length > MaxProofNoteLength)
        {
            trimmedProof = trimmedProof[..MaxProofNoteLength];
        }

        settlement.Status = SettlementStatus.MarkedPaid;
        settlement.ProofNote = trimmedProof;
        settlement.MarkedPaidAtUtc = DateTime.UtcNow;
        settlement.DisputeReason = null;

        await settlementRepository.UpdateAsync(settlement, ct);

        var debtorName = settlement.FromUser?.FullName ?? "Thành viên";
        var tripName = settlement.Trip?.Name ?? "chuyến đi";

        var notification = await notificationService.CreateAsync(
            settlement.ToUserId,
            settlement.TripId,
            NotificationType.SettlementUpdate,
            "Chờ bạn xác nhận đã nhận tiền",
            $"{debtorName} vừa báo đã chuyển {settlement.Amount:N0}đ trong \"{tripName}\". Vui lòng kiểm tra tài khoản và xác nhận.",
            $"/Trips/Settlement?tripId={settlement.TripId}",
            ct);

        return new SettlementActionResultDto(
            MapToCardDto(settlement),
            settlement.Trip?.Status ?? TripStatus.Settling,
            TripCompleted: false,
            settlement.ToUserId,
            notification);
    }

    public async Task<SettlementActionResultDto> ConfirmReceivedAsync(
        Guid settlementId,
        Guid actorUserId,
        CancellationToken ct = default)
    {
        var settlement = await settlementRepository.GetByIdAsync(settlementId, ct)
            ?? throw new KeyNotFoundException("Không tìm thấy giao dịch tất toán.");

        if (settlement.ToUserId != actorUserId)
        {
            throw new UnauthorizedAccessException("Chỉ người thụ hưởng (chủ nợ) mới có quyền xác nhận đã nhận tiền.");
        }

        if (settlement.Status != SettlementStatus.MarkedPaid)
        {
            throw new InvalidOperationException("Chỉ có thể xác nhận giao dịch đã được người chuyển đánh dấu thanh toán.");
        }

        settlement.Status = SettlementStatus.Confirmed;
        settlement.ConfirmedAtUtc = DateTime.UtcNow;
        settlement.DisputeReason = null;

        await settlementRepository.UpdateAsync(settlement, ct);

        // Refresh residual pending settlements after confirmation
        await SyncSettlementPlanAsync(settlement.TripId, ct);

        var trip = await tripRepository.GetByIdAsync(settlement.TripId, ct)
            ?? throw new KeyNotFoundException("Không tìm thấy chuyến đi.");

        var tripCompleted = false;
        if (trip.Status == TripStatus.Settling)
        {
            var balances = await tripService.GetTripMemberBalancesAsync(trip.Id, ct);
            var allZero = balances.All(b => b.NetBalance == 0m);
            var hasUnconfirmed = await tripRepository.HasUnconfirmedSettlementsAsync(trip.Id, ct);

            if (allZero && !hasUnconfirmed)
            {
                trip.Status = TripStatus.Completed;
                trip.ClosedAtUtc = DateTime.UtcNow;
                await tripRepository.UpdateAsync(trip, ct);
                tripCompleted = true;
            }
        }

        var creditorName = settlement.ToUser?.FullName ?? "Thành viên";
        var tripName = trip.Name;

        var notification = await notificationService.CreateAsync(
            settlement.FromUserId,
            settlement.TripId,
            NotificationType.SettlementUpdate,
            tripCompleted ? "Đã xác nhận nhận tiền & Hoàn tất chuyến đi!" : "Chủ nợ đã xác nhận nhận tiền",
            tripCompleted
                ? $"{creditorName} đã xác nhận nhận {settlement.Amount:N0}đ. Toàn bộ công nợ chuyến \"{tripName}\" đã về 0đ!"
                : $"{creditorName} đã xác nhận nhận đủ {settlement.Amount:N0}đ cho chuyến \"{tripName}\".",
            $"/Trips/Settlement?tripId={settlement.TripId}",
            ct);

        return new SettlementActionResultDto(
            MapToCardDto(settlement),
            trip.Status,
            tripCompleted,
            settlement.FromUserId,
            notification);
    }

    public async Task<SettlementActionResultDto> DisputeAsync(
        Guid settlementId,
        Guid actorUserId,
        string disputeReason,
        CancellationToken ct = default)
    {
        var trimmedReason = (disputeReason ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(trimmedReason) || trimmedReason.Length > MaxDisputeReasonLength)
        {
            throw new ArgumentException("Vui lòng nhập lý do chưa nhận được tiền (từ 1 đến 500 ký tự).", nameof(disputeReason));
        }

        var settlement = await settlementRepository.GetByIdAsync(settlementId, ct)
            ?? throw new KeyNotFoundException("Không tìm thấy giao dịch tất toán.");

        if (settlement.ToUserId != actorUserId)
        {
            throw new UnauthorizedAccessException("Chỉ người thụ hưởng (chủ nợ) mới có quyền báo chưa nhận được tiền.");
        }

        if (settlement.Status != SettlementStatus.MarkedPaid)
        {
            throw new InvalidOperationException("Chỉ có thể báo chưa nhận tiền đối với giao dịch đang chờ xác nhận.");
        }

        settlement.Status = SettlementStatus.Disputed;
        settlement.DisputeReason = trimmedReason;

        await settlementRepository.UpdateAsync(settlement, ct);

        var creditorName = settlement.ToUser?.FullName ?? "Thành viên";
        var tripName = settlement.Trip?.Name ?? "chuyến đi";

        var notification = await notificationService.CreateAsync(
            settlement.FromUserId,
            settlement.TripId,
            NotificationType.SettlementUpdate,
            "Giao dịch tất toán cần kiểm tra lại",
            $"{creditorName} báo chưa nhận được {settlement.Amount:N0}đ trong \"{tripName}\": \"{trimmedReason}\". Vui lòng kiểm tra và xác nhận lại.",
            $"/Trips/Settlement?tripId={settlement.TripId}",
            ct);

        return new SettlementActionResultDto(
            MapToCardDto(settlement),
            settlement.Trip?.Status ?? TripStatus.Settling,
            TripCompleted: false,
            settlement.FromUserId,
            notification);
    }

    public async Task<SettlementNudgeResultDto> NudgeDebtorAsync(
        Guid settlementId,
        Guid actorUserId,
        CancellationToken ct = default)
    {
        var settlement = await settlementRepository.GetByIdAsync(settlementId, ct)
            ?? throw new KeyNotFoundException("Không tìm thấy giao dịch tất toán.");

        var isCreditor = settlement.ToUserId == actorUserId;
        var isOwner = settlement.Trip?.OwnerUserId == actorUserId;

        if (!isCreditor && !isOwner)
        {
            throw new UnauthorizedAccessException("Chỉ người thụ hưởng hoặc trưởng đoàn mới có quyền gửi lời nhắc thanh toán.");
        }

        if (settlement.FromUserId == actorUserId)
        {
            throw new InvalidOperationException("Bạn không thể gửi lời nhắc thanh toán cho chính mình.");
        }

        if (settlement.Status is not (SettlementStatus.Pending or SettlementStatus.Disputed))
        {
            throw new InvalidOperationException("Chỉ có thể gửi lời nhắc cho khoản nợ chưa thanh toán hoặc đang cần chuyển lại.");
        }

        var nowUtc = DateTime.UtcNow;
        settlement.LastNudgedAtUtc = nowUtc;
        await settlementRepository.UpdateAsync(settlement, ct);

        var senderName = isCreditor
            ? (settlement.ToUser?.FullName ?? "Chủ nợ")
            : "Trưởng đoàn";
        var creditorName = settlement.ToUser?.FullName ?? "thành viên";
        var tripName = settlement.Trip?.Name ?? "chuyến đi";

        var notification = await notificationService.CreateAsync(
            settlement.FromUserId,
            settlement.TripId,
            NotificationType.DebtNudge,
            "Nhắc tất toán công nợ chuyến đi",
            $"{senderName} nhắc bạn tất toán khoản {settlement.Amount:N0}đ cho {creditorName} trong \"{tripName}\" qua mã VietQR.",
            $"/Trips/Settlement?tripId={settlement.TripId}",
            ct);

        return new SettlementNudgeResultDto(
            settlement.Id,
            settlement.TripId,
            settlement.FromUserId,
            nowUtc,
            notification);
    }

    public async Task<SettlementAutomationCycleResultDto> RunAutomationCycleAsync(
        DateTime utcNow,
        CancellationToken ct = default)
    {
        var normalizedUtc = utcNow.Kind == DateTimeKind.Utc
            ? utcNow
            : DateTime.SpecifyKind(utcNow, DateTimeKind.Utc);

        // Vietnam standard time is permanently UTC+07:00
        var todayVietnam = DateOnly.FromDateTime(normalizedUtc.AddHours(7));

        var autoSettledEvents = new List<AutoSettledTripEventDto>();
        var eligibleTrips = await settlementRepository.GetAutoSettleableTripsAsync(todayVietnam, ct);

        foreach (var trip in eligibleTrips)
        {
            trip.Status = TripStatus.Settling;
            await tripRepository.UpdateAsync(trip, ct);

            var activeSettlements = await SyncSettlementPlanAsync(trip.Id, ct);
            var memberNotifications = new List<(Guid UserId, NotificationDto Notification)>();

            foreach (var member in trip.Members.Where(m => m.IsActive))
            {
                var notif = await notificationService.CreateAsync(
                    member.UserId,
                    trip.Id,
                    NotificationType.SettlementUpdate,
                    "Chuyến đi chuyển sang giai đoạn Tất toán",
                    activeSettlements.Count > 0
                        ? $"Chuyến đi \"{trip.Name}\" đã kết thúc hành trình. Hệ thống đã tối ưu {activeSettlements.Count} giao dịch VietQR để tất toán."
                        : $"Chuyến đi \"{trip.Name}\" đã kết thúc hành trình với số dư công nợ cân bằng 0đ.",
                    $"/Trips/Settlement?tripId={trip.Id}",
                    ct);

                memberNotifications.Add((member.UserId, notif));
            }

            autoSettledEvents.Add(new AutoSettledTripEventDto(
                trip.Id,
                trip.Name,
                activeSettlements.Count,
                memberNotifications));
        }

        var overdueNudges = new List<SettlementNudgeResultDto>();
        var cutoffUtc = normalizedUtc.AddHours(-24);
        var overdueSettlements = await settlementRepository.GetOverdueSettlementsForNudgeAsync(cutoffUtc, ct);

        foreach (var settlement in overdueSettlements)
        {
            settlement.LastNudgedAtUtc = normalizedUtc;
            await settlementRepository.UpdateAsync(settlement, ct);

            var creditorName = settlement.ToUser?.FullName ?? "thành viên";
            var tripName = settlement.Trip?.Name ?? "chuyến đi";

            var notif = await notificationService.CreateAsync(
                settlement.FromUserId,
                settlement.TripId,
                NotificationType.DebtNudge,
                "Nhắc hẹn tất toán quá 24 giờ",
                $"Bạn còn khoản công nợ {settlement.Amount:N0}đ cần chuyển cho {creditorName} trong chuyến \"{tripName}\". Quét mã VietQR để hoàn tất nhanh!",
                $"/Trips/Settlement?tripId={settlement.TripId}",
                ct);

            overdueNudges.Add(new SettlementNudgeResultDto(
                settlement.Id,
                settlement.TripId,
                settlement.FromUserId,
                normalizedUtc,
                notif));
        }

        return new SettlementAutomationCycleResultDto(autoSettledEvents, overdueNudges);
    }

    private SettlementCardDto MapToCardDto(Settlement s)
    {
        var toUser = s.ToUser;
        var qr = vietQrService.BuildQuickLink(
            toUser?.BankBin,
            toUser?.BankCode,
            toUser?.BankAccountNumber,
            toUser?.BankAccountHolder,
            toUser?.FullName,
            s.Amount,
            s.TransferNote);

        return new SettlementCardDto(
            s.Id,
            s.TripId,
            s.FromUserId,
            s.FromUser?.FullName ?? "Thành viên",
            s.FromUser?.Email ?? string.Empty,
            s.ToUserId,
            toUser?.FullName ?? "Thành viên",
            toUser?.Email ?? string.Empty,
            s.Amount,
            s.Status,
            qr.TransferNote,
            s.ProofNote,
            s.DisputeReason,
            s.CreatedAtUtc,
            s.MarkedPaidAtUtc,
            s.ConfirmedAtUtc,
            s.LastNudgedAtUtc,
            qr);
    }

    private static bool ArePendingPlansIdentical(
        IReadOnlyList<Settlement> existingPending,
        IReadOnlyList<SimplifiedTransferDto> desiredTransfers)
    {
        if (existingPending.Count != desiredTransfers.Count)
        {
            return false;
        }

        var left = existingPending
            .OrderBy(x => x.FromUserId)
            .ThenBy(x => x.ToUserId)
            .ThenBy(x => x.Amount)
            .Select(x => (x.FromUserId, x.ToUserId, x.Amount))
            .ToList();

        var right = desiredTransfers
            .OrderBy(x => x.FromUserId)
            .ThenBy(x => x.ToUserId)
            .ThenBy(x => x.Amount)
            .Select(x => (x.FromUserId, x.ToUserId, x.Amount))
            .ToList();

        return left.SequenceEqual(right);
    }

    private static int StatusSortRank(SettlementStatus status) => status switch
    {
        SettlementStatus.Disputed => 0,
        SettlementStatus.MarkedPaid => 1,
        SettlementStatus.Pending => 2,
        SettlementStatus.Confirmed => 3,
        _ => 4
    };
}
