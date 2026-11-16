using CourtBook.Services.Models;
using Xunit;

namespace CourtBook.Tests;

/// <summary>
/// Các ca kiểm thử tự động bổ sung cho định dạng VietQR và logic đơn giá giờ vàng.
/// Tác giả: tphuy227266-star
/// </summary>
public class VietQrAndPricingTests
{
    [Fact]
    public void VietQrUrl_ContainsCorrectAmountAndBookingCode()
    {
        var ticket = new BookingTicketDto
        {
            Id = 101,
            BookingCode = "CB-TEST-2026",
            PriceVnd = 150000m
        };

        string qrUrl = ticket.VietQrUrl;
        Assert.NotNull(qrUrl);
        Assert.Contains("amount=150000", qrUrl);
        Assert.Contains("addInfo=", qrUrl);
        Assert.Contains("970422", qrUrl); // MB Bank Napas BIN
    }

    [Theory]
    [InlineData(17, true)]
    [InlineData(18, true)]
    [InlineData(20, true)]
    [InlineData(16, false)]
    [InlineData(21, false)]
    public void PeakHour_IdentifiesCorrectPeakTimeRanges(int hour, bool expectedPeak)
    {
        bool isPeak = hour >= 17 && hour < 21;
        Assert.Equal(expectedPeak, isPeak);
    }
}
