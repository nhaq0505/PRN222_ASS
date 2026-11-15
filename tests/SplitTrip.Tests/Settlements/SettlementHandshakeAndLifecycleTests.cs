using SplitTrip.BusinessObjects.Enums;

namespace SplitTrip.Tests.Settlements;

public sealed class SettlementHandshakeAndLifecycleTests
{
    [Fact]
    public async Task TwoStepHandshake_MarkPaidAndConfirmReceived_EnforcesPermissionsAndAutoCompletesSettlingTrip()
    {
        await using var harness = new SettlementTestHarness();

        var alice = await harness.CreateUserAsync("alice@test.vn", "Alice Nguyễn", "VCB", "9988776655");
        var bob = await harness.CreateUserAsync("bob@test.vn", "Bob Trần", "MB", "1122334455");
        var charlie = await harness.CreateUserAsync("charlie@test.vn", "Charlie Lê", "TCB", "5566778899");

        var trip = await harness.CreateTripWithMembersAsync(
            alice.UserId,
            [bob.UserId, charlie.UserId],
            initialStatus: TripStatus.Ongoing);

        // Alice pays 900,000đ split equally (300k each) -> Bob owes 300k, Charlie owes 300k
        await harness.AddExpenseWithSplitsAsync(
            trip.Id,
            alice.UserId,
            "Khách sạn Đà Lạt",
            900_000m,
            new Dictionary<Guid, decimal>
            {
                [alice.UserId] = 300_000m,
                [bob.UserId] = 300_000m,
                [charlie.UserId] = 300_000m
            });

        await harness.TripService.TransitionStatusAsync(trip.Id, alice.UserId, TripStatus.Settling);
        var plan = await harness.SettlementService.SyncSettlementPlanAsync(trip.Id);
        Assert.Equal(2, plan.Count);

        var bobSettlement = Assert.Single(plan, s => s.FromUserId == bob.UserId);
        var charlieSettlement = Assert.Single(plan, s => s.FromUserId == charlie.UserId);

        // Creditor or third party cannot mark Bob's debt as paid
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            harness.SettlementService.MarkAsPaidAsync(bobSettlement.Id, alice.UserId, "Fake"));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            harness.SettlementService.MarkAsPaidAsync(bobSettlement.Id, charlie.UserId, "Fake"));

        // Creditor cannot confirm before debtor marks paid
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            harness.SettlementService.ConfirmReceivedAsync(bobSettlement.Id, alice.UserId));

        // Step 1: Bob marks paid -> Ledger NetBalance must NOT change yet!
        var markedBob = await harness.SettlementService.MarkAsPaidAsync(bobSettlement.Id, bob.UserId, "FT26320111");
        Assert.Equal(SettlementStatus.MarkedPaid, markedBob.Settlement.Status);
        Assert.False(markedBob.TripCompleted);

        var balancesWhileMarked = await harness.TripService.GetTripMemberBalancesAsync(trip.Id);
        Assert.Equal(-300_000m, balancesWhileMarked.Single(b => b.UserId == bob.UserId).NetBalance);

        // Re-syncing plan while Bob is MarkedPaid must NOT double-bill Bob!
        var resynced = await harness.SettlementService.SyncSettlementPlanAsync(trip.Id);
        Assert.Equal(2, resynced.Count);
        Assert.Single(resynced, s => s.FromUserId == bob.UserId && s.Status == SettlementStatus.MarkedPaid);

        // Non-creditor cannot confirm
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            harness.SettlementService.ConfirmReceivedAsync(bobSettlement.Id, bob.UserId));

        // Step 2a: Alice confirms Bob's payment -> Bob's NetBalance hits 0đ, trip still Settling (Charlie remains)
        var confirmedBob = await harness.SettlementService.ConfirmReceivedAsync(bobSettlement.Id, alice.UserId);
        Assert.Equal(SettlementStatus.Confirmed, confirmedBob.Settlement.Status);
        Assert.False(confirmedBob.TripCompleted);
        Assert.Equal(TripStatus.Settling, confirmedBob.NewTripStatus);

        var balancesAfterBob = await harness.TripService.GetTripMemberBalancesAsync(trip.Id);
        Assert.Equal(0m, balancesAfterBob.Single(b => b.UserId == bob.UserId).NetBalance);
        Assert.Equal(300_000m, balancesAfterBob.Single(b => b.UserId == alice.UserId).NetBalance);
        Assert.Equal(-300_000m, balancesAfterBob.Single(b => b.UserId == charlie.UserId).NetBalance);

        // Step 1 & 2b: Charlie marks paid -> Alice confirms -> Trip auto-transitions to Completed!
        await harness.SettlementService.MarkAsPaidAsync(charlieSettlement.Id, charlie.UserId, "CK Charlie");
        var confirmedCharlie = await harness.SettlementService.ConfirmReceivedAsync(charlieSettlement.Id, alice.UserId);

        Assert.True(confirmedCharlie.TripCompleted);
        Assert.Equal(TripStatus.Completed, confirmedCharlie.NewTripStatus);

        var finalDetails = await harness.TripService.GetTripDetailsAsync(trip.Id, alice.UserId);
        Assert.Equal(TripStatus.Completed, finalDetails.Status);
        Assert.NotNull(finalDetails.ClosedAtUtc);
        Assert.All(finalDetails.MemberBalances, m => Assert.Equal(0m, m.NetBalance));
    }

    [Fact]
    public async Task DisputeAndResubmit_PreservesDisputedCardWithoutDuplicateAndCompletesOnResubmission()
    {
        await using var harness = new SettlementTestHarness();

        var owner = await harness.CreateUserAsync("owner@test.vn", "Trưởng Đoàn", "VCB", "1234567890");
        var debtor = await harness.CreateUserAsync("debtor@test.vn", "Thành Viên Nợ", null, null);

        var trip = await harness.CreateTripWithMembersAsync(
            owner.UserId,
            [debtor.UserId],
            initialStatus: TripStatus.Ongoing);

        await harness.AddExpenseWithSplitsAsync(
            trip.Id,
            owner.UserId,
            "Vé máy bay",
            1_000_000m,
            new Dictionary<Guid, decimal>
            {
                [owner.UserId] = 500_000m,
                [debtor.UserId] = 500_000m
            });

        await harness.TripService.TransitionStatusAsync(trip.Id, owner.UserId, TripStatus.Settling);
        var plan = await harness.SettlementService.SyncSettlementPlanAsync(trip.Id);
        var card = Assert.Single(plan);

        await harness.SettlementService.MarkAsPaidAsync(card.Id, debtor.UserId, "Đã chuyển nhầm STK");

        // Empty dispute reason rejected
        await Assert.ThrowsAsync<ArgumentException>(() =>
            harness.SettlementService.DisputeAsync(card.Id, owner.UserId, "   "));

        var disputed = await harness.SettlementService.DisputeAsync(
            card.Id,
            owner.UserId,
            "Chưa nhận được 500k trong tài khoản VCB");

        Assert.Equal(SettlementStatus.Disputed, disputed.Settlement.Status);
        Assert.Equal("Chưa nhận được 500k trong tài khoản VCB", disputed.Settlement.DisputeReason);

        // Workspace load / re-sync must NOT create a duplicate Pending transfer while Disputed is open
        var workspace = await harness.SettlementService.GetWorkspaceAsync(trip.Id, debtor.UserId);
        var activeCard = Assert.Single(workspace.ActiveSettlements);
        Assert.Equal(card.Id, activeCard.Id);
        Assert.Equal(SettlementStatus.Disputed, activeCard.Status);

        // Debtor re-submits MarkAsPaid -> clears DisputeReason -> Creditor confirms -> Completed!
        var remarked = await harness.SettlementService.MarkAsPaidAsync(card.Id, debtor.UserId, "Đã chuyển lại mã FT999");
        Assert.Equal(SettlementStatus.MarkedPaid, remarked.Settlement.Status);
        Assert.Null(remarked.Settlement.DisputeReason);

        var confirmed = await harness.SettlementService.ConfirmReceivedAsync(card.Id, owner.UserId);
        Assert.True(confirmed.TripCompleted);
        Assert.Equal(TripStatus.Completed, confirmed.NewTripStatus);
    }

    [Fact]
    public async Task PostSettlementLateExpense_ReopeningSettlingTrip_PreservesConfirmedHistoryAndBillsOnlyDelta()
    {
        await using var harness = new SettlementTestHarness();

        var a = await harness.CreateUserAsync("a@test.vn", "An", "VCB", "1111222233");
        var b = await harness.CreateUserAsync("b@test.vn", "Bình", "TCB", "4444555566");

        var trip = await harness.CreateTripWithMembersAsync(
            a.UserId,
            [b.UserId],
            initialStatus: TripStatus.Ongoing);

        // Expense 1: An pays 400,000đ (200k each) -> Bình owes An 200,000đ
        await harness.AddExpenseWithSplitsAsync(
            trip.Id,
            a.UserId,
            "Ăn tối hải sản",
            400_000m,
            new Dictionary<Guid, decimal>
            {
                [a.UserId] = 200_000m,
                [b.UserId] = 200_000m
            });

        // Settle Expense 1 while Trip is Ongoing (before closing) or reopen after partial settlement
        var initialPlan = await harness.SettlementService.SyncSettlementPlanAsync(trip.Id);
        var firstTransfer = Assert.Single(initialPlan);
        Assert.Equal(200_000m, firstTransfer.Amount);

        await harness.SettlementService.MarkAsPaidAsync(firstTransfer.Id, b.UserId, "Đợt 1");
        await harness.SettlementService.ConfirmReceivedAsync(firstTransfer.Id, a.UserId);

        // Verify balances are 0đ after Expense 1 settlement
        var midBalances = await harness.TripService.GetTripMemberBalancesAsync(trip.Id);
        Assert.All(midBalances, m => Assert.Equal(0m, m.NetBalance));

        // Transition to Settling, then Owner realizes a late taxi bill was forgotten -> reopens Ongoing!
        await harness.TripService.TransitionStatusAsync(trip.Id, a.UserId, TripStatus.Settling);
        await harness.TripService.TransitionStatusAsync(trip.Id, a.UserId, TripStatus.Ongoing);

        // Late Expense 2: An pays 160,000đ taxi (80k each) -> Bình should owe ONLY the 80,000đ delta!
        await harness.AddExpenseWithSplitsAsync(
            trip.Id,
            a.UserId,
            "Taxi sân bay phát sinh muộn",
            160_000m,
            new Dictionary<Guid, decimal>
            {
                [a.UserId] = 80_000m,
                [b.UserId] = 80_000m
            });

        await harness.TripService.TransitionStatusAsync(trip.Id, a.UserId, TripStatus.Settling);
        var workspace = await harness.SettlementService.GetWorkspaceAsync(trip.Id, a.UserId);

        Assert.True(workspace.IsZeroSumBalanced);
        Assert.Equal(0m, workspace.LedgerNetSum);
        Assert.Equal(200_000m, workspace.TotalConfirmedAmount);
        Assert.Single(workspace.ConfirmedHistory);

        var deltaTransfer = Assert.Single(workspace.ActiveSettlements);
        Assert.Equal(b.UserId, deltaTransfer.FromUserId);
        Assert.Equal(a.UserId, deltaTransfer.ToUserId);
        Assert.Equal(80_000m, deltaTransfer.Amount);
        Assert.Equal(SettlementStatus.Pending, deltaTransfer.Status);

        // Verify Manual Nudge permissions (Creditor can nudge Debtor; Debtor cannot nudge self)
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            harness.SettlementService.NudgeDebtorAsync(deltaTransfer.Id, b.UserId));

        var nudge = await harness.SettlementService.NudgeDebtorAsync(deltaTransfer.Id, a.UserId);
        Assert.Equal(b.UserId, nudge.TargetUserId);
        Assert.Equal(NotificationType.DebtNudge, nudge.Notification.Type);
    }
}
