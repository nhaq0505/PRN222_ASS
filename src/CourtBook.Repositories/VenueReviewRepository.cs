using CourtBook.BusinessObjects.Entities;
using CourtBook.DataAccessObjects;

namespace CourtBook.Repositories;

public class VenueReviewRepository : IVenueReviewRepository
{
    private readonly VenueReviewDao _dao;

    public VenueReviewRepository(VenueReviewDao dao)
    {
        _dao = dao;
    }

    public Task<List<VenueReview>> GetByVenueIdAsync(int venueId, CancellationToken ct = default)
        => _dao.GetByVenueIdAsync(venueId, ct);

    public Task<VenueReview> AddAsync(VenueReview review, CancellationToken ct = default)
        => _dao.AddAsync(review, ct);

    public Task<bool> HasUserReviewedVenueAsync(int venueId, int userId, CancellationToken ct = default)
        => _dao.HasUserReviewedVenueAsync(venueId, userId, ct);
}
