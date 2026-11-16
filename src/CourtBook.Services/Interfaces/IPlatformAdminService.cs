using CourtBook.BusinessObjects.Entities;
using CourtBook.BusinessObjects.Enums;
using CourtBook.Services.Models;

namespace CourtBook.Services.Interfaces;

public interface IPlatformAdminService
{
    Task<PlatformAdminDashboardDto> GetDashboardAsync(
        DateOnly selectedDate,
        VenueStatus? statusFilter = null,
        CancellationToken ct = default);

    Task<ServiceResult<Venue>> ApproveVenueAsync(int venueId, CancellationToken ct = default);
    Task<ServiceResult<Venue>> SuspendVenueAsync(int venueId, CancellationToken ct = default);
    Task<ServiceResult<Venue>> ReactivateVenueAsync(int venueId, CancellationToken ct = default);
    Task<ServiceResult<AppUser>> ToggleUserActiveAsync(int targetUserId, int adminUserId, CancellationToken ct = default);
}
