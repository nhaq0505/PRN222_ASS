using SplitTrip.Services.Settlements;
using SplitTrip.Services.Settlements.Dtos;

namespace SplitTrip.Tests.Settlements;

public sealed class MinCashFlowServiceTests
{
    private readonly MinCashFlowService _sut = new();

    [Fact]
    public void SimplifyPairwiseDebts_EightPersonPureCircularRing_EliminatesAllTransfersToZero()
    {
        var users = Enumerable.Range(1, 8)
            .Select(i => (Id: Guid.Parse($"00000000-0000-0000-0000-00000000000{i}"), Name: $"Thành viên {i}"))
            .ToArray();

        // U1 -> U2 -> U3 -> ... -> U8 -> U1 (each owes 250,000đ in a full circle)
        var circularEdges = new List<PairwiseDebtInput>();
        for (var i = 0; i < users.Length; i++)
        {
            var next = (i + 1) % users.Length;
            circularEdges.Add(new PairwiseDebtInput(
                users[i].Id,
                users[i].Name,
                users[next].Id,
                users[next].Name,
                250_000m));
        }

        var result = _sut.SimplifyPairwiseDebts(circularEdges);

        Assert.Equal(8, result.NaivePairwiseCount);
        Assert.Equal(0, result.SimplifiedTransferCount);
        Assert.Empty(result.Transfers);
        Assert.Equal(8, result.EliminatedTransfersCount);
        Assert.Equal(100.0, result.ReductionPercentage);
        Assert.Equal(0m, result.TotalTransferVolume);
    }

    [Fact]
    public void SimplifyPairwiseDebts_EightPersonTangledCrossDebts_ReducesToAtMostNMinusOneAndConservesCash()
    {
        var u = Enumerable.Range(1, 8)
            .Select(i => (Id: Guid.Parse($"10000000-0000-0000-0000-00000000000{i}"), Name: $"Member {i}"))
            .ToArray();

        // 18 cross-debt obligations across 8 travelers
        PairwiseDebtInput[] tangledDebts =
        [
            new(u[0].Id, u[0].Name, u[1].Id, u[1].Name, 320_000m),
            new(u[1].Id, u[1].Name, u[2].Id, u[2].Name, 280_000m),
            new(u[2].Id, u[2].Name, u[3].Id, u[3].Name, 450_000m),
            new(u[3].Id, u[3].Name, u[4].Id, u[4].Name, 190_000m),
            new(u[4].Id, u[4].Name, u[5].Id, u[5].Name, 510_000m),
            new(u[5].Id, u[5].Name, u[6].Id, u[6].Name, 230_000m),
            new(u[6].Id, u[6].Name, u[7].Id, u[7].Name, 340_000m),
            new(u[7].Id, u[7].Name, u[0].Id, u[0].Name, 150_000m),
            new(u[0].Id, u[0].Name, u[3].Id, u[3].Name, 210_000m),
            new(u[1].Id, u[1].Name, u[5].Id, u[5].Name, 175_000m),
            new(u[2].Id, u[2].Name, u[6].Id, u[6].Name, 130_000m),
            new(u[4].Id, u[4].Name, u[7].Id, u[7].Name, 290_000m),
            new(u[6].Id, u[6].Name, u[1].Id, u[1].Name, 110_000m),
            new(u[5].Id, u[5].Name, u[0].Id, u[0].Name, 95_000m),
            new(u[3].Id, u[3].Name, u[7].Id, u[7].Name, 160_000m),
            new(u[2].Id, u[2].Name, u[4].Id, u[4].Name, 85_000m),
            new(u[7].Id, u[7].Name, u[2].Id, u[2].Name, 240_000m),
            new(u[1].Id, u[1].Name, u[6].Id, u[6].Name, 145_000m)
        ];

        // Compute ground-truth net balance per user
        var expectedNet = u.ToDictionary(x => x.Id, _ => 0m);
        foreach (var edge in tangledDebts)
        {
            expectedNet[edge.FromUserId] -= edge.Amount;
            expectedNet[edge.ToUserId] += edge.Amount;
        }

        Assert.Equal(0m, expectedNet.Values.Sum());

        var result = _sut.SimplifyPairwiseDebts(tangledDebts);

        Assert.Equal(18, result.NaivePairwiseCount);
        Assert.True(result.SimplifiedTransferCount <= result.NonZeroParticipantCount - 1);
        Assert.True(result.SimplifiedTransferCount <= 7);
        Assert.True(result.EliminatedTransfersCount >= 11);

        var totalPositiveCredit = expectedNet.Values.Where(v => v > 0m).Sum();
        Assert.Equal(totalPositiveCredit, result.TotalTransferVolume);
        Assert.Equal(totalPositiveCredit, result.Transfers.Sum(t => t.Amount));

        // Verify applying simplified transfers settles every single participant to exactly 0đ
        var postSettlementBalances = new Dictionary<Guid, decimal>(expectedNet);
        foreach (var t in result.Transfers)
        {
            Assert.True(t.Amount > 0m);
            postSettlementBalances[t.FromUserId] += t.Amount;
            postSettlementBalances[t.ToUserId] -= t.Amount;
        }

        Assert.All(postSettlementBalances.Values, bal => Assert.Equal(0m, bal));
    }

    [Fact]
    public void Simplify_ChainDebtAtoBtoC_CollapsesMiddlemanB()
    {
        var a = Guid.Parse("20000000-0000-0000-0000-000000000001");
        var b = Guid.Parse("20000000-0000-0000-0000-000000000002");
        var c = Guid.Parse("20000000-0000-0000-0000-000000000003");

        var result = _sut.SimplifyPairwiseDebts(
        [
            new PairwiseDebtInput(a, "An", b, "Bình", 200_000m),
            new PairwiseDebtInput(b, "Bình", c, "Chi", 200_000m)
        ]);

        var single = Assert.Single(result.Transfers);
        Assert.Equal(a, single.FromUserId);
        Assert.Equal(c, single.ToUserId);
        Assert.Equal(200_000m, single.Amount);
        Assert.Equal(1, result.EliminatedTransfersCount);
    }

    [Fact]
    public void Simplify_WhenLedgerSumIsNonZero_ThrowsInvalidOperationException()
    {
        var a = Guid.NewGuid();
        var b = Guid.NewGuid();

        var ex = Assert.Throws<InvalidOperationException>(() =>
            _sut.Simplify(
            [
                new MemberBalanceInput(a, "An", 150_000m),
                new MemberBalanceInput(b, "Bình", -140_000m)
            ]));

        Assert.Contains("Sổ cái không cân bằng", ex.Message);
    }
}
