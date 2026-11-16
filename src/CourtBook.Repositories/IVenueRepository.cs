using CourtBook.BusinessObjects.Entities;
using CourtBook.BusinessObjects.Enums;

namespace CourtBook.Repositories;

public interface IVenueRepository
{
    Task<Venue?> GetByIdAsync(int id, CancellationToken ct = default);
    Task<Venue?> GetByIdWithDetailsAsync(int id, CancellationToken ct = default);
    Task<Venue?> GetBySlugAsync(string slug, CancellationToken ct = default);
    Task<List<Venue>> GetMarketplaceVenuesAsync(string? keyword, string? district, SportType? sport, CancellationToken ct = default);
    Task<List<string>> GetActiveDistrictsAsync(CancellationToken ct = default);
    Task<List<Venue>> GetByOwnerAsync(int ownerId, CancellationToken ct = default);
    Task<List<Venue>> GetAllForAdminAsync(VenueStatus? statusFilter = null, CancellationToken ct = default);
    Task<bool> SlugExistsAsync(string slug, int? excludeId = null, CancellationToken ct = default);
    Task<Venue> AddAsync(Venue venue, CancellationToken ct = default);
    Task UpdateAsync(Venue venue, CancellationToken ct = default);
}
