using CourtBook.BusinessObjects.Enums;

namespace CourtBook.BusinessObjects.Entities;

public class Court
{
    public int Id { get; set; }
    public int VenueId { get; set; }
    public Venue? Venue { get; set; }
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public SportType SportType { get; set; }
    public bool IsIndoor { get; set; }
    public string Surface { get; set; } = string.Empty;
    public decimal BasePricePerHour { get; set; }
    public decimal PeakPricePerHour { get; set; }
    public bool IsActive { get; set; } = true;
    public int DisplayOrder { get; set; }

    public ICollection<Booking> Bookings { get; set; } = new List<Booking>();

    /// <summary>
    /// Peak hours cover 17:00-21:00 (start hours 17, 18, 19, 20).
    /// </summary>
    public static bool IsPeakHour(int startHour) => startHour >= 17 && startHour < 21;

    /// <summary>
    /// Returns the hourly VND rate for the specified starting hour (6..21).
    /// </summary>
    public decimal GetPriceForHour(int startHour)
        => IsPeakHour(startHour) ? PeakPricePerHour : BasePricePerHour;
}
