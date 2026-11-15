namespace SplitTrip.BusinessObjects.Entities;

public partial class User
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Email { get; set; } = string.Empty;
    public string PasswordHash { get; set; } = string.Empty;
    public string FullName { get; set; } = string.Empty;
    public string? AvatarUrl { get; set; }
    public string? BankCode { get; set; }
    public string? BankBin { get; set; }
    public string? BankAccountNumber { get; set; }
    public string? BankAccountHolder { get; set; }
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;

    public ICollection<Trip> OwnedTrips { get; set; } = new List<Trip>();
    public ICollection<TripMember> TripMemberships { get; set; } = new List<TripMember>();
}
