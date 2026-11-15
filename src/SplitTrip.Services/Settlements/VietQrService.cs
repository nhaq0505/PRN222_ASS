using System.Globalization;
using System.Text;
using SplitTrip.Services.Auth;
using SplitTrip.Services.Settlements.Dtos;

namespace SplitTrip.Services.Settlements;

public sealed class VietQrService : IVietQrService
{
    private const int MaxTransferNoteLength = 50;

    public string BuildTransferNote(string? inviteCode, string? debtorFullName)
    {
        var codePart = SanitizeAsciiAlphanumeric(inviteCode);
        var namePart = SanitizeAsciiAlphanumeric(debtorFullName);

        var raw = string.IsNullOrWhiteSpace(codePart)
            ? $"SPLITTRIP {namePart}".Trim()
            : $"SPLITTRIP {codePart} {namePart}".Trim();

        if (string.IsNullOrWhiteSpace(raw) || raw == "SPLITTRIP")
        {
            raw = "SPLITTRIP THANH TOAN";
        }

        return raw.Length <= MaxTransferNoteLength
            ? raw
            : raw[..MaxTransferNoteLength].TrimEnd();
    }

    public VietQrPaymentInfoDto BuildQuickLink(
        string? bankBin,
        string? bankCode,
        string? accountNumber,
        string? accountHolder,
        string? creditorFullName,
        decimal amount,
        string? transferNote)
    {
        var sanitizedNote = SanitizeAsciiAlphanumeric(transferNote);
        if (string.IsNullOrWhiteSpace(sanitizedNote))
        {
            sanitizedNote = "SPLITTRIP THANH TOAN";
        }
        else if (sanitizedNote.Length > MaxTransferNoteLength)
        {
            sanitizedNote = sanitizedNote[..MaxTransferNoteLength].TrimEnd();
        }

        var truncatedAmount = Math.Max(0m, decimal.Truncate(amount));
        var matchedBank = VietQrBankCatalog.FindByBinOrCode(bankBin ?? bankCode)
                          ?? VietQrBankCatalog.FindByBinOrCode(bankCode);

        var resolvedBin = !string.IsNullOrWhiteSpace(bankBin)
            ? bankBin.Trim()
            : (matchedBank?.BankBin ?? bankCode?.Trim());

        var resolvedCode = !string.IsNullOrWhiteSpace(bankCode)
            ? bankCode.Trim().ToUpperInvariant()
            : matchedBank?.BankCode;

        var cleanAccount = CleanAccountNumber(accountNumber);

        var rawHolder = !string.IsNullOrWhiteSpace(accountHolder)
            ? accountHolder
            : creditorFullName;
        var normalizedHolder = SanitizeAsciiAlphanumeric(rawHolder);

        if (string.IsNullOrWhiteSpace(resolvedBin) || string.IsNullOrWhiteSpace(cleanAccount))
        {
            var displayName = string.IsNullOrWhiteSpace(creditorFullName) ? "Người thụ hưởng" : creditorFullName.Trim();
            return new VietQrPaymentInfoDto(
                IsBankConfigured: false,
                QrImageUrl: null,
                BankBin: resolvedBin,
                BankCode: resolvedCode,
                BankShortName: matchedBank?.ShortName ?? resolvedCode,
                BankFullName: matchedBank?.FullName,
                AccountNumber: cleanAccount,
                AccountHolder: string.IsNullOrWhiteSpace(normalizedHolder) ? null : normalizedHolder,
                Amount: truncatedAmount,
                TransferNote: sanitizedNote,
                MissingBankMessage: $"{displayName} chưa cấu hình tài khoản ngân hàng VietQR tại trang Hồ sơ cá nhân (/Account/Profile).");
        }

        var wholeVnd = ((long)truncatedAmount).ToString(CultureInfo.InvariantCulture);
        var encodedNote = Uri.EscapeDataString(sanitizedNote);
        var encodedHolder = Uri.EscapeDataString(normalizedHolder);

        var url = $"https://img.vietqr.io/image/{Uri.EscapeDataString(resolvedBin)}-{Uri.EscapeDataString(cleanAccount)}-compact2.png"
                  + $"?amount={wholeVnd}&addInfo={encodedNote}&accountName={encodedHolder}";

        return new VietQrPaymentInfoDto(
            IsBankConfigured: true,
            QrImageUrl: url,
            BankBin: resolvedBin,
            BankCode: resolvedCode,
            BankShortName: matchedBank?.ShortName ?? resolvedCode ?? resolvedBin,
            BankFullName: matchedBank?.FullName,
            AccountNumber: cleanAccount,
            AccountHolder: string.IsNullOrWhiteSpace(normalizedHolder) ? null : normalizedHolder,
            Amount: truncatedAmount,
            TransferNote: sanitizedNote,
            MissingBankMessage: null);
    }

    private static string? CleanAccountNumber(string? accountNumber)
    {
        if (string.IsNullOrWhiteSpace(accountNumber))
        {
            return null;
        }

        var sb = new StringBuilder(accountNumber.Length);
        foreach (var ch in accountNumber.Trim())
        {
            if (char.IsLetterOrDigit(ch))
            {
                sb.Append(char.ToUpperInvariant(ch));
            }
        }

        return sb.Length > 0 ? sb.ToString() : null;
    }

    private static string SanitizeAsciiAlphanumeric(string? input)
    {
        var asciiUpper = VietQrBankCatalog.NormalizeAsciiUpper(input);
        if (string.IsNullOrWhiteSpace(asciiUpper))
        {
            return string.Empty;
        }

        var sb = new StringBuilder(asciiUpper.Length);
        var prevSpace = false;

        foreach (var ch in asciiUpper)
        {
            if (ch is (>= 'A' and <= 'Z') or (>= '0' and <= '9'))
            {
                sb.Append(ch);
                prevSpace = false;
            }
            else if (char.IsWhiteSpace(ch) || ch is '-' or '_')
            {
                if (!prevSpace && sb.Length > 0)
                {
                    sb.Append(' ');
                    prevSpace = true;
                }
            }
        }

        return sb.ToString().Trim();
    }
}
