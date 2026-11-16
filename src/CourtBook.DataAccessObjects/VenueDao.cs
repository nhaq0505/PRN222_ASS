using CourtBook.BusinessObjects.Entities;
using CourtBook.BusinessObjects.Enums;
using Microsoft.EntityFrameworkCore;

namespace CourtBook.DataAccessObjects;

public class VenueDao
{
    private readonly CourtBookDbContext _context;

    public VenueDao(CourtBookDbContext context)
    {
        _context = context;
    }

    public Task<Venue?> GetByIdAsync(int id, CancellationToken ct = default)
        => _context.Venues.FirstOrDefaultAsync(v => v.Id == id, ct);

    public Task<Venue?> GetByIdWithDetailsAsync(int id, CancellationToken ct = default)
        => _context.Venues
            .Include(v => v.Owner)
            .Include(v => v.Courts)
            .Include(v => v.Reviews)
                .ThenInclude(r => r.User)
            .FirstOrDefaultAsync(v => v.Id == id, ct);

    public Task<Venue?> GetBySlugAsync(string slug, CancellationToken ct = default)
    {
        string normalized = slug.Trim().ToLowerInvariant();
        return _context.Venues
            .AsNoTracking()
            .Include(v => v.Owner)
            .Include(v => v.Courts)
            .FirstOrDefaultAsync(v => v.Slug.ToLower() == normalized, ct);
    }

    public Task<List<Venue>> GetMarketplaceVenuesAsync(
        string? keyword,
        string? district,
        SportType? sport,
        CancellationToken ct = default)
    {
        IQueryable<Venue> query = _context.Venues
            .AsNoTracking()
            .Include(v => v.Owner)
            .Include(v => v.Courts)
            .Include(v => v.Reviews)
            .Where(v => v.Status == VenueStatus.Active);

        if (!string.IsNullOrWhiteSpace(keyword))
        {
            string kw = keyword.Trim().ToLowerInvariant();
            query = query.Where(v =>
                v.Name.ToLower().Contains(kw) ||
                v.District.ToLower().Contains(kw) ||
                v.Address.ToLower().Contains(kw) ||
                v.Description.ToLower().Contains(kw) ||
                (v.Owner != null && v.Owner.FullName.ToLower().Contains(kw)));
        }

        if (!string.IsNullOrWhiteSpace(district))
        {
            string normalizedDistrict = district.Trim().ToLowerInvariant();
            query = query.Where(v => v.District.ToLower() == normalizedDistrict);
        }

        if (sport.HasValue)
        {
            query = query.Where(v => v.Courts.Any(c => c.IsActive && c.SportType == sport.Value));
        }

        return query
            .OrderBy(v => v.Id)
            .ToListAsync(ct);
    }

    public Task<List<string>> GetActiveDistrictsAsync(CancellationToken ct = default)
        => _context.Venues
            .AsNoTracking()
            .Where(v => v.Status == VenueStatus.Active && v.District != "")
            .Select(v => v.District)
            .Distinct()
            .OrderBy(d => d)
            .ToListAsync(ct);

    public Task<List<Venue>> GetByOwnerAsync(int ownerId, CancellationToken ct = default)
        => _context.Venues
            .AsNoTracking()
            .Include(v => v.Owner)
            .Include(v => v.Courts)
            .Where(v => v.OwnerId == ownerId)
            .OrderBy(v => v.Id)
            .ToListAsync(ct);

    public Task<List<Venue>> GetAllForAdminAsync(
        VenueStatus? statusFilter = null,
        CancellationToken ct = default)
    {
        IQueryable<Venue> query = _context.Venues
            .AsNoTracking()
            .Include(v => v.Owner)
            .Include(v => v.Courts);

        if (statusFilter.HasValue)
        {
            query = query.Where(v => v.Status == statusFilter.Value);
        }

        return query
            .OrderBy(v => v.Status)
            .ThenByDescending(v => v.CreatedAtUtc)
            .ThenBy(v => v.Id)
            .ToListAsync(ct);
    }

    public Task<bool> SlugExistsAsync(string slug, int? excludeId = null, CancellationToken ct = default)
    {
        string normalized = slug.Trim().ToLowerInvariant();
        IQueryable<Venue> query = _context.Venues.Where(v => v.Slug.ToLower() == normalized);

        if (excludeId.HasValue)
        {
            query = query.Where(v => v.Id != excludeId.Value);
        }

        return query.AnyAsync(ct);
    }

    public async Task<Venue> AddAsync(Venue venue, CancellationToken ct = default)
    {
        venue.Slug = venue.Slug.Trim().ToLowerInvariant();
        _context.Venues.Add(venue);
        await _context.SaveChangesAsync(ct);
        return venue;
    }

    public async Task UpdateAsync(Venue venue, CancellationToken ct = default)
    {
        venue.Slug = venue.Slug.Trim().ToLowerInvariant();
        if (_context.Entry(venue).State == EntityState.Detached)
        {
            _context.Venues.Update(venue);
        }
        await _context.SaveChangesAsync(ct);
    }
}
