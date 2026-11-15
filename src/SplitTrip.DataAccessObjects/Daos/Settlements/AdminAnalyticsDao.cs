using Microsoft.EntityFrameworkCore;
using SplitTrip.BusinessObjects.Entities;
using SplitTrip.BusinessObjects.Enums;

namespace SplitTrip.DataAccessObjects.Daos.Settlements;

public sealed record AdminAnalyticsSnapshot(
    int TotalRegisteredUsers,
    int NewUsersInRange,
    IReadOnlyList<Trip> FilteredTrips,
    IReadOnlyList<Expense> FinalizedExpenses,
    IReadOnlyList<Settlement> Settlements,
    int TotalItineraryStops,
    int AiSuggestedItineraryStops);

public sealed class AdminAnalyticsDao(SplitTripDbContext dbContext)
{
    public async Task<AdminAnalyticsSnapshot> GetPlatformSnapshotAsync(
        DateOnly? startDate = null,
        DateOnly? endDate = null,
        CancellationToken ct = default)
    {
        var totalUsers = await dbContext.Users.AsNoTracking().CountAsync(ct);

        var usersQuery = dbContext.Users.AsNoTracking();
        if (startDate.HasValue)
        {
            var fromUtc = startDate.Value.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);
            usersQuery = usersQuery.Where(u => u.CreatedAtUtc >= fromUtc);
        }

        if (endDate.HasValue)
        {
            var toExclusiveUtc = endDate.Value.AddDays(1).ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);
            usersQuery = usersQuery.Where(u => u.CreatedAtUtc < toExclusiveUtc);
        }

        var newUsersInRange = await usersQuery.CountAsync(ct);

        var tripsQuery = dbContext.Trips
            .AsNoTracking()
            .Include(t => t.OwnerUser)
            .Include(t => t.Members)
            .AsQueryable();

        if (startDate.HasValue)
        {
            tripsQuery = tripsQuery.Where(t => t.EndDate >= startDate.Value);
        }

        if (endDate.HasValue)
        {
            tripsQuery = tripsQuery.Where(t => t.StartDate <= endDate.Value);
        }

        var filteredTrips = await tripsQuery
            .OrderByDescending(t => t.CreatedAtUtc)
            .ToListAsync(ct);

        if (filteredTrips.Count == 0)
        {
            return new AdminAnalyticsSnapshot(
                totalUsers,
                newUsersInRange,
                [],
                [],
                [],
                0,
                0);
        }

        var tripIds = filteredTrips.Select(t => t.Id).ToHashSet();

        var finalizedExpenses = await dbContext.Expenses
            .AsNoTracking()
            .Include(e => e.PaidByUser)
            .Include(e => e.Splits)
            .Where(e => tripIds.Contains(e.TripId) && e.ScanStatus != ReceiptScanStatus.DraftReview)
            .OrderByDescending(e => e.SpentAtUtc)
            .ToListAsync(ct);

        var settlements = await dbContext.Settlements
            .AsNoTracking()
            .Include(s => s.FromUser)
            .Include(s => s.ToUser)
            .Where(s => tripIds.Contains(s.TripId))
            .OrderByDescending(s => s.CreatedAtUtc)
            .ToListAsync(ct);

        var itineraryItems = await dbContext.ItineraryItems
            .AsNoTracking()
            .Include(i => i.TripDay)
            .Where(i => i.TripDay != null && tripIds.Contains(i.TripDay.TripId))
            .Select(i => new { i.Id, i.IsAiSuggested })
            .ToListAsync(ct);

        var totalStops = itineraryItems.Count;
        var aiStops = itineraryItems.Count(i => i.IsAiSuggested);

        return new AdminAnalyticsSnapshot(
            totalUsers,
            newUsersInRange,
            filteredTrips,
            finalizedExpenses,
            settlements,
            totalStops,
            aiStops);
    }
}
