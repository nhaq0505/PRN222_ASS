using Microsoft.EntityFrameworkCore;
using SplitTrip.BusinessObjects.Enums;

namespace SplitTrip.Tests.Settlements;

public sealed class AutomationAndAdminAnalyticsTests
{
    [Fact]
    public async Task RunAutomationCycle_AutoTransitionsPastOngoingTripsWithoutOpenReceipts_AndSends24hIdempotentDebtNudge()
    {
        await using var harness = new SettlementTestHarness();

        var owner = await harness.CreateUserAsync("owner-auto@test.vn", "Minh Trưởng Đoàn", "VCB", "9090909090");
        var debtor = await harness.CreateUserAsync("debtor-auto@test.vn", "Khoa Thành Viên", "MB", "8080808080");

        // Trip 1: Ended yesterday, finalized expense only -> MUST auto-transition Ongoing -> Settling
        var pastReadyTrip = await harness.CreateTripWithMembersAsync(
            owner.UserId,
            [debtor.UserId],
            name: "Phú Quốc Đã Kết Thúc",
            startDate: new DateOnly(2026, 11, 10),
            endDate: new DateOnly(2026, 11, 12),
            initialStatus: TripStatus.Ongoing);

        await harness.AddExpenseWithSplitsAsync(
            pastReadyTrip.Id,
            owner.UserId,
            "Thuê cano",
            600_000m,
            new Dictionary<Guid, decimal>
            {
                [owner.UserId] = 300_000m,
                [debtor.UserId] = 300_000m
            },
            scanStatus: ReceiptScanStatus.Locked);

        // Trip 2: Ended yesterday, but STILL has a ClaimingOpen receipt -> MUST stay Ongoing!
        var pastUnfinishedReceiptTrip = await harness.CreateTripWithMembersAsync(
            owner.UserId,
            [debtor.UserId],
            name: "Nha Trang Còn Hóa Đơn Mở",
            startDate: new DateOnly(2026, 11, 10),
            endDate: new DateOnly(2026, 11, 12),
            initialStatus: TripStatus.Ongoing);

        await harness.AddExpenseWithSplitsAsync(
            pastUnfinishedReceiptTrip.Id,
            owner.UserId,
            "Buffet đang tick món",
            500_000m,
            new Dictionary<Guid, decimal>
            {
                [owner.UserId] = 250_000m,
                [debtor.UserId] = 250_000m
            },
            scanStatus: ReceiptScanStatus.ClaimingOpen);

        // Cycle 1 at 2026-11-14 08:00 UTC (15:00 Vietnam time on 2026-11-14)
        var t0 = new DateTime(2026, 11, 14, 8, 0, 0, DateTimeKind.Utc);
        var cycle1 = await harness.SettlementService.RunAutomationCycleAsync(t0);

        var settledEvent = Assert.Single(cycle1.AutoSettledTrips);
        Assert.Equal(pastReadyTrip.Id, settledEvent.TripId);
        Assert.Equal(1, settledEvent.GeneratedSettlementCount);
        Assert.Empty(cycle1.OverdueNudges);

        var updatedTrip1 = await harness.TripService.GetTripDetailsAsync(pastReadyTrip.Id, owner.UserId);
        var updatedTrip2 = await harness.TripService.GetTripDetailsAsync(pastUnfinishedReceiptTrip.Id, owner.UserId);
        Assert.Equal(TripStatus.Settling, updatedTrip1.Status);
        Assert.Equal(TripStatus.Ongoing, updatedTrip2.Status);

        // Backdate CreatedAtUtc of the generated Pending settlement by 25 hours to simulate overdue debt
        var createdSettlement = Assert.Single(await harness.DbContext.Settlements.ToListAsync());
        createdSettlement.CreatedAtUtc = t0.AddHours(-25);
        await harness.DbContext.SaveChangesAsync();

        // Cycle 2 at t0 + 1h -> Should emit 1 overdue DebtNudge!
        var t1 = t0.AddHours(1);
        var cycle2 = await harness.SettlementService.RunAutomationCycleAsync(t1);
        var nudge = Assert.Single(cycle2.OverdueNudges);
        Assert.Equal(debtor.UserId, nudge.TargetUserId);
        Assert.Equal(NotificationType.DebtNudge, nudge.Notification.Type);

        // Cycle 3 only 2 hours later -> Cooldown guard prevents duplicate DebtNudge!
        var cycle3 = await harness.SettlementService.RunAutomationCycleAsync(t1.AddHours(2));
        Assert.Empty(cycle3.OverdueNudges);

        // Cycle 4 after 24h+ cooldown -> Sends follow-up DebtNudge!
        var cycle4 = await harness.SettlementService.RunAutomationCycleAsync(t1.AddHours(25));
        Assert.Single(cycle4.OverdueNudges);
    }

