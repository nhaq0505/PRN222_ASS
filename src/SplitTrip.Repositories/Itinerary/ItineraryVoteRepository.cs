using SplitTrip.BusinessObjects.Entities;
using SplitTrip.DataAccessObjects.Daos.Itinerary;

namespace SplitTrip.Repositories.Itinerary;

public sealed class ItineraryVoteRepository(ItineraryVoteDao itineraryVoteDao) : IItineraryVoteRepository
{
    public Task<ItineraryVote?> GetVoteAsync(Guid itineraryItemId, Guid userId, CancellationToken ct = default)
        => itineraryVoteDao.GetVoteAsync(itineraryItemId, userId, ct);

    public Task<IReadOnlyList<ItineraryVote>> GetVotesByItemIdAsync(Guid itineraryItemId, CancellationToken ct = default)
        => itineraryVoteDao.GetVotesByItemIdAsync(itineraryItemId, ct);

    public Task AddAsync(ItineraryVote vote, CancellationToken ct = default)
        => itineraryVoteDao.AddAsync(vote, ct);

    public Task UpdateAsync(ItineraryVote vote, CancellationToken ct = default)
        => itineraryVoteDao.UpdateAsync(vote, ct);

    public Task RemoveAsync(ItineraryVote vote, CancellationToken ct = default)
        => itineraryVoteDao.RemoveAsync(vote, ct);
}
