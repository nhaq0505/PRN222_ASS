using SplitTrip.Services.Settlements.Dtos;

namespace SplitTrip.Services.Settlements;

public interface IDebtSimplificationService
{
    DebtSimplificationResultDto Simplify(
        IReadOnlyCollection<MemberBalanceInput> balances,
        int? rawObligationCount = null);

    DebtSimplificationResultDto SimplifyPairwiseDebts(
        IReadOnlyCollection<PairwiseDebtInput> rawDebts);

    IReadOnlyList<SimplifiedTransferDto> SimplifyTransfers(
        IReadOnlyCollection<MemberBalanceInput> balances);
}
