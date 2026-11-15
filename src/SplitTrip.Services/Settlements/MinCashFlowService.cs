using SplitTrip.Services.Settlements.Dtos;

namespace SplitTrip.Services.Settlements;

public sealed class MinCashFlowService : IDebtSimplificationService
{
    public DebtSimplificationResultDto Simplify(
        IReadOnlyCollection<MemberBalanceInput> balances,
        int? rawObligationCount = null)
    {
        ArgumentNullException.ThrowIfNull(balances);

        if (rawObligationCount is < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(rawObligationCount), "Số lượng giao dịch gốc không được âm.");
        }

        if (balances.Count == 0)
        {
            var rawEmpty = rawObligationCount ?? 0;
            return new DebtSimplificationResultDto([], 0, rawEmpty, 0, rawEmpty, 0m);
        }

        var aggregated = balances
            .GroupBy(b => b.UserId)
            .Select(g => new MemberBalanceInput(
                g.Key,
                g.Select(x => x.FullName).FirstOrDefault(n => !string.IsNullOrWhiteSpace(n)) ?? "Thành viên",
                g.Sum(x => decimal.Truncate(x.NetBalance))))
            .ToList();

        var netSum = aggregated.Sum(b => b.NetBalance);
        if (netSum != 0m)
        {
            throw new InvalidOperationException(
                $"Sổ cái không cân bằng tổng bằng 0 (∑ NetBalance = {netSum:N0}đ).");
        }

        var creditors = aggregated
            .Where(b => b.NetBalance > 0m)
            .Select(b => new BalanceNode(b.UserId, b.FullName, b.NetBalance))
            .ToList();

        var debtors = aggregated
            .Where(b => b.NetBalance < 0m)
            .Select(b => new BalanceNode(b.UserId, b.FullName, -b.NetBalance))
            .ToList();

        var nonZeroParticipantCount = creditors.Count + debtors.Count;
        var defaultPairwiseCount = creditors.Count * debtors.Count;
        var transfers = new List<SimplifiedTransferDto>();

        while (creditors.Count > 0 && debtors.Count > 0)
        {
            var maxCreditor = creditors
                .OrderByDescending(c => c.Remaining)
                .ThenBy(c => c.UserId)
                .First();

            var maxDebtor = debtors
                .OrderByDescending(d => d.Remaining)
                .ThenBy(d => d.UserId)
                .First();

            var transferAmount = Math.Min(maxCreditor.Remaining, maxDebtor.Remaining);

            transfers.Add(new SimplifiedTransferDto(
                maxDebtor.UserId,
                maxDebtor.FullName,
                maxCreditor.UserId,
                maxCreditor.FullName,
                transferAmount));

            maxCreditor.Remaining -= transferAmount;
            maxDebtor.Remaining -= transferAmount;

            if (maxCreditor.Remaining == 0m)
            {
                creditors.Remove(maxCreditor);
            }

            if (maxDebtor.Remaining == 0m)
            {
                debtors.Remove(maxDebtor);
            }
        }

        var baselineCount = rawObligationCount.HasValue
            ? Math.Max(rawObligationCount.Value, transfers.Count)
            : Math.Max(defaultPairwiseCount, transfers.Count);

        var eliminatedCount = Math.Max(0, baselineCount - transfers.Count);
        var totalVolume = transfers.Sum(t => t.Amount);

        return new DebtSimplificationResultDto(
            transfers,
            nonZeroParticipantCount,
            baselineCount,
            transfers.Count,
            eliminatedCount,
            totalVolume);
    }

    public DebtSimplificationResultDto SimplifyPairwiseDebts(
        IReadOnlyCollection<PairwiseDebtInput> rawDebts)
    {
        ArgumentNullException.ThrowIfNull(rawDebts);

        if (rawDebts.Any(d => d.Amount < 0m))
        {
            throw new ArgumentOutOfRangeException(nameof(rawDebts), "Khoản nợ giữa các thành viên không được âm.");
        }

        var effectiveEdges = rawDebts
            .Where(d => d.Amount > 0m && d.FromUserId != d.ToUserId)
            .ToList();

        if (effectiveEdges.Count == 0)
        {
            return new DebtSimplificationResultDto([], 0, 0, 0, 0, 0m);
        }

        var names = new Dictionary<Guid, string>();
        var netBalances = new Dictionary<Guid, decimal>();

        foreach (var edge in effectiveEdges)
        {
            var amount = decimal.Truncate(edge.Amount);
            if (amount <= 0m)
            {
                continue;
            }

            if (!string.IsNullOrWhiteSpace(edge.FromUserName))
            {
                names[edge.FromUserId] = edge.FromUserName;
            }

            if (!string.IsNullOrWhiteSpace(edge.ToUserName))
            {
                names[edge.ToUserId] = edge.ToUserName;
            }

            netBalances[edge.FromUserId] = netBalances.GetValueOrDefault(edge.FromUserId, 0m) - amount;
            netBalances[edge.ToUserId] = netBalances.GetValueOrDefault(edge.ToUserId, 0m) + amount;
        }

        var inputs = netBalances
            .Select(kvp => new MemberBalanceInput(
                kvp.Key,
                names.GetValueOrDefault(kvp.Key, "Thành viên"),
                kvp.Value))
            .ToList();

        return Simplify(inputs, rawObligationCount: effectiveEdges.Count);
    }

    public IReadOnlyList<SimplifiedTransferDto> SimplifyTransfers(
        IReadOnlyCollection<MemberBalanceInput> balances)
        => Simplify(balances).Transfers;

    private sealed class BalanceNode(Guid userId, string fullName, decimal remaining)
    {
        public Guid UserId { get; } = userId;
        public string FullName { get; } = fullName;
        public decimal Remaining { get; set; } = remaining;
    }
}
