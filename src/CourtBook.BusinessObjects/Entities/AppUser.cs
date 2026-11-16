using CourtBook.BusinessObjects.Enums;

namespace CourtBook.BusinessObjects.Entities;

public class AppUser
{
    public int Id { get; set; }
    public string FullName { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string PhoneNumber { get; set; } = string.Empty;
    public string PasswordHash { get; set; } = string.Empty;
    public UserRole Role { get; set; } = UserRole.Member;
    public bool IsActive { get; set; } = true;
    public decimal WalletBalanceVnd { get; set; }
    public string? AvatarUrl { get; set; }
    public string? Bio { get; set; }
    public string? PreferredDistrict { get; set; }
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;

    public ICollection<Venue> OwnedVenues { get; set; } = new List<Venue>();
    public ICollection<Booking> Bookings { get; set; } = new List<Booking>();
    public ICollection<FavoriteVenue> FavoriteVenues { get; set; } = new List<FavoriteVenue>();
    public ICollection<VenueReview> Reviews { get; set; } = new List<VenueReview>();
    public ICollection<WalletTransaction> WalletTransactions { get; set; } = new List<WalletTransaction>();
    public ICollection<UserNotification> Notifications { get; set; } = new List<UserNotification>();
}
