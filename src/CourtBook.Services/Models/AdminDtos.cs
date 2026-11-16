using CourtBook.BusinessObjects.Entities;
using CourtBook.BusinessObjects.Enums;

namespace CourtBook.Services.Models;

public class OwnerVenueSummaryDto
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string District { get; set; } = string.Empty;
    public string CoverImageUrl { get; set; } = string.Empty;
    public VenueStatus Status { get; set; }
    public int CourtCount { get; set; }
}

public class OwnerPortalDto
{
    public IReadOnlyList<OwnerVenueSummaryDto> MyVenues { get; set; } = Array.Empty<OwnerVenueSummaryDto>();
    public Venue? SelectedVenue { get; set; }
    public DateOnly SelectedDate { get; set; }
    public int TodayConfirmedCount { get; set; }
    public int TodayCheckedInCount { get; set; }
    public decimal TodayRevenueVnd { get; set; }
    public decimal MonthRevenueVnd { get; set; }
    public int MonthBookingsCount { get; set; }
    public double OccupancyPercent { get; set; }
    public int ActiveHoldsCount { get; set; }
    public IReadOnlyList<BookingTicketDto> DateBookings { get; set; } = Array.Empty<BookingTicketDto>();
    public IReadOnlyList<Court> VenueCourts { get; set; } = Array.Empty<Court>();
}

public class CourtUpsertDto
{
    public int? Id { get; set; }
    public int VenueId { get; set; }
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public SportType SportType { get; set; } = SportType.Pickleball;
    public bool IsIndoor { get; set; }
    public string Surface { get; set; } = string.Empty;
    public string? ImageUrl { get; set; }
    public decimal BasePricePerHour { get; set; }
    public decimal PeakPricePerHour { get; set; }
    public bool IsActive { get; set; } = true;
    public int DisplayOrder { get; set; }
}

public class AdminVenueRowDto
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string District { get; set; } = string.Empty;
    public string Address { get; set; } = string.Empty;
    public string CoverImageUrl { get; set; } = string.Empty;
    public int OwnerId { get; set; }
    public string OwnerName { get; set; } = string.Empty;
    public string OwnerEmail { get; set; } = string.Empty;
    public int CourtCount { get; set; }
    public int TodayBookings { get; set; }
    public decimal TodayRevenueVnd { get; set; }
    public VenueStatus Status { get; set; }
    public DateTime CreatedAtUtc { get; set; }
}

public class AdminUserRowDto
{
    public int Id { get; set; }
    public string FullName { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string PhoneNumber { get; set; } = string.Empty;
    public UserRole Role { get; set; }
    public int OwnedVenueCount { get; set; }
    public bool IsActive { get; set; }
    public DateTime CreatedAtUtc { get; set; }
}

public class PlatformAdminDashboardDto
{
    public DateOnly SelectedDate { get; set; }
    public VenueStatus? StatusFilter { get; set; }
    public int ActiveVenuesCount { get; set; }
    public int PendingVenuesCount { get; set; }
    public int TotalOwnersCount { get; set; }
    public int TotalMembersCount { get; set; }
    public int TodayBookingsCount { get; set; }
    public decimal TodayPlatformGmvVnd { get; set; }
    public decimal PlatformCommissionPercent { get; set; } = 10m;
    public decimal TodayPlatformCommissionVnd => Math.Round(TodayPlatformGmvVnd * 0.10m, 0);
    public decimal TotalAllTimeGmvVnd { get; set; }
    public decimal TotalAllTimeCommissionVnd => Math.Round(TotalAllTimeGmvVnd * 0.10m, 0);
    public IReadOnlyList<AdminVenueRowDto> AllVenues { get; set; } = Array.Empty<AdminVenueRowDto>();
    public IReadOnlyList<AdminUserRowDto> AllUsers { get; set; } = Array.Empty<AdminUserRowDto>();
}
