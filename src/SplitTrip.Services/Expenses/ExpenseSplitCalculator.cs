using SplitTrip.BusinessObjects.Enums;
using SplitTrip.Services.Expenses.Dtos;

namespace SplitTrip.Services.Expenses;

public static class ExpenseSplitCalculator
{
    private const decimal PercentageTolerance = 0.0001m;

    public static IReadOnlyDictionary<Guid, decimal> AllocateByWeights(
        decimal totalAmount,
        Guid paidByUserId,
        IReadOnlyDictionary<Guid, decimal> weights)
    {
        ArgumentNullException.ThrowIfNull(weights);

        ValidatePositiveWholeVnd(totalAmount, nameof(totalAmount), "Tổng chi phí phải là số nguyên dương (VNĐ).");

        var positive = weights.Where(kv => kv.Value > 0m).ToList();
        if (positive.Count == 0)
        {
            throw new InvalidOperationException("Phải có ít nhất một thành viên tham gia chia tiền.");
        }

        var totalWeight = positive.Sum(kv => kv.Value);
        var provisional = positive.Select(kv =>
        {
            var exact = totalAmount * kv.Value / totalWeight;
            var floored = decimal.Floor(exact);
            return (UserId: kv.Key, Floored: floored, Remainder: exact - floored);
        }).ToList();

        var remainderVnd = (int)(totalAmount - provisional.Sum(x => x.Floored));
        var winners = provisional
            .OrderByDescending(x => x.Remainder)
            .ThenBy(x => x.UserId == paidByUserId ? 0 : 1)
            .ThenBy(x => x.UserId)
            .Take(remainderVnd)
            .Select(x => x.UserId)
            .ToHashSet();

        return provisional.ToDictionary(
            x => x.UserId,
            x => x.Floored + (winners.Contains(x.UserId) ? 1m : 0m));
    }

    public static IReadOnlyList<ComputedSplitShare> CalculateSplits(
        ExpenseSplitMode splitMode,
        decimal totalAmount,
        Guid paidByUserId,
        IReadOnlyList<ParticipantShareInput> participants)
    {
        ArgumentNullException.ThrowIfNull(participants);
        ValidatePositiveWholeVnd(totalAmount, nameof(totalAmount), "Tổng chi tiêu phải là số nguyên dương (VNĐ).");

        if (participants.Count == 0)
        {
            throw new ArgumentException("Phải chọn ít nhất một thành viên tham gia chia khoản chi này.", nameof(participants));
        }

        var seenUsers = new HashSet<Guid>();
        foreach (var participant in participants)
        {
            if (participant.UserId == Guid.Empty)
            {
                throw new ArgumentException("Mã thành viên tham gia không hợp lệ.", nameof(participants));
            }

            if (!seenUsers.Add(participant.UserId))
            {
                throw new ArgumentException("Mỗi thành viên chỉ được xuất hiện một lần trong danh sách chia tiền.", nameof(participants));
            }
        }

        var result = splitMode switch
        {
            ExpenseSplitMode.Equal => CalculateEqualSplits(totalAmount, paidByUserId, participants),
            ExpenseSplitMode.ExactAmount => CalculateExactAmountSplits(totalAmount, participants),
            ExpenseSplitMode.Percentage => CalculatePercentageSplits(totalAmount, paidByUserId, participants),
            ExpenseSplitMode.Shares => CalculateWeightedShareSplits(totalAmount, paidByUserId, participants),
            _ => throw new ArgumentOutOfRangeException(
                nameof(splitMode),
                $"Chế độ chia tiền '{splitMode}' không hỗ trợ tính trực tiếp qua biểu mẫu thủ công.")
        };

        var sum = result.Sum(s => s.OwedAmount);
        if (sum != totalAmount)
        {
            throw new InvalidOperationException(
                $"Lỗi đối soát số nguyên VNĐ: tổng phần chia ({sum:N0}đ) không khớp tổng chi tiêu ({totalAmount:N0}đ).");
        }

        return result;
    }

    private static IReadOnlyList<ComputedSplitShare> CalculateEqualSplits(
        decimal totalAmount,
        Guid paidByUserId,
        IReadOnlyList<ParticipantShareInput> participants)
    {
        var weighted = participants
            .Select((p, idx) => (p.UserId, OrderIndex: idx, Weight: 1m, StoredRatio: 1m))
            .ToList();

        return ReconcileOrderedWeights(totalAmount, paidByUserId, weighted);
    }

