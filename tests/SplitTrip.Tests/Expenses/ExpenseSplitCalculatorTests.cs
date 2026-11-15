using SplitTrip.BusinessObjects.Enums;
using SplitTrip.Services.Expenses;
using SplitTrip.Services.Expenses.Dtos;

namespace SplitTrip.Tests.Expenses;

public sealed class ExpenseSplitCalculatorTests
{
    [Fact]
    public void EqualSplit_100kAcrossThreeMembers_Assigns1VndRemainderToParticipatingPayer()
    {
        var alice = Guid.Parse("00000000-0000-0000-0000-000000000001");
        var bob = Guid.Parse("00000000-0000-0000-0000-000000000002");
        var charlie = Guid.Parse("00000000-0000-0000-0000-000000000003");

        // Bob is the payer even though Alice is listed first
        var splits = ExpenseSplitCalculator.CalculateSplits(
            ExpenseSplitMode.Equal,
            totalAmount: 100_000m,
            paidByUserId: bob,
            participants:
            [
                new ParticipantShareInput(alice, 1m),
                new ParticipantShareInput(bob, 1m),
                new ParticipantShareInput(charlie, 1m)
            ]);

        Assert.Equal(100_000m, splits.Sum(s => s.OwedAmount));
        Assert.Equal(33_333m, splits.Single(s => s.UserId == alice).OwedAmount);
        Assert.Equal(33_334m, splits.Single(s => s.UserId == bob).OwedAmount);
        Assert.Equal(33_333m, splits.Single(s => s.UserId == charlie).OwedAmount);
    }

    [Fact]
    public void EqualSplit_WhenPayerDoesNotParticipate_AssignsRemainderToFirstOrderedParticipant()
    {
        var payer = Guid.Parse("00000000-0000-0000-0000-000000000099");
        var first = Guid.Parse("00000000-0000-0000-0000-000000000005");
        var second = Guid.Parse("00000000-0000-0000-0000-000000000001");
        var third = Guid.Parse("00000000-0000-0000-0000-000000000002");

        var splits = ExpenseSplitCalculator.CalculateSplits(
            ExpenseSplitMode.Equal,
            totalAmount: 100_000m,
            paidByUserId: payer,
            participants:
            [
                new ParticipantShareInput(first, 1m),
                new ParticipantShareInput(second, 1m),
                new ParticipantShareInput(third, 1m)
            ]);

        Assert.Equal(100_000m, splits.Sum(s => s.OwedAmount));
        Assert.Equal(33_334m, splits.Single(s => s.UserId == first).OwedAmount);
        Assert.Equal(33_333m, splits.Single(s => s.UserId == second).OwedAmount);
        Assert.Equal(33_333m, splits.Single(s => s.UserId == third).OwedAmount);
    }

    [Fact]
    public void ExactAmount_MismatchedSum_ThrowsArgumentExceptionWithDelta()
    {
        var u1 = Guid.NewGuid();
        var u2 = Guid.NewGuid();

        var ex = Assert.Throws<ArgumentException>(() =>
            ExpenseSplitCalculator.CalculateSplits(
                ExpenseSplitMode.ExactAmount,
                totalAmount: 250_000m,
                paidByUserId: u1,
                participants:
                [
                    new ParticipantShareInput(u1, 100_000m),
                    new ParticipantShareInput(u2, 120_000m)
                ]));

        Assert.Contains("30", ex.Message);
    }

    [Fact]
    public void PercentageSplit_MustSumTo100AndReconcileWholeVnd()
    {
        var u1 = Guid.Parse("00000000-0000-0000-0000-000000000001");
        var u2 = Guid.Parse("00000000-0000-0000-0000-000000000002");
        var u3 = Guid.Parse("00000000-0000-0000-0000-000000000003");

        var splits = ExpenseSplitCalculator.CalculateSplits(
            ExpenseSplitMode.Percentage,
            totalAmount: 100_000m,
            paidByUserId: u1,
            participants:
            [
                new ParticipantShareInput(u1, 33.3333m),
                new ParticipantShareInput(u2, 33.3333m),
                new ParticipantShareInput(u3, 33.3334m)
            ]);

        Assert.Equal(100_000m, splits.Sum(s => s.OwedAmount));
        Assert.All(splits, s => Assert.Equal(decimal.Truncate(s.OwedAmount), s.OwedAmount));
        // u3 has largest fractional remainder (.34 vs .30) so u3 gets 33,334
        Assert.Equal(33_334m, splits.Single(s => s.UserId == u3).OwedAmount);

        Assert.Throws<ArgumentException>(() =>
            ExpenseSplitCalculator.CalculateSplits(
                ExpenseSplitMode.Percentage,
                totalAmount: 100_000m,
                paidByUserId: u1,
                participants:
                [
                    new ParticipantShareInput(u1, 50m),
                    new ParticipantShareInput(u2, 49.5m)
                ]));
    }

    [Fact]
    public void SharesSplit_UnequalWeights_ReconcilesExactTotal()
    {
        var u1 = Guid.NewGuid();
        var u2 = Guid.NewGuid();
        var u3 = Guid.NewGuid();

        // 1 : 2 : 4 on 1,000,000 VND (7 shares -> 142,857.14 | 285,714.28 | 571,428.57 -> sum floored = 999,998 -> +1 to u3 and u2!)
        var splits = ExpenseSplitCalculator.CalculateSplits(
            ExpenseSplitMode.Shares,
            totalAmount: 1_000_000m,
            paidByUserId: u1,
            participants:
            [
                new ParticipantShareInput(u1, 1m),
                new ParticipantShareInput(u2, 2m),
                new ParticipantShareInput(u3, 4m)
            ]);

        Assert.Equal(1_000_000m, splits.Sum(s => s.OwedAmount));
        Assert.Equal(142_857m, splits.Single(s => s.UserId == u1).OwedAmount);
        Assert.Equal(285_714m, splits.Single(s => s.UserId == u2).OwedAmount);
        Assert.Equal(571_429m, splits.Single(s => s.UserId == u3).OwedAmount);
    }

    [Theory]
    [InlineData(1, 3)]
    [InlineData(2, 3)]
    [InlineData(7, 5)]
    [InlineData(99_991, 6)]
    [InlineData(123_457, 7)]
    [InlineData(999_999, 4)]
    public void PropertySweep_UnfriendlyPrimeTotals_AlwaysReconcilesExactIntegerVnd(int totalVnd, int memberCount)
    {
        var users = Enumerable.Range(1, memberCount)
            .Select(i => Guid.Parse($"00000000-0000-0000-0000-{i:D12}"))
            .ToList();

        var participants = users
            .Select((u, idx) => new ParticipantShareInput(u, idx + 1))
            .ToList();

        var splits = ExpenseSplitCalculator.CalculateSplits(
            ExpenseSplitMode.Shares,
            totalAmount: totalVnd,
            paidByUserId: users[0],
            participants);

        Assert.Equal(totalVnd, splits.Sum(s => s.OwedAmount));
        Assert.All(splits, s =>
        {
            Assert.True(s.OwedAmount >= 0m);
            Assert.Equal(decimal.Truncate(s.OwedAmount), s.OwedAmount);
        });
    }
}
