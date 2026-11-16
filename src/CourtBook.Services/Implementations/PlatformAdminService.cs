using CourtBook.BusinessObjects.Entities;
using CourtBook.BusinessObjects.Enums;
using CourtBook.Repositories;
using CourtBook.Services.Helpers;
using CourtBook.Services.Interfaces;
using CourtBook.Services.Models;

namespace CourtBook.Services.Implementations;

public class PlatformAdminService : IPlatformAdminService
{
    private readonly IVenueRepository _venueRepository;
    private readonly IUserRepository _userRepository;
    private readonly IBookingRepository _bookingRepository;
    private readonly ISlotRealtimeNotifier _notifier;
    private readonly TimeProvider _timeProvider;

    public PlatformAdminService(
        IVenueRepository venueRepository,
        IUserRepository userRepository,
        IBookingRepository bookingRepository,
        ISlotRealtimeNotifier? notifier = null,
        TimeProvider? timeProvider = null)
    {
        _venueRepository = venueRepository;
        _userRepository = userRepository;
        _bookingRepository = bookingRepository;
        _notifier = notifier ?? NullSlotRealtimeNotifier.Instance;
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    public async Task<PlatformAdminDashboardDto> GetDashboardAsync(
        DateOnly selectedDate,
        VenueStatus? statusFilter = null,
        CancellationToken ct = default)
    {
        List<Venue> allVenues = await _venueRepository.GetAllForAdminAsync(null, ct);
        List<AppUser> allUsers = await _userRepository.GetAllAsync(ct);
        List<Booking> dateBookings = await _bookingRepository.GetBookingsForDateAsync(selectedDate, activeOnly: false, ct);

        List<Booking> validDateBookings = dateBookings
            .Where(b => b.Status == BookingStatus.Confirmed || b.Status == BookingStatus.CheckedIn)
            .ToList();

        var statsByVenue = validDateBookings
            .Where(b => b.Court is not null)
            .GroupBy(b => b.Court!.VenueId)
            .ToDictionary(
                g => g.Key,
                g => (
                    Count: g.Count(),
                    RealizedRevenue: g.Where(b => b.Status == BookingStatus.CheckedIn).Sum(b => b.PriceVnd),
                    ExpectedRevenue: g.Where(b => b.Status == BookingStatus.Confirmed).Sum(b => b.PriceVnd)));

        List<Venue> filteredVenues = statusFilter.HasValue
            ? allVenues.Where(v => v.Status == statusFilter.Value).ToList()
            : allVenues;

        List<AdminVenueRowDto> venueRows = filteredVenues.Select(v =>
        {
            statsByVenue.TryGetValue(v.Id, out var stats);
            return new AdminVenueRowDto
            {
                Id = v.Id,
                Name = v.Name,
                District = v.District,
                Address = v.Address,
                CoverImageUrl = v.CoverImageUrl,
                OwnerId = v.OwnerId,
                OwnerName = v.Owner?.FullName ?? string.Empty,
                OwnerEmail = v.Owner?.Email ?? string.Empty,
                CourtCount = v.Courts.Count,
                TodayBookings = stats.Count,
                TodayRevenueVnd = stats.RealizedRevenue,
                TodayExpectedRevenueVnd = stats.ExpectedRevenue,
                Status = v.Status,
                CreatedAtUtc = AsUtc(v.CreatedAtUtc)
            };
        }).ToList();

        List<AdminUserRowDto> userRows = allUsers.Select(u => new AdminUserRowDto
        {
            Id = u.Id,
            FullName = u.FullName,
            Email = u.Email,
            PhoneNumber = u.PhoneNumber,
            Role = u.Role,
            OwnedVenueCount = u.OwnedVenues.Count,
            IsActive = u.IsActive,
            CreatedAtUtc = AsUtc(u.CreatedAtUtc)
        }).ToList();

        decimal realizedPlatformRevenue = validDateBookings
            .Where(b => b.Status == BookingStatus.CheckedIn)
            .Sum(b => b.PriceVnd);
        decimal expectedPlatformRevenue = validDateBookings
            .Where(b => b.Status == BookingStatus.Confirmed)
            .Sum(b => b.PriceVnd);

        return new PlatformAdminDashboardDto
        {
            SelectedDate = selectedDate,
            StatusFilter = statusFilter,
            ActiveVenuesCount = allVenues.Count(v => v.Status == VenueStatus.Active),
            PendingVenuesCount = allVenues.Count(v => v.Status == VenueStatus.PendingApproval),
            TotalOwnersCount = allUsers.Count(u => u.Role == UserRole.CourtOwner),
            TotalMembersCount = allUsers.Count(u => u.Role == UserRole.Member),
            TodayBookingsCount = validDateBookings.Count,
            TodayPlatformGmvVnd = realizedPlatformRevenue + expectedPlatformRevenue,
            TodayRealizedRevenueVnd = realizedPlatformRevenue,
            TodayExpectedRevenueVnd = expectedPlatformRevenue,
            AllVenues = venueRows,
            AllUsers = userRows
        };
    }

    public async Task<ServiceResult<Venue>> ApproveVenueAsync(int venueId, CancellationToken ct = default)
    {
        Venue? venue = await _venueRepository.GetByIdAsync(venueId, ct);
        if (venue is null)
        {
            return ServiceResult<Venue>.Fail("Không tìm thấy cụm sân.");
        }

        if (venue.Status == VenueStatus.Active)
        {
            return ServiceResult<Venue>.Fail("Cụm sân này đang hoạt động trên sàn.");
        }

        if (venue.Status != VenueStatus.PendingApproval)
        {
            return ServiceResult<Venue>.Fail("Chỉ có thể duyệt cụm sân đang ở trạng thái chờ phê duyệt.");
        }

        venue.Status = VenueStatus.Active;
        await _venueRepository.UpdateAsync(venue, ct);
        return ServiceResult<Venue>.Ok(venue);
    }

    public async Task<ServiceResult<Venue>> SuspendVenueAsync(int venueId, CancellationToken ct = default)
    {
        Venue? venue = await _venueRepository.GetByIdAsync(venueId, ct);
        if (venue is null)
        {
            return ServiceResult<Venue>.Fail("Không tìm thấy cụm sân.");
        }

        if (venue.Status == VenueStatus.Suspended)
        {
            return ServiceResult<Venue>.Fail("Cụm sân này đã tạm khóa trước đó.");
        }

        venue.Status = VenueStatus.Suspended;
        await _venueRepository.UpdateAsync(venue, ct);

        DateTime utcNow = _timeProvider.GetUtcNow().UtcDateTime;
        List<Booking> activeHolds = await _bookingRepository.GetActiveHoldsByVenueAsync(venueId, utcNow, ct);
        await HoldExpirationHelper.ExpireHoldsAndBroadcastAsync(
            activeHolds,
            _bookingRepository,
            _notifier,
            fallbackVenueId: venueId,
            ct: ct);

        return ServiceResult<Venue>.Ok(venue);
    }

    public async Task<ServiceResult<Venue>> ReactivateVenueAsync(int venueId, CancellationToken ct = default)
    {
        Venue? venue = await _venueRepository.GetByIdAsync(venueId, ct);
        if (venue is null)
        {
            return ServiceResult<Venue>.Fail("Không tìm thấy cụm sân.");
        }

        if (venue.Status == VenueStatus.Active)
        {
            return ServiceResult<Venue>.Fail("Cụm sân này đang hoạt động.");
        }

        venue.Status = VenueStatus.Active;
        await _venueRepository.UpdateAsync(venue, ct);
        return ServiceResult<Venue>.Ok(venue);
    }

    public async Task<ServiceResult<AppUser>> ToggleUserActiveAsync(
        int targetUserId,
        int adminUserId,
        CancellationToken ct = default)
    {
        if (targetUserId == adminUserId)
        {
            return ServiceResult<AppUser>.Fail("Bạn không thể tự khóa tài khoản của chính mình.");
        }

        AppUser? user = await _userRepository.GetByIdAsync(targetUserId, ct);
        if (user is null)
        {
            return ServiceResult<AppUser>.Fail("Không tìm thấy người dùng.");
        }

        if (user.Role == UserRole.Admin)
        {
            return ServiceResult<AppUser>.Fail("Không thể khóa tài khoản Quản trị sàn.");
        }

        user.IsActive = !user.IsActive;
        await _userRepository.UpdateAsync(user, ct);

        if (!user.IsActive)
        {
            DateTime utcNow = _timeProvider.GetUtcNow().UtcDateTime;
            List<Booking> activeHolds = await _bookingRepository.GetActiveHoldsByUserAsync(targetUserId, null, utcNow, ct);
            await HoldExpirationHelper.ExpireHoldsAndBroadcastAsync(
                activeHolds,
                _bookingRepository,
                _notifier,
                ct: ct);
        }

        return ServiceResult<AppUser>.Ok(user);
    }

    private static DateTime AsUtc(DateTime value)
        => value.Kind == DateTimeKind.Utc ? value : DateTime.SpecifyKind(value, DateTimeKind.Utc);
}
