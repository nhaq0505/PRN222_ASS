using SplitTrip.BusinessObjects.Enums;

namespace SplitTrip.BusinessObjects.Entities;

public class TripMember
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid TripId { get; set; }
    public Guid UserId { get; set; }
    public TripMemberRole Role { get; set; } = TripMemberRole.Member;
    public bool IsActive { get; set; } = true;
    public DateTime JoinedAtUtc { get; set; } = DateTime.UtcNow;

    public Trip? Trip { get; set; }
    public User? User { get; set; }
}
