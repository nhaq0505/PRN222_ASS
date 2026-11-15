using SplitTrip.Services.Settlements.Dtos;

namespace SplitTrip.Services.Settlements;

public interface IAdminAnalyticsService
{
    Task<AdminDashboardDto> GetDashboardAsync(
        DateOnly? startDate = null,
        DateOnly? endDate = null,
        CancellationToken ct = default);
}
