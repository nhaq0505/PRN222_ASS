using SplitTrip.BusinessObjects.Entities;
using SplitTrip.BusinessObjects.Enums;
using SplitTrip.Repositories.Settlements;
using SplitTrip.Services.Settlements.Dtos;

namespace SplitTrip.Services.Settlements;

public sealed class AdminAnalyticsService(
    IAdminAnalyticsRepository analyticsRepository,
    IDebtSimplificationService debtSimplificationService) : IAdminAnalyticsService
{
    public async Task<AdminDashboardDto> GetDashboardAsync(
        DateOnly? startDate = null,
        DateOnly? endDate = null,
        CancellationToken ct = default)
    {
        if (startDate.HasValue && endDate.HasValue && endDate.Value < startDate.Value)
        {
            throw new ArgumentException("Ngày kết thúc báo cáo không được nhỏ hơn ngày bắt đầu.");
        }

        var snapshot = await analyticsRepository.GetPlatformSnapshotAsync(startDate, endDate, ct);

        var expensesByTrip = snapshot.FinalizedExpenses
            .GroupBy(e => e.TripId)
            .ToDictionary(g => g.Key, g => g.ToList());

        var settlementsByTrip = snapshot.Settlements
            .GroupBy(s => s.TripId)
            .ToDictionary(g => g.Key, g => g.ToList());

        var reportRows = new List<AdminTripReportRowDto>(snapshot.FilteredTrips.Count);
        var totalRawObligations = 0;
        var totalSimplifiedTransfers = 0;
        var totalEliminatedTransfers = 0;

        foreach (var trip in snapshot.FilteredTrips)
        {
            var tripExpenses = expensesByTrip.GetValueOrDefault(trip.Id) ?? [];
            var tripSettlements = settlementsByTrip.GetValueOrDefault(trip.Id) ?? [];

            var tripSpend = tripExpenses.Sum(e => e.TotalAmount);
            var tripAiReceipts = tripExpenses.Count(IsAiScannedExpense);

            var confirmedAmt = tripSettlements
                .Where(s => s.Status == SettlementStatus.Confirmed)
                .Sum(s => s.Amount);

            var openSettlementAmt = tripSettlements
                .Where(s => s.Status != SettlementStatus.Confirmed)
                .Sum(s => s.Amount);

            var rawObligations = tripExpenses.Sum(e =>
                e.Splits.Count(s => s.UserId != e.PaidByUserId && s.OwedAmount > 0m));

            var grossNetByUser = ComputeGrossNetBalances(tripExpenses);
            var grossInputs = grossNetByUser
                .Select(kvp => new MemberBalanceInput(kvp.Key, "Member", kvp.Value))
                .ToList();

            var grossSum = grossInputs.Sum(x => x.NetBalance);
            var simResult = grossSum == 0m
                ? debtSimplificationService.Simplify(grossInputs, rawObligations)
                : new DebtSimplificationResultDto([], 0, rawObligations, 0, 0, 0m);

            // If settlements haven't been materialized yet, report residual unsettled net credit as pending volume
            var residualPendingVnd = openSettlementAmt > 0m
                ? openSettlementAmt
                : Math.Max(0m, simResult.TotalTransferVolume - confirmedAmt);

            totalRawObligations += simResult.NaivePairwiseCount;
            totalSimplifiedTransfers += simResult.SimplifiedTransferCount;
            totalEliminatedTransfers += simResult.EliminatedTransfersCount;

            reportRows.Add(new AdminTripReportRowDto(
                trip.Id,
                trip.Name,
                trip.Destination,
                trip.InviteCode,
                trip.OwnerUser?.FullName ?? "Trưởng đoàn",
                trip.StartDate,
                trip.EndDate,
                trip.CreatedAtUtc,
                trip.Status,
                trip.Members.Count(m => m.IsActive),
                tripExpenses.Count,
                tripAiReceipts,
                tripSpend,
                confirmedAmt,
                residualPendingVnd,
                simResult.NaivePairwiseCount,
                simResult.SimplifiedTransferCount,
                simResult.EliminatedTransfersCount));
        }

        var orderedReportRows = reportRows
            .OrderByDescending(r => r.TotalSpendVnd)
            .ThenByDescending(r => r.CreatedAtUtc)
            .ToList();

        var totalVolumeVnd = snapshot.FinalizedExpenses.Sum(e => e.TotalAmount);
        var confirmedSettlements = snapshot.Settlements.Where(s => s.Status == SettlementStatus.Confirmed).ToList();
        var markedPaidSettlements = snapshot.Settlements.Where(s => s.Status == SettlementStatus.MarkedPaid).ToList();
        var pendingSettlements = snapshot.Settlements.Where(s => s.Status == SettlementStatus.Pending).ToList();
        var disputedSettlements = snapshot.Settlements.Where(s => s.Status == SettlementStatus.Disputed).ToList();

        var confirmedVnd = confirmedSettlements.Sum(s => s.Amount);
        var markedPaidVnd = markedPaidSettlements.Sum(s => s.Amount);
        var pendingRowsVnd = pendingSettlements.Sum(s => s.Amount);
        var disputedVnd = disputedSettlements.Sum(s => s.Amount);

        var aggregatedPendingVnd = orderedReportRows.Sum(r => r.PendingSettlementVnd);
        var aiReceiptsTotal = snapshot.FinalizedExpenses.Count(IsAiScannedExpense);

        var efficiencyPercent = totalRawObligations > 0
            ? Math.Round((double)totalEliminatedTransfers * 100.0 / totalRawObligations, 1)
            : 0.0;

        var kpis = new AdminKpiSummaryDto(
            snapshot.TotalRegisteredUsers,
            snapshot.NewUsersInRange,
            snapshot.FilteredTrips.Count,
            snapshot.FilteredTrips.Count(t => t.Status == TripStatus.Planning),
            snapshot.FilteredTrips.Count(t => t.Status == TripStatus.Ongoing),
            snapshot.FilteredTrips.Count(t => t.Status == TripStatus.Settling),
            snapshot.FilteredTrips.Count(t => t.Status == TripStatus.Completed),
            snapshot.FinalizedExpenses.Count,
            totalVolumeVnd,
            confirmedVnd,
            aggregatedPendingVnd,
            markedPaidVnd,
            disputedVnd,
            aiReceiptsTotal,
            snapshot.TotalItineraryStops,
            snapshot.AiSuggestedItineraryStops,
            totalRawObligations,
            totalSimplifiedTransfers,
            totalEliminatedTransfers,
            efficiencyPercent);

        var settlementSummary = new AdminSettlementStatusSummaryDto(
            confirmedSettlements.Count,
            confirmedVnd,
            markedPaidSettlements.Count,
            markedPaidVnd,
            pendingSettlements.Count,
            pendingRowsVnd,
            disputedSettlements.Count,
            disputedVnd);

        var categoryBreakdown = Enum.GetValues<ExpenseCategory>()
            .Select(cat =>
            {
                var catExpenses = snapshot.FinalizedExpenses.Where(e => e.Category == cat).ToList();
                var catTotal = catExpenses.Sum(e => e.TotalAmount);
                var share = totalVolumeVnd > 0m
                    ? Math.Round((double)(catTotal * 100m / totalVolumeVnd), 1)
                    : 0.0;

                return new AdminCategoryBreakdownDto(
                    cat,
                    DescribeCategory(cat),
                    catExpenses.Count,
                    catTotal,
                    share);
            })
            .Where(c => c.ExpenseCount > 0)
            .OrderByDescending(c => c.TotalAmountVnd)
            .ThenBy(c => c.Category)
            .ToList();

        var recentActivities = BuildRecentActivities(
            snapshot.FilteredTrips,
            snapshot.FinalizedExpenses,
            snapshot.Settlements);

        return new AdminDashboardDto(
            startDate,
            endDate,
            DateTime.UtcNow,
            kpis,
            settlementSummary,
            categoryBreakdown,
            orderedReportRows,
            recentActivities);
    }

    public static string DescribeCategory(ExpenseCategory category) => category switch
    {
        ExpenseCategory.Food => "Ăn uống & Nhà hàng",
        ExpenseCategory.Transport => "Di chuyển & Vé xe",
        ExpenseCategory.Accommodation => "Lưu trú & Khách sạn",
        ExpenseCategory.Ticket => "Vé tham quan",
        ExpenseCategory.Shopping => "Mua sắm",
        ExpenseCategory.Entertainment => "Giải trí & Hoạt động",
        _ => "Chi phí khác"
    };

    private static bool IsAiScannedExpense(Expense expense)
        => expense.ScanStatus != ReceiptScanStatus.None
           || !string.IsNullOrWhiteSpace(expense.ReceiptImageUrl);

    private static Dictionary<Guid, decimal> ComputeGrossNetBalances(IReadOnlyCollection<Expense> expenses)
    {
        var map = new Dictionary<Guid, decimal>();
        foreach (var expense in expenses)
        {
            var splitSum = expense.Splits.Sum(s => s.OwedAmount);
            // Guard in case an incomplete test fixture creates an expense without splits yet
            if (splitSum != expense.TotalAmount)
            {
                continue;
            }

            map[expense.PaidByUserId] = map.GetValueOrDefault(expense.PaidByUserId, 0m) + expense.TotalAmount;
            foreach (var split in expense.Splits)
            {
                map[split.UserId] = map.GetValueOrDefault(split.UserId, 0m) - split.OwedAmount;
            }
        }

        return map;
    }

    private static IReadOnlyList<AdminActivityItemDto> BuildRecentActivities(
        IReadOnlyList<Trip> trips,
        IReadOnlyList<Expense> expenses,
        IReadOnlyList<Settlement> settlements)
    {
        var tripNameById = trips.ToDictionary(t => t.Id, t => t.Name);
        var items = new List<AdminActivityItemDto>();

        foreach (var trip in trips.Take(6))
        {
            items.Add(new AdminActivityItemDto(
                "Trip",
                "Chuyến đi",
                "text-bg-primary",
                $"Tạo chuyến đi \"{trip.Name}\" ({trip.Destination})",
                $"Trưởng đoàn: {trip.OwnerUser?.FullName ?? "Thành viên"} · Mã mời: {trip.InviteCode}",
                null,
                trip.CreatedAtUtc));
        }

        foreach (var expense in expenses.Take(6))
        {
            var tName = tripNameById.GetValueOrDefault(expense.TripId, "Chuyến đi");
            items.Add(new AdminActivityItemDto(
                "Expense",
                IsAiScannedExpense(expense) ? "AI Hóa đơn" : "Chi tiêu",
                IsAiScannedExpense(expense) ? "text-bg-info" : "text-bg-secondary",
                $"{expense.PaidByUser?.FullName ?? "Thành viên"} chi \"{expense.Title}\"",
                $"Chuyến: {tName} · {DescribeCategory(expense.Category)}",
                expense.TotalAmount,
                expense.SpentAtUtc));
        }

        foreach (var settlement in settlements.Take(6))
        {
            var tName = tripNameById.GetValueOrDefault(settlement.TripId, "Chuyến đi");
            var (badge, css, verb, ts) = settlement.Status switch
            {
                SettlementStatus.Confirmed => (
                    "Đã tất toán",
                    "text-bg-success",
                    "hoàn tất chuyển khoản cho",
                    settlement.ConfirmedAtUtc ?? settlement.CreatedAtUtc),
                SettlementStatus.MarkedPaid => (
                    "Chờ xác nhận",
                    "text-bg-warning",
                    "báo đã chuyển khoản cho",
                    settlement.MarkedPaidAtUtc ?? settlement.CreatedAtUtc),
                SettlementStatus.Disputed => (
                    "Khiếu nại",
                    "text-bg-danger",
                    "cần kiểm tra lại giao dịch với",
                    settlement.CreatedAtUtc),
                _ => (
                    "Lệnh VietQR",
                    "text-bg-light text-dark border",
                    "cần thanh toán cho",
                    settlement.CreatedAtUtc)
            };

            items.Add(new AdminActivityItemDto(
                "Settlement",
                badge,
                css,
                $"{settlement.FromUser?.FullName ?? "Thành viên"} {verb} {settlement.ToUser?.FullName ?? "Thành viên"}",
                $"Chuyến: {tName}",
                settlement.Amount,
                ts));
        }

        return items
            .OrderByDescending(i => i.OccurredAtUtc)
            .Take(12)
            .ToList();
    }
}
