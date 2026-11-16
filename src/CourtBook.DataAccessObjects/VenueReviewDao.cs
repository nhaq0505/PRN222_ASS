using CourtBook.BusinessObjects.Entities;
using Microsoft.EntityFrameworkCore;

namespace CourtBook.DataAccessObjects;

public class VenueReviewDao
{
    private readonly CourtBookDbContext _context;

    public VenueReviewDao(CourtBookDbContext context)
    {
        _context = context;
    }

    public async Task<List<VenueReview>> GetByVenueIdAsync(int venueId, CancellationToken ct = default)
    {
        return await _context.VenueReviews
            .AsNoTracking()
            .Include(r => r.User)
            .Where(r => r.VenueId == venueId)
            .OrderByDescending(r => r.CreatedAtUtc)
            .ToListAsync(ct);
    }

    public async Task<VenueReview> AddAsync(VenueReview review, CancellationToken ct = default)
    {
        _context.VenueReviews.Add(review);
        await _context.SaveChangesAsync(ct);
        return review;
    }

    public async Task<bool> HasUserReviewedVenueAsync(int venueId, int userId, CancellationToken ct = default)
    {
        return await _context.VenueReviews
            .AnyAsync(r => r.VenueId == venueId && r.UserId == userId, ct);
    }
}
