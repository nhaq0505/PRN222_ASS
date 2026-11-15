using SplitTrip.BusinessObjects.Entities;

namespace SplitTrip.Repositories.Itinerary;

public interface IItineraryVoteRepository
{
    Task<ItineraryVote?> GetVoteAsync(Guid itineraryItemId, Guid userId, CancellationToken ct = default);
    Task<IReadOnlyList<ItineraryVote>> GetVotesByItemIdAsync(Guid itineraryItemId, CancellationToken ct = default);
    Task AddAsync(ItineraryVote vote, CancellationToken ct = default);
    Task UpdateAsync(ItineraryVote vote, CancellationToken ct = default);
    Task RemoveAsync(ItineraryVote vote, CancellationToken ct = default);
}
