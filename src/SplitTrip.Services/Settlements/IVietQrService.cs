using SplitTrip.Services.Settlements.Dtos;

namespace SplitTrip.Services.Settlements;

public interface IVietQrService
{
    string BuildTransferNote(string? inviteCode, string? debtorFullName);

    VietQrPaymentInfoDto BuildQuickLink(
        string? bankBin,
        string? bankCode,
        string? accountNumber,
        string? accountHolder,
        string? creditorFullName,
        decimal amount,
        string? transferNote);
}
