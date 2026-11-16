using CourtBook.BusinessObjects.Entities;
using CourtBook.BusinessObjects.Enums;
using Microsoft.EntityFrameworkCore;

namespace CourtBook.DataAccessObjects;

public class CourtDao
{
    private readonly CourtBookDbContext _context;

    public CourtDao(CourtBookDbContext context)
    {
        _context = context;
    }

    public Task<List<Court>> GetAllAsync(
        bool activeOnly = false,
        SportType? sportFilter = null,
        CancellationToken ct = default)
    {
        IQueryable<Court> query = _context.Courts
            .AsNoTracking()
            .Include(c => c.Venue);

        if (activeOnly)
        {
            query = query.Where(c => c.IsActive);
        }

        if (sportFilter.HasValue)
        {
            query = query.Where(c => c.SportType == sportFilter.Value);
        }

        return query
            .OrderBy(c => c.VenueId)
            .ThenBy(c => c.DisplayOrder)
            .ThenBy(c => c.Id)
            .ToListAsync(ct);
    }

    public Task<List<Court>> GetByVenueAsync(
        int venueId,
        bool activeOnly = false,
        SportType? sportFilter = null,
        CancellationToken ct = default)
    {
        IQueryable<Court> query = _context.Courts
            .AsNoTracking()
            .Include(c => c.Venue)
            .Where(c => c.VenueId == venueId);

        if (activeOnly)
        {
            query = query.Where(c => c.IsActive);
        }

        if (sportFilter.HasValue)
        {
            query = query.Where(c => c.SportType == sportFilter.Value);
        }

        return query
            .OrderBy(c => c.DisplayOrder)
            .ThenBy(c => c.Id)
            .ToListAsync(ct);
    }

    public Task<Court?> GetByIdAsync(int id, CancellationToken ct = default)
        => _context.Courts
            .Include(c => c.Venue)
            .FirstOrDefaultAsync(c => c.Id == id, ct);

    public Task<bool> CodeExistsInVenueAsync(
        int venueId,
        string code,
        int? excludeId = null,
        CancellationToken ct = default)
    {
        string normalized = code.Trim().ToUpperInvariant();
        IQueryable<Court> query = _context.Courts
            .Where(c => c.VenueId == venueId && c.Code.ToUpper() == normalized);

        if (excludeId.HasValue)
        {
            query = query.Where(c => c.Id != excludeId.Value);
        }

        return query.AnyAsync(ct);
    }

    public Task<int> CountActiveMarketplaceCourtsAsync(CancellationToken ct = default)
        => _context.Courts.CountAsync(
            c => c.IsActive && c.Venue != null && c.Venue.Status == VenueStatus.Active,
            ct);

    public async Task<Court> AddAsync(Court court, CancellationToken ct = default)
    {
        court.Code = court.Code.Trim().ToUpperInvariant();
        _context.Courts.Add(court);
        await _context.SaveChangesAsync(ct);
        return court;
    }

    public async Task UpdateAsync(Court court, CancellationToken ct = default)
    {
        court.Code = court.Code.Trim().ToUpperInvariant();
        if (_context.Entry(court).State == EntityState.Detached)
        {
            _context.Courts.Update(court);
        }
        await _context.SaveChangesAsync(ct);
    }
}
