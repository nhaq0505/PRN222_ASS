using SplitTrip.BusinessObjects.Enums;

namespace SplitTrip.BusinessObjects.Entities;

public class ItineraryVote
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid ItineraryItemId { get; set; }
    public Guid UserId { get; set; }
    public VoteType Vote { get; set; } = VoteType.Upvote;
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;

    public ItineraryItem? ItineraryItem { get; set; }
    public User? User { get; set; }
}
