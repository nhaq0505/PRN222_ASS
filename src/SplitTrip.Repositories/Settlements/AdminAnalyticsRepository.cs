using SplitTrip.DataAccessObjects.Daos.Settlements;

namespace SplitTrip.Repositories.Settlements;

public sealed class AdminAnalyticsRepository(AdminAnalyticsDao analyticsDao) : IAdminAnalyticsRepository
{
    public Task<AdminAnalyticsSnapshot> GetPlatformSnapshotAsync(
        DateOnly? startDate = null,
        DateOnly? endDate = null,
        CancellationToken ct = default)
        => analyticsDao.GetPlatformSnapshotAsync(startDate, endDate, ct);
}
