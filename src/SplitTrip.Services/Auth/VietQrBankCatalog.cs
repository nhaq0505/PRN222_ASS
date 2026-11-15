using System.Globalization;
using System.Text;
using SplitTrip.Services.Auth.Dtos;

namespace SplitTrip.Services.Auth;

public static class VietQrBankCatalog
{
    public static readonly IReadOnlyList<VietQrBankInfo> SupportedBanks =
    [
        new("VCB", "970436", "Vietcombank", "Ngân hàng TMCP Ngoại thương Việt Nam"),
        new("TCB", "970407", "Techcombank", "Ngân hàng TMCP Kỹ thương Việt Nam"),
        new("MB", "970422", "MB Bank", "Ngân hàng TMCP Quân đội"),
        new("ICB", "970415", "VietinBank", "Ngân hàng TMCP Công thương Việt Nam"),
        new("BIDV", "970418", "BIDV", "Ngân hàng TMCP Đầu tư và Phát triển Việt Nam"),
        new("ACB", "970416", "ACB", "Ngân hàng TMCP Á Châu"),
        new("VPB", "970432", "VPBank", "Ngân hàng TMCP Việt Nam Thịnh Vượng"),
        new("TPB", "970423", "TPBank", "Ngân hàng TMCP Tiên Phong"),
        new("STB", "970403", "Sacombank", "Ngân hàng TMCP Sài Gòn Thương Tín"),
        new("VBA", "970405", "Agribank", "Ngân hàng Nông nghiệp và Phát triển Nông thôn"),
        new("VIB", "970441", "VIB", "Ngân hàng TMCP Quốc tế Việt Nam"),
        new("HDB", "970437", "HDBank", "Ngân hàng TMCP Phát triển TP.HCM"),
        new("MSB", "970426", "MSB", "Ngân hàng TMCP Hàng Hải Việt Nam"),
        new("SHB", "970443", "SHB", "Ngân hàng TMCP Sài Gòn - Hà Nội"),
        new("OCB", "970448", "OCB", "Ngân hàng TMCP Phương Đông"),
        new("LPB", "970449", "LPBank", "Ngân hàng TMCP Lộc Phát Việt Nam")
    ];

    public static VietQrBankInfo? FindByBinOrCode(string? binOrCode)
    {
        if (string.IsNullOrWhiteSpace(binOrCode))
            return null;

        var key = binOrCode.Trim();
        return SupportedBanks.FirstOrDefault(b =>
            string.Equals(b.BankBin, key, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(b.BankCode, key, StringComparison.OrdinalIgnoreCase));
    }

    public static string NormalizeAsciiUpper(string? input)
    {
        if (string.IsNullOrWhiteSpace(input))
            return string.Empty;

        var normalized = input.Trim()
            .Replace('đ', 'd')
            .Replace('Đ', 'D')
            .Normalize(NormalizationForm.FormD);

        var sb = new StringBuilder(normalized.Length);
        foreach (var ch in normalized)
        {
            var category = CharUnicodeInfo.GetUnicodeCategory(ch);
            if (category != UnicodeCategory.NonSpacingMark)
            {
                sb.Append(ch);
            }
        }

        return string.Join(' ', sb.ToString()
            .Normalize(NormalizationForm.FormC)
            .ToUpperInvariant()
            .Split(' ', StringSplitOptions.RemoveEmptyEntries));
    }
}
