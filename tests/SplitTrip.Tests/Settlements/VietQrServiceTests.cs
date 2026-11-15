using SplitTrip.Services.Settlements;

namespace SplitTrip.Tests.Settlements;

public sealed class VietQrServiceTests
{
    private readonly VietQrService _sut = new();

    [Fact]
    public void BuildQuickLink_WithConfiguredCreditorBank_GeneratesValidNapasUrlAndStripsVietnameseDiacritics()
    {
        var note = _sut.BuildTransferNote("DL2026", "Đặng Nguyễn Văn Ánh");
        Assert.Equal("SPLITTRIP DL2026 DANG NGUYEN VAN ANH", note);

        var qr = _sut.BuildQuickLink(
            bankBin: "970436",
            bankCode: "VCB",
            accountNumber: "0123 456 789",
            accountHolder: "Nguyễn Văn Ánh",
            creditorFullName: "Nguyễn Văn Ánh",
            amount: 425_000m,
            transferNote: note);

        Assert.True(qr.IsBankConfigured);
        Assert.Null(qr.MissingBankMessage);
        Assert.Equal("970436", qr.BankBin);
        Assert.Equal("VCB", qr.BankCode);
        Assert.Equal("Vietcombank", qr.BankShortName);
        Assert.Equal("0123456789", qr.AccountNumber);
        Assert.Equal("NGUYEN VAN ANH", qr.AccountHolder);
        Assert.Equal(
            "https://img.vietqr.io/image/970436-0123456789-compact2.png?amount=425000&addInfo=SPLITTRIP%20DL2026%20DANG%20NGUYEN%20VAN%20ANH&accountName=NGUYEN%20VAN%20ANH",
            qr.QrImageUrl);
    }

    [Fact]
    public void BuildQuickLink_WhenOnlyBankCodeProvided_ResolvesNapasBinFromCatalog()
    {
        var qr = _sut.BuildQuickLink(
            bankBin: null,
            bankCode: "MB",
            accountNumber: "8888999900",
            accountHolder: null,
            creditorFullName: "Trần Quốc Bảo",
            amount: 180_000m,
            transferNote: "SPLITTRIP HUE99");

        Assert.True(qr.IsBankConfigured);
        Assert.Equal("970422", qr.BankBin);
        Assert.Equal("MB", qr.BankCode);
        Assert.Equal("MB Bank", qr.BankShortName);
        Assert.Contains("https://img.vietqr.io/image/970422-8888999900-compact2.png?amount=180000", qr.QrImageUrl);
        Assert.Contains("accountName=TRAN%20QUOC%20BAO", qr.QrImageUrl);
    }

    [Fact]
    public void BuildQuickLink_WhenCreditorMissingBankInfo_ReturnsGracefulUnconfiguredDto()
    {
        var qr = _sut.BuildQuickLink(
            bankBin: null,
            bankCode: null,
            accountNumber: null,
            accountHolder: null,
            creditorFullName: "Lê Thu Hà",
            amount: 300_000m,
            transferNote: "SPLITTRIP SAPA01");

        Assert.False(qr.IsBankConfigured);
        Assert.Null(qr.QrImageUrl);
        Assert.NotNull(qr.MissingBankMessage);
        Assert.Contains("/Account/Profile", qr.MissingBankMessage);
    }
}
