using SplitTrip.BusinessObjects.Enums;

namespace SplitTrip.BusinessObjects.Entities;

public partial class Trip
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = string.Empty;
    public string Destination { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string InviteCode { get; set; } = string.Empty;
    public DateOnly StartDate { get; set; }
    public DateOnly EndDate { get; set; }
    public decimal EstimatedBudgetPerPerson { get; set; }
    public TripStatus Status { get; set; } = TripStatus.Planning;
    public Guid OwnerUserId { get; set; }
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime? ClosedAtUtc { get; set; }

    public User? OwnerUser { get; set; }
    public ICollection<TripMember> Members { get; set; } = new List<TripMember>();
}
