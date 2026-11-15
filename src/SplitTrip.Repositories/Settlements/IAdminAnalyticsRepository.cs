using SplitTrip.DataAccessObjects.Daos.Settlements;

namespace SplitTrip.Repositories.Settlements;

public interface IAdminAnalyticsRepository
{
    Task<AdminAnalyticsSnapshot> GetPlatformSnapshotAsync(
        DateOnly? startDate = null,
        DateOnly? endDate = null,
        CancellationToken ct = default);
}
