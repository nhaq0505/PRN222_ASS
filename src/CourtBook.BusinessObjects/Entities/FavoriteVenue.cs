namespace CourtBook.BusinessObjects.Entities;

public class FavoriteVenue
{
    public int Id { get; set; }
    public int UserId { get; set; }
    public AppUser? User { get; set; }
    public int VenueId { get; set; }
    public Venue? Venue { get; set; }
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
}
