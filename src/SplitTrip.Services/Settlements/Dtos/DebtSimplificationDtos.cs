namespace SplitTrip.Services.Settlements.Dtos;

public sealed record MemberBalanceInput(
    Guid UserId,
    string FullName,
    decimal NetBalance);

public sealed record PairwiseDebtInput(
    Guid FromUserId,
    string FromUserName,
    Guid ToUserId,
    string ToUserName,
    decimal Amount);

public sealed record SimplifiedTransferDto(
    Guid FromUserId,
    string FromUserName,
    Guid ToUserId,
    string ToUserName,
    decimal Amount);

public sealed record DebtSimplificationResultDto(
    IReadOnlyList<SimplifiedTransferDto> Transfers,
    int NonZeroParticipantCount,
    int NaivePairwiseCount,
    int SimplifiedTransferCount,
    int EliminatedTransfersCount,
    decimal TotalTransferVolume)
{
    public double ReductionPercentage => NaivePairwiseCount > 0
        ? Math.Round((double)EliminatedTransfersCount * 100.0 / NaivePairwiseCount, 1)
        : 0.0;
}
