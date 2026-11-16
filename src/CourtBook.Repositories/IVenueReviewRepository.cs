using CourtBook.BusinessObjects.Entities;

namespace CourtBook.Repositories;

public interface IVenueReviewRepository
{
    Task<List<VenueReview>> GetByVenueIdAsync(int venueId, CancellationToken ct = default);
    Task<VenueReview> AddAsync(VenueReview review, CancellationToken ct = default);
    Task<bool> HasUserReviewedVenueAsync(int venueId, int userId, CancellationToken ct = default);
}
