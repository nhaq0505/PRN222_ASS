namespace CourtBook.BusinessObjects.Entities;

public class VenueReview
{
    public int Id { get; set; }
    public int VenueId { get; set; }
    public Venue? Venue { get; set; }
    public int UserId { get; set; }
    public AppUser? User { get; set; }
    public int Rating { get; set; } = 5;
    public string Comment { get; set; } = string.Empty;
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime? UpdatedAtUtc { get; set; }
}
