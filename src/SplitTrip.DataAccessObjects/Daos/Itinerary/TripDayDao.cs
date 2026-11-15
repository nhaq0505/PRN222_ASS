using Microsoft.EntityFrameworkCore;
using SplitTrip.BusinessObjects.Entities;

namespace SplitTrip.DataAccessObjects.Daos.Itinerary;

public sealed class TripDayDao(SplitTripDbContext dbContext)
{
    public async Task<IReadOnlyList<TripDay>> GetByTripIdWithItemsAsync(
        Guid tripId,
        CancellationToken ct = default)
        => await dbContext.TripDays
            .Include(d => d.Items)
                .ThenInclude(i => i.CreatedByUser)
            .Include(d => d.Items)
                .ThenInclude(i => i.Votes)
                    .ThenInclude(v => v.User)
            .Where(d => d.TripId == tripId)
            .OrderBy(d => d.DayNumber)
            .ToListAsync(ct);

    public Task<TripDay?> GetByIdAsync(Guid id, CancellationToken ct = default)
        => dbContext.TripDays
            .Include(d => d.Trip)
            .Include(d => d.Items)
            .FirstOrDefaultAsync(d => d.Id == id, ct);

    public Task<TripDay?> GetByTripAndDayNumberAsync(
        Guid tripId,
        int dayNumber,
        CancellationToken ct = default)
        => dbContext.TripDays
            .Include(d => d.Items)
            .FirstOrDefaultAsync(d => d.TripId == tripId && d.DayNumber == dayNumber, ct);

    public async Task AddRangeAsync(IEnumerable<TripDay> days, CancellationToken ct = default)
    {
        dbContext.TripDays.AddRange(days);
        await dbContext.SaveChangesAsync(ct);
    }

    public async Task UpdateAsync(TripDay day, CancellationToken ct = default)
    {
        dbContext.TripDays.Update(day);
        await dbContext.SaveChangesAsync(ct);
    }

    public async Task UpdateRangeAsync(IEnumerable<TripDay> days, CancellationToken ct = default)
    {
        dbContext.TripDays.UpdateRange(days);
        await dbContext.SaveChangesAsync(ct);
    }
}
