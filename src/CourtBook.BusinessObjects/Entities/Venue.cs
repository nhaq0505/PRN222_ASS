using CourtBook.BusinessObjects.Enums;

namespace CourtBook.BusinessObjects.Entities;

public class Venue
{
    public int Id { get; set; }
    public string Slug { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string District { get; set; } = string.Empty;
    public string Address { get; set; } = string.Empty;
    public string PhoneNumber { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string AccentColor { get; set; } = "emerald";
    public int OpenHour { get; set; } = 6;
    public int CloseHour { get; set; } = 22;
    public int OwnerId { get; set; }
    public AppUser? Owner { get; set; }
    public VenueStatus Status { get; set; } = VenueStatus.PendingApproval;
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;

    public ICollection<Court> Courts { get; set; } = new List<Court>();

    /// <summary>
    /// Returns the operating start hours for this venue (e.g. 6..21 for 06:00 - 22:00).
    /// </summary>
    public int[] GetOperatingHours()
    {
        int start = Math.Clamp(OpenHour, 5, 22);
        int end = Math.Clamp(CloseHour, start + 1, 23);
        return Enumerable.Range(start, end - start).ToArray();
    }
}
