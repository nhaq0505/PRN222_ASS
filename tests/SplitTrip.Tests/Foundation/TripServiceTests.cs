using Microsoft.EntityFrameworkCore;
using SplitTrip.BusinessObjects.Entities;
using SplitTrip.BusinessObjects.Enums;
using SplitTrip.Services.Auth.Dtos;
using SplitTrip.Services.Trips.Dtos;

namespace SplitTrip.Tests.Foundation;

public sealed class TripServiceTests
{
    [Fact]
    public async Task CreateTrip_CreatesOwnerMembershipEightCharInviteCodeAndSequentialTripDays()
    {
        await using var harness = new FoundationTestHarness();
        var owner = await harness.AuthService.RegisterAsync(
            new RegisterUserRequest("owner@splittrip.vn", "Secret#123", "Minh Trưởng Đoàn"));

        var trip = await harness.TripService.CreateTripAsync(
            owner.UserId,
            new CreateTripRequest(
                Name: "Đà Lạt Săn Mây",
                Destination: "Đà Lạt, Lâm Đồng",
                Description: "Chuyến đi 3 ngày 2 đêm",
                StartDate: new DateOnly(2026, 12, 10),
                EndDate: new DateOnly(2026, 12, 12),
                EstimatedBudgetPerPerson: 2_500_000m));

        Assert.Equal(TripStatus.Planning, trip.Status);
        Assert.Equal(8, trip.InviteCode.Length);
        Assert.Equal(trip.InviteCode.ToUpperInvariant(), trip.InviteCode);
        Assert.Equal(3, trip.DayCount);
        Assert.Equal([1, 2, 3], trip.Days.Select(d => d.DayNumber).ToArray());
        Assert.Single(trip.MemberBalances);
        Assert.Equal(TripMemberRole.Owner, trip.MemberBalances[0].Role);
    }

    [Fact]
    public async Task JoinByInviteCode_PreventsDuplicateActiveJoin_AndReactivatesInactiveMember()
    {
        await using var harness = new FoundationTestHarness();
        var owner = await harness.AuthService.RegisterAsync(
            new RegisterUserRequest("owner@splittrip.vn", "Secret#123", "Owner"));
        var member = await harness.AuthService.RegisterAsync(
            new RegisterUserRequest("member@splittrip.vn", "Secret#123", "Member"));

        var trip = await harness.TripService.CreateTripAsync(
            owner.UserId,
            new CreateTripRequest(
                "Phú Quốc",
                "Phú Quốc",
                null,
                new DateOnly(2026, 12, 1),
                new DateOnly(2026, 12, 3),
                3_000_000m));

        var firstJoin = await harness.TripService.JoinByInviteCodeAsync(member.UserId, trip.InviteCode.ToLowerInvariant());
        Assert.False(firstJoin.WasReactivated);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            harness.TripService.JoinByInviteCodeAsync(member.UserId, trip.InviteCode));

        // Simulate inactive historical membership and verify reactivation without duplicate row
        var dbMember = await harness.DbContext.TripMembers
            .SingleAsync(m => m.TripId == trip.Id && m.UserId == member.UserId);
        dbMember.IsActive = false;
        await harness.DbContext.SaveChangesAsync();

