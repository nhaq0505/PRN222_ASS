using SplitTrip.BusinessObjects.Enums;
using SplitTrip.Services.Expenses.Dtos;

namespace SplitTrip.Tests.Expenses;

public sealed class ExpenseServiceTests
{
    [Fact]
    public async Task CreateUpdateDeleteExpense_MaintainsZeroSumTripLedger()
    {
        await using var harness = new ExpensesTestHarness();
        var (tripId, alice, bob, charlie) = await harness.SeedTripWithThreeMembersAsync();

        var created = await harness.ExpenseService.CreateExpenseAsync(
            alice,
            new UpsertExpenseRequest(
                TripId: tripId,
                PaidByUserId: alice,
                Title: "Taxi sân bay Liên Khương",
                Category: ExpenseCategory.Transport,
                TotalAmount: 250_000m,
                SplitMode: ExpenseSplitMode.Equal,
                Participants:
                [
                    new ParticipantShareInput(alice),
                    new ParticipantShareInput(bob),
                    new ParticipantShareInput(charlie)
                ]));

        Assert.Equal(250_000m, created.Splits.Sum(s => s.OwedAmount));
        Assert.Equal(83_334m, created.Splits.Single(s => s.UserId == alice).OwedAmount);

        var balancesAfterCreate = await harness.TripService.GetTripMemberBalancesAsync(tripId);
        Assert.Equal(0m, balancesAfterCreate.Sum(b => b.NetBalance));
        Assert.Equal(166_666m, balancesAfterCreate.Single(b => b.UserId == alice).NetBalance);

        // Update to Shares mode (1 : 1 : 2)
        var updated = await harness.ExpenseService.UpdateExpenseAsync(
            created.Id,
            alice,
            new UpsertExpenseRequest(
                TripId: tripId,
                PaidByUserId: bob,
                Title: "Taxi sân bay + gửi hành lý",
                Category: ExpenseCategory.Transport,
                TotalAmount: 300_000m,
                SplitMode: ExpenseSplitMode.Shares,
                Participants:
                [
                    new ParticipantShareInput(alice, 1m),
                    new ParticipantShareInput(bob, 1m),
                    new ParticipantShareInput(charlie, 2m)
                ]));

        Assert.Equal(300_000m, updated.Splits.Sum(s => s.OwedAmount));
        Assert.Equal(150_000m, updated.Splits.Single(s => s.UserId == charlie).OwedAmount);

        var balancesAfterUpdate = await harness.TripService.GetTripMemberBalancesAsync(tripId);
        Assert.Equal(0m, balancesAfterUpdate.Sum(b => b.NetBalance));

        // Delete restores all balances to 0
        await harness.ExpenseService.DeleteExpenseAsync(created.Id, alice);
        var balancesAfterDelete = await harness.TripService.GetTripMemberBalancesAsync(tripId);
        Assert.All(balancesAfterDelete, b => Assert.Equal(0m, b.NetBalance));
    }

    [Fact]
    public async Task TripStatusGuard_BlocksMutationsInSettlingAndCompleted_AllowsWhenReopenedToOngoing()
    {
        await using var harness = new ExpensesTestHarness();
        var (tripId, alice, bob, _) = await harness.SeedTripWithThreeMembersAsync();

        // Planning -> Ongoing
        await harness.TripService.TransitionStatusAsync(tripId, alice, TripStatus.Ongoing);

        var expense = await harness.ExpenseService.CreateExpenseAsync(
            alice,
            new UpsertExpenseRequest(
                TripId: tripId,
                PaidByUserId: alice,
                Title: "Vé tham quan vườn hoa",
                Category: ExpenseCategory.Ticket,
                TotalAmount: 200_000m,
                SplitMode: ExpenseSplitMode.ExactAmount,
                Participants:
                [
                    new ParticipantShareInput(alice, 100_000m),
                    new ParticipantShareInput(bob, 100_000m)
                ]));

        // Ongoing -> Settling
        await harness.TripService.TransitionStatusAsync(tripId, alice, TripStatus.Settling);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            harness.ExpenseService.CreateExpenseAsync(
                alice,
                new UpsertExpenseRequest(
                    TripId: tripId,
                    PaidByUserId: alice,
                    Title: "Khoản phát sinh muộn",
                    Category: ExpenseCategory.Food,
                    TotalAmount: 100_000m,
                    SplitMode: ExpenseSplitMode.Equal,
                    Participants: [new ParticipantShareInput(alice), new ParticipantShareInput(bob)])));

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            harness.ExpenseService.DeleteExpenseAsync(expense.Id, alice));

        // Owner reopens Settling -> Ongoing
        await harness.TripService.TransitionStatusAsync(tripId, alice, TripStatus.Ongoing);

        await harness.ExpenseService.DeleteExpenseAsync(expense.Id, alice);

        // Settling -> Completed (since all balances are now 0)
        await harness.TripService.TransitionStatusAsync(tripId, alice, TripStatus.Settling);
        await harness.TripService.TransitionStatusAsync(tripId, alice, TripStatus.Completed);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            harness.ExpenseService.CreateExpenseAsync(
                alice,
                new UpsertExpenseRequest(
                    TripId: tripId,
                    PaidByUserId: alice,
                    Title: "Bị chặn khi đã hoàn tất",
                    Category: ExpenseCategory.Other,
                    TotalAmount: 50_000m,
                    SplitMode: ExpenseSplitMode.Equal,
                    Participants: [new ParticipantShareInput(alice)])));
    }
}