    [Fact]
    public async Task GetDashboardAsync_ExcludesDraftReviewExpenses_AndOrdersDateFilteredTripsDescendingBySpend()
    {
        await using var harness = new SettlementTestHarness();

        var u1 = await harness.CreateUserAsync("u1-admin@test.vn", "User Một");
        var u2 = await harness.CreateUserAsync("u2-admin@test.vn", "User Hai");

        // Trip in October (outside November filter)
        var octTrip = await harness.CreateTripWithMembersAsync(
            u1.UserId,
            [u2.UserId],
            name: "Hà Giang Tháng 10",
            startDate: new DateOnly(2026, 10, 5),
            endDate: new DateOnly(2026, 10, 8));

        await harness.AddExpenseWithSplitsAsync(
            octTrip.Id,
            u1.UserId,
            "Thuê xe máy",
            800_000m,
            new Dictionary<Guid, decimal> { [u1.UserId] = 400_000m, [u2.UserId] = 400_000m },
            ExpenseCategory.Transport);

        // November Trip Low Spend (1,200,000đ finalized + 9,999,000đ DraftReview that MUST be ignored!)
        var novLowTrip = await harness.CreateTripWithMembersAsync(
            u1.UserId,
            [u2.UserId],
            name: "Vũng Tàu Cuối Tuần",
            startDate: new DateOnly(2026, 11, 5),
            endDate: new DateOnly(2026, 11, 6));

        await harness.AddExpenseWithSplitsAsync(
            novLowTrip.Id,
            u1.UserId,
            "Hải sản Vũng Tàu",
            1_200_000m,
            new Dictionary<Guid, decimal> { [u1.UserId] = 600_000m, [u2.UserId] = 600_000m },
            ExpenseCategory.Food,
            ReceiptScanStatus.Locked);

        await harness.AddExpenseWithSplitsAsync(
            novLowTrip.Id,
            u1.UserId,
            "Hóa đơn OCR chưa duyệt nháp",
            9_999_000m,
            new Dictionary<Guid, decimal> { [u1.UserId] = 4_999_500m, [u2.UserId] = 4_999_500m },
            ExpenseCategory.Shopping,
            ReceiptScanStatus.DraftReview);

        // November Trip High Spend (4,500,000đ finalized)
        var novHighTrip = await harness.CreateTripWithMembersAsync(
            u1.UserId,
            [u2.UserId],
            name: "Phú Quốc Nghỉ Dưỡng",
            startDate: new DateOnly(2026, 11, 15),
            endDate: new DateOnly(2026, 11, 18));

        await harness.AddExpenseWithSplitsAsync(
            novHighTrip.Id,
            u1.UserId,
            "Resort 5 sao",
            4_500_000m,
            new Dictionary<Guid, decimal> { [u1.UserId] = 2_250_000m, [u2.UserId] = 2_250_000m },
            ExpenseCategory.Accommodation);

        var novReport = await harness.AdminAnalyticsService.GetDashboardAsync(
            new DateOnly(2026, 11, 1),
            new DateOnly(2026, 11, 30));

        Assert.Equal(2, novReport.Kpis.TotalRegisteredUsers);
        Assert.Equal(2, novReport.Kpis.TotalTrips);
        Assert.Equal(5_700_000m, novReport.Kpis.TotalFinalizedVolumeVnd);
        Assert.Equal(1, novReport.Kpis.AiScannedReceiptsCount);

        // Report table must be ordered descending by TotalSpendVnd (4.5M first, then 1.2M)
        Assert.Equal(2, novReport.TripReportRows.Count);
        Assert.Equal(novHighTrip.Id, novReport.TripReportRows[0].TripId);
        Assert.Equal(4_500_000m, novReport.TripReportRows[0].TotalSpendVnd);
        Assert.Equal(novLowTrip.Id, novReport.TripReportRows[1].TripId);
        Assert.Equal(1_200_000m, novReport.TripReportRows[1].TotalSpendVnd);
    }
}