    private static IReadOnlyList<ComputedSplitShare> CalculateExactAmountSplits(
        decimal totalAmount,
        IReadOnlyList<ParticipantShareInput> participants)
    {
        var sanitized = new List<ComputedSplitShare>(participants.Count);
        decimal runningSum = 0m;

        foreach (var p in participants)
        {
            if (p.Value < 0m || decimal.Truncate(p.Value) != p.Value)
            {
                throw new ArgumentException(
                    "Số tiền cụ thể của từng thành viên phải là số nguyên không âm (VNĐ).",
                    nameof(participants));
            }

            runningSum += p.Value;
            if (p.Value > 0m)
            {
                sanitized.Add(new ComputedSplitShare(p.UserId, p.Value, p.Value));
            }
        }

        if (runningSum != totalAmount)
        {
            var delta = totalAmount - runningSum;
            throw new ArgumentException(
                $"Tổng số tiền nhập tay ({runningSum:N0}đ) không khớp với tổng chi tiêu ({totalAmount:N0}đ). Chênh lệch: {(delta > 0 ? "+" : "")}{delta:N0}đ.",
                nameof(participants));
        }

        if (sanitized.Count == 0)
        {
            throw new ArgumentException("Phải có ít nhất một thành viên có số tiền phải trả lớn hơn 0đ.", nameof(participants));
        }

        return sanitized;
    }

    private static IReadOnlyList<ComputedSplitShare> CalculatePercentageSplits(
        decimal totalAmount,
        Guid paidByUserId,
        IReadOnlyList<ParticipantShareInput> participants)
    {
        if (participants.Any(p => p.Value <= 0m || p.Value > 100m))
        {
            throw new ArgumentException(
                "Tỷ lệ phần trăm của mỗi thành viên tham gia phải lớn hơn 0% và không vượt quá 100%.",
                nameof(participants));
        }

        var percentageSum = participants.Sum(p => p.Value);
        if (Math.Abs(percentageSum - 100m) > PercentageTolerance)
        {
            throw new ArgumentException(
                $"Tổng tỷ lệ phần trăm phải đúng bằng 100% (hiện tại: {percentageSum:0.####}%).",
                nameof(participants));
        }

        var weighted = participants
            .Select((p, idx) => (
                p.UserId,
                OrderIndex: idx,
                Weight: p.Value,
                StoredRatio: decimal.Round(p.Value, 4, MidpointRounding.AwayFromZero)))
            .ToList();

        return ReconcileOrderedWeights(totalAmount, paidByUserId, weighted);
    }

    private static IReadOnlyList<ComputedSplitShare> CalculateWeightedShareSplits(
        decimal totalAmount,
        Guid paidByUserId,
        IReadOnlyList<ParticipantShareInput> participants)
    {
        if (participants.Any(p => p.Value <= 0m))
        {
            throw new ArgumentException(
                "Hệ số phần chia (Shares) của mỗi thành viên tham gia phải lớn hơn 0.",
                nameof(participants));
        }

        var weighted = participants
            .Select((p, idx) => (
                p.UserId,
                OrderIndex: idx,
                Weight: p.Value,
                StoredRatio: decimal.Round(p.Value, 4, MidpointRounding.AwayFromZero)))
            .ToList();

        return ReconcileOrderedWeights(totalAmount, paidByUserId, weighted);
    }

    internal static IReadOnlyList<ComputedSplitShare> ReconcileOrderedWeights(
        decimal totalAmount,
        Guid paidByUserId,
        IReadOnlyList<(Guid UserId, int OrderIndex, decimal Weight, decimal StoredRatio)> participants)
    {
        var totalWeight = participants.Sum(x => x.Weight);
        if (totalWeight <= 0m)
        {
            throw new InvalidOperationException("Tổng trọng số chia tiền phải lớn hơn 0.");
        }

        var provisional = participants.Select(x =>
        {
            var exact = totalAmount * x.Weight / totalWeight;
            var floored = decimal.Floor(exact);
            return (
                x.UserId,
                x.OrderIndex,
                x.StoredRatio,
                Floored: floored,
                Remainder: exact - floored);
        }).ToList();

        var remainderVnd = (int)(totalAmount - provisional.Sum(x => x.Floored));
        var winners = provisional
            .OrderByDescending(x => x.Remainder)
            .ThenBy(x => x.UserId == paidByUserId ? 0 : 1)
            .ThenBy(x => x.OrderIndex)
            .ThenBy(x => x.UserId)
            .Take(remainderVnd)
            .Select(x => x.UserId)
            .ToHashSet();

        return provisional
            .OrderBy(x => x.OrderIndex)
            .Select(x => new ComputedSplitShare(
                x.UserId,
                x.StoredRatio,
                x.Floored + (winners.Contains(x.UserId) ? 1m : 0m)))
            .ToList();
    }

    public static void ValidatePositiveWholeVnd(decimal amount, string paramName, string message)
    {
        if (amount <= 0m || decimal.Truncate(amount) != amount)
        {
            throw new ArgumentOutOfRangeException(paramName, message);
        }
    }

    public static void ValidateNonNegativeWholeVnd(decimal amount, string paramName, string message)
    {
        if (amount < 0m || decimal.Truncate(amount) != amount)
        {
            throw new ArgumentOutOfRangeException(paramName, message);
        }
    }
}
