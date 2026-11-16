using CourtBook.BusinessObjects.Enums;

namespace CourtBook.BusinessObjects.Entities;

public class Booking
{
    public int Id { get; set; }
    public string BookingCode { get; set; } = string.Empty;
    public int CourtId { get; set; }
    public Court? Court { get; set; }
    public int UserId { get; set; }
    public AppUser? User { get; set; }
    public DateOnly PlayDate { get; set; }
    public int StartHour { get; set; }
    public decimal PriceVnd { get; set; }
    public BookingStatus Status { get; set; }
    public DateTime? HoldExpiresAtUtc { get; set; }
    public string? Notes { get; set; }
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime? ConfirmedAtUtc { get; set; }
    public DateTime? CheckedInAtUtc { get; set; }
    public DateTime? CancelledAtUtc { get; set; }
}