        var reactivated = await harness.TripService.JoinByInviteCodeAsync(member.UserId, trip.InviteCode);
        Assert.True(reactivated.WasReactivated);
        Assert.Equal(1, await harness.DbContext.TripMembers.CountAsync(m => m.TripId == trip.Id && m.UserId == member.UserId));
    }

    [Fact]
    public async Task TransitionStatus_EnforcesLifecycleRulesReopenAndZeroBalanceCompletionGate()
    {
        await using var harness = new FoundationTestHarness();
        var owner = await harness.AuthService.RegisterAsync(
            new RegisterUserRequest("owner@splittrip.vn", "Secret#123", "Owner"));
        var member = await harness.AuthService.RegisterAsync(
            new RegisterUserRequest("member@splittrip.vn", "Secret#123", "Member"));

        var trip = await harness.TripService.CreateTripAsync(
            owner.UserId,
            new CreateTripRequest(
                "Hội An",
                "Quảng Nam",
                null,
                new DateOnly(2026, 12, 5),
                new DateOnly(2026, 12, 6),
                1_500_000m));

        await harness.TripService.JoinByInviteCodeAsync(member.UserId, trip.InviteCode);

        // Non-owner cannot transition status
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            harness.TripService.TransitionStatusAsync(trip.Id, member.UserId, TripStatus.Ongoing));

        // Cannot skip Planning -> Completed
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            harness.TripService.TransitionStatusAsync(trip.Id, owner.UserId, TripStatus.Completed));

        // Planning -> Ongoing -> Settling -> Ongoing (reopen) -> Settling
        Assert.Equal(TripStatus.Ongoing, await harness.TripService.TransitionStatusAsync(trip.Id, owner.UserId, TripStatus.Ongoing));
        Assert.Equal(TripStatus.Settling, await harness.TripService.TransitionStatusAsync(trip.Id, owner.UserId, TripStatus.Settling));
        Assert.Equal(TripStatus.Ongoing, await harness.TripService.TransitionStatusAsync(trip.Id, owner.UserId, TripStatus.Ongoing));

        // Add unsettled expense, move to Settling, verify Completed is blocked until settled
        var expense = new Expense
        {
            Id = Guid.NewGuid(),
            TripId = trip.Id,
            PaidByUserId = owner.UserId,
            Title = "Vé máy bay",
            Category = ExpenseCategory.Transport,
            SubtotalAmount = 2_000_000m,
            TotalAmount = 2_000_000m,
            SplitMode = ExpenseSplitMode.Equal,
            ScanStatus = ReceiptScanStatus.None
        };
        harness.DbContext.Expenses.Add(expense);
        harness.DbContext.ExpenseSplits.AddRange(
            new ExpenseSplit { ExpenseId = expense.Id, UserId = owner.UserId, ShareRatioOrWeight = 1m, OwedAmount = 1_000_000m },
            new ExpenseSplit { ExpenseId = expense.Id, UserId = member.UserId, ShareRatioOrWeight = 1m, OwedAmount = 1_000_000m });
        await harness.DbContext.SaveChangesAsync();

        await harness.TripService.TransitionStatusAsync(trip.Id, owner.UserId, TripStatus.Settling);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            harness.TripService.TransitionStatusAsync(trip.Id, owner.UserId, TripStatus.Completed));

        // Confirm settlement -> NetBalance == 0 -> Completed succeeds
        harness.DbContext.Settlements.Add(new Settlement
        {
            TripId = trip.Id,
            FromUserId = member.UserId,
            ToUserId = owner.UserId,
            Amount = 1_000_000m,
            Status = SettlementStatus.Confirmed,
            TransferNote = $"SPLITTRIP {trip.InviteCode} MEMBER",
            ConfirmedAtUtc = DateTime.UtcNow
        });
        await harness.DbContext.SaveChangesAsync();

        var completed = await harness.TripService.TransitionStatusAsync(trip.Id, owner.UserId, TripStatus.Completed);
        Assert.Equal(TripStatus.Completed, completed);

        var updatedTrip = await harness.DbContext.Trips.SingleAsync(t => t.Id == trip.Id);
        Assert.NotNull(updatedTrip.ClosedAtUtc);
    }

    [Fact]
    public async Task RemoveOrLeaveMember_IgnoresDraftReviewExpenses_BlocksNonZeroNetBalance_AndSoftDeactivatesHistory()
    {
        await using var harness = new FoundationTestHarness();
        var owner = await harness.AuthService.RegisterAsync(
            new RegisterUserRequest("owner@splittrip.vn", "Secret#123", "Owner"));
        var cleanMember = await harness.AuthService.RegisterAsync(
            new RegisterUserRequest("clean@splittrip.vn", "Secret#123", "Clean Member"));
        var debtorMember = await harness.AuthService.RegisterAsync(
            new RegisterUserRequest("debtor@splittrip.vn", "Secret#123", "Debtor Member"));

        var trip = await harness.TripService.CreateTripAsync(
            owner.UserId,
            new CreateTripRequest(
                "Nha Trang",
                "Khánh Hòa",
                null,
                new DateOnly(2026, 12, 15),
                new DateOnly(2026, 12, 17),
                2_000_000m));

        await harness.TripService.JoinByInviteCodeAsync(cleanMember.UserId, trip.InviteCode);
        await harness.TripService.JoinByInviteCodeAsync(debtorMember.UserId, trip.InviteCode);

        // 1. Clean member with zero history is hard-deleted cleanly
        await harness.TripService.RemoveOrLeaveMemberAsync(trip.Id, cleanMember.UserId, cleanMember.UserId);
        Assert.False(await harness.DbContext.TripMembers.AnyAsync(m => m.TripId == trip.Id && m.UserId == cleanMember.UserId));

        // 2. Unfinalized DraftReview OCR expense must NOT affect NetBalance
        var draftExpense = new Expense
        {
            Id = Guid.NewGuid(),
            TripId = trip.Id,
            PaidByUserId = owner.UserId,
            Title = "Hóa đơn nháp OCR",
            Category = ExpenseCategory.Food,
            SubtotalAmount = 500_000m,
            TotalAmount = 500_000m,
            SplitMode = ExpenseSplitMode.Itemized,
            ScanStatus = ReceiptScanStatus.DraftReview
        };
        harness.DbContext.Expenses.Add(draftExpense);
        harness.DbContext.ExpenseSplits.Add(new ExpenseSplit
        {
            ExpenseId = draftExpense.Id,
            UserId = debtorMember.UserId,
            ShareRatioOrWeight = 1m,
            OwedAmount = 500_000m
        });
        await harness.DbContext.SaveChangesAsync();

        var balancesWithDraftOnly = await harness.TripService.GetTripMemberBalancesAsync(trip.Id);
        Assert.All(balancesWithDraftOnly, b => Assert.Equal(0m, b.NetBalance));

        // 3. Finalized expense creates +300k / -300k NetBalance and blocks leave/remove
        var finalizedExpense = new Expense
        {
            Id = Guid.NewGuid(),
            TripId = trip.Id,
            PaidByUserId = owner.UserId,
            Title = "Hải sản tối",
            Category = ExpenseCategory.Food,
            SubtotalAmount = 600_000m,
            TotalAmount = 600_000m,
            SplitMode = ExpenseSplitMode.Equal,
            ScanStatus = ReceiptScanStatus.None
        };
        harness.DbContext.Expenses.Add(finalizedExpense);
        harness.DbContext.ExpenseSplits.AddRange(
            new ExpenseSplit { ExpenseId = finalizedExpense.Id, UserId = owner.UserId, ShareRatioOrWeight = 1m, OwedAmount = 300_000m },
            new ExpenseSplit { ExpenseId = finalizedExpense.Id, UserId = debtorMember.UserId, ShareRatioOrWeight = 1m, OwedAmount = 300_000m });
        await harness.DbContext.SaveChangesAsync();

        var activeBalances = await harness.TripService.GetTripMemberBalancesAsync(trip.Id);
        Assert.Equal(0m, activeBalances.Sum(b => b.NetBalance));
        Assert.Equal(300_000m, activeBalances.Single(b => b.UserId == owner.UserId).NetBalance);
        Assert.Equal(-300_000m, activeBalances.Single(b => b.UserId == debtorMember.UserId).NetBalance);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            harness.TripService.RemoveOrLeaveMemberAsync(trip.Id, debtorMember.UserId, debtorMember.UserId));
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            harness.TripService.RemoveOrLeaveMemberAsync(trip.Id, owner.UserId, debtorMember.UserId));

        // 4. MarkedPaid (unconfirmed) does NOT clear NetBalance yet
        var settlement = new Settlement
        {
            Id = Guid.NewGuid(),
            TripId = trip.Id,
            FromUserId = debtorMember.UserId,
            ToUserId = owner.UserId,
            Amount = 300_000m,
            Status = SettlementStatus.MarkedPaid,
            TransferNote = $"SPLITTRIP {trip.InviteCode} DEBTOR"
        };
        harness.DbContext.Settlements.Add(settlement);
        await harness.DbContext.SaveChangesAsync();

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            harness.TripService.RemoveOrLeaveMemberAsync(trip.Id, debtorMember.UserId, debtorMember.UserId));

        // 5. Confirmed settlement brings NetBalance == 0; leave soft-deactivates (IsActive = false) instead of hard-deleting
        settlement.Status = SettlementStatus.Confirmed;
        settlement.ConfirmedAtUtc = DateTime.UtcNow;
        await harness.DbContext.SaveChangesAsync();

        await harness.TripService.RemoveOrLeaveMemberAsync(trip.Id, debtorMember.UserId, debtorMember.UserId);

        var retainedRow = await harness.DbContext.TripMembers
            .SingleAsync(m => m.TripId == trip.Id && m.UserId == debtorMember.UserId);
        Assert.False(retainedRow.IsActive);
    }
}
