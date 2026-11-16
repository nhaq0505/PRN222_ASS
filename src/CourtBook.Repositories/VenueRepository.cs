using CourtBook.BusinessObjects.Entities;
using CourtBook.BusinessObjects.Enums;
using CourtBook.DataAccessObjects;

namespace CourtBook.Repositories;

public class VenueRepository : IVenueRepository
{
    private readonly VenueDao _dao;

    public VenueRepository(VenueDao dao)
    {
        _dao = dao;
    }

    public Task<Venue?> GetByIdAsync(int id, CancellationToken ct = default)
        => _dao.GetByIdAsync(id, ct);

    public Task<Venue?> GetByIdWithDetailsAsync(int id, CancellationToken ct = default)
        => _dao.GetByIdWithDetailsAsync(id, ct);

    public Task<Venue?> GetBySlugAsync(string slug, CancellationToken ct = default)
        => _dao.GetBySlugAsync(slug, ct);

    public Task<List<Venue>> GetMarketplaceVenuesAsync(
        string? keyword,
        string? district,
        SportType? sport,
        CancellationToken ct = default)
        => _dao.GetMarketplaceVenuesAsync(keyword, district, sport, ct);

    public Task<List<string>> GetActiveDistrictsAsync(CancellationToken ct = default)
        => _dao.GetActiveDistrictsAsync(ct);

    public Task<List<Venue>> GetByOwnerAsync(int ownerId, CancellationToken ct = default)
        => _dao.GetByOwnerAsync(ownerId, ct);

    public Task<List<Venue>> GetAllForAdminAsync(VenueStatus? statusFilter = null, CancellationToken ct = default)
        => _dao.GetAllForAdminAsync(statusFilter, ct);

    public Task<bool> SlugExistsAsync(string slug, int? excludeId = null, CancellationToken ct = default)
        => _dao.SlugExistsAsync(slug, excludeId, ct);

    public Task<Venue> AddAsync(Venue venue, CancellationToken ct = default)
        => _dao.AddAsync(venue, ct);

    public Task UpdateAsync(Venue venue, CancellationToken ct = default)
        => _dao.UpdateAsync(venue, ct);
}
