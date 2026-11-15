using Microsoft.EntityFrameworkCore;
using SplitTrip.BusinessObjects.Entities;
using SplitTrip.BusinessObjects.Enums;

namespace SplitTrip.DataAccessObjects.Daos;

public sealed class TripDao(SplitTripDbContext dbContext)
{
    public Task<Trip?> GetByIdAsync(Guid id, CancellationToken ct = default)
        => dbContext.Trips.FirstOrDefaultAsync(t => t.Id == id, ct);

    public Task<Trip?> GetByIdWithDetailsAsync(Guid id, CancellationToken ct = default)
        => dbContext.Trips
            .Include(t => t.OwnerUser)
            .Include(t => t.Members)
                .ThenInclude(m => m.User)
            .Include(t => t.Days.OrderBy(d => d.DayNumber))
            .FirstOrDefaultAsync(t => t.Id == id, ct);

    public Task<Trip?> GetByInviteCodeAsync(string inviteCode, CancellationToken ct = default)
        => dbContext.Trips
            .Include(t => t.OwnerUser)
            .Include(t => t.Members)
                .ThenInclude(m => m.User)
            .FirstOrDefaultAsync(t => t.InviteCode == inviteCode, ct);

    public Task<bool> InviteCodeExistsAsync(string inviteCode, CancellationToken ct = default)
        => dbContext.Trips.AnyAsync(t => t.InviteCode == inviteCode, ct);

    public async Task<IReadOnlyList<Trip>> GetTripsForUserAsync(Guid userId, CancellationToken ct = default)
        => await dbContext.Trips
            .Include(t => t.OwnerUser)
            .Include(t => t.Members)
                .ThenInclude(m => m.User)
            .Where(t => t.Members.Any(m => m.UserId == userId && m.IsActive))
            .OrderByDescending(t => t.CreatedAtUtc)
            .ToListAsync(ct);

    public async Task AddTripWithOwnerAndDaysAsync(
        Trip trip,
        TripMember ownerMember,
        IEnumerable<TripDay> days,
        CancellationToken ct = default)
    {
        dbContext.Trips.Add(trip);
        dbContext.TripMembers.Add(ownerMember);
        dbContext.TripDays.AddRange(days);
        await dbContext.SaveChangesAsync(ct);
    }

    public async Task UpdateAsync(Trip trip, CancellationToken ct = default)
    {
        dbContext.Trips.Update(trip);
        await dbContext.SaveChangesAsync(ct);
    }

    public Task<bool> HasUnconfirmedSettlementsAsync(Guid tripId, CancellationToken ct = default)
        => dbContext.Settlements.AnyAsync(
            s => s.TripId == tripId && s.Status != SettlementStatus.Confirmed,
            ct);
}
