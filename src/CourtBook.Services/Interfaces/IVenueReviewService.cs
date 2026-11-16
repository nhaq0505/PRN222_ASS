using CourtBook.Services.Models;

namespace CourtBook.Services.Interfaces;

public interface IVenueReviewService
{
    Task<IReadOnlyList<VenueReviewDto>> GetVenueReviewsAsync(int venueId, CancellationToken ct = default);
    Task<ServiceResult<VenueReviewDto>> AddReviewAsync(int userId, CreateReviewRequestDto req, CancellationToken ct = default);
}
