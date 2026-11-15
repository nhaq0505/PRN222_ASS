using Microsoft.EntityFrameworkCore;
using SplitTrip.BusinessObjects.Entities;

namespace SplitTrip.DataAccessObjects.Daos.Itinerary;

public sealed class ItineraryVoteDao(SplitTripDbContext dbContext)
{
    public Task<ItineraryVote?> GetVoteAsync(
        Guid itineraryItemId,
        Guid userId,
        CancellationToken ct = default)
        => dbContext.ItineraryVotes
            .FirstOrDefaultAsync(v => v.ItineraryItemId == itineraryItemId && v.UserId == userId, ct);

    public async Task<IReadOnlyList<ItineraryVote>> GetVotesByItemIdAsync(
        Guid itineraryItemId,
        CancellationToken ct = default)
        => await dbContext.ItineraryVotes
            .Include(v => v.User)
            .Where(v => v.ItineraryItemId == itineraryItemId)
            .ToListAsync(ct);

    public async Task AddAsync(ItineraryVote vote, CancellationToken ct = default)
    {
        dbContext.ItineraryVotes.Add(vote);
        await dbContext.SaveChangesAsync(ct);
    }

    public async Task UpdateAsync(ItineraryVote vote, CancellationToken ct = default)
    {
        dbContext.ItineraryVotes.Update(vote);
        await dbContext.SaveChangesAsync(ct);
    }

    public async Task RemoveAsync(ItineraryVote vote, CancellationToken ct = default)
    {
        dbContext.ItineraryVotes.Remove(vote);
        await dbContext.SaveChangesAsync(ct);
    }
}
