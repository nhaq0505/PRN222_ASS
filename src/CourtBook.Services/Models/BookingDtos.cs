using CourtBook.BusinessObjects.Enums;

namespace CourtBook.Services.Models;

public class BookingTicketDto
{
    public int Id { get; set; }
    public string BookingCode { get; set; } = string.Empty;
    public int VenueId { get; set; }
    public string VenueName { get; set; } = string.Empty;
    public string VenueDistrict { get; set; } = string.Empty;
    public string VenueAddress { get; set; } = string.Empty;
    public int CourtId { get; set; }
    public string CourtCode { get; set; } = string.Empty;
    public string CourtName { get; set; } = string.Empty;
    public SportType SportType { get; set; }
    public bool IsIndoor { get; set; }
    public int UserId { get; set; }
    public string CustomerName { get; set; } = string.Empty;
    public string CustomerPhone { get; set; } = string.Empty;
    public DateOnly PlayDate { get; set; }
    public int StartHour { get; set; }
    public string TimeRangeLabel { get; set; } = string.Empty;
    public decimal PriceVnd { get; set; }
    public BookingStatus Status { get; set; }
    public string? Notes { get; set; }
    public string PaymentMethod { get; set; } = "Tại sân";
    public bool IsPaid { get; set; }
    public string VietQrUrl => $"https://img.vietqr.io/image/970422-0901000001-compact2.png?amount={Convert.ToInt64(PriceVnd)}&addInfo={Uri.EscapeDataString("CB " + BookingCode)}&accountName=COURTBOOK%20VN";
    public DateTime CreatedAtUtc { get; set; }
    public bool CanCancel { get; set; }
    public bool CanCheckIn { get; set; }
}

public class UserBookingsPageDto
{
    public IReadOnlyList<BookingTicketDto> Upcoming { get; set; } = Array.Empty<BookingTicketDto>();
    public IReadOnlyList<BookingTicketDto> History { get; set; } = Array.Empty<BookingTicketDto>();
}
