using CourtBook.BusinessObjects.Entities;
using CourtBook.BusinessObjects.Enums;

namespace CourtBook.Repositories;

public interface ICourtRepository
{
    Task<List<Court>> GetAllAsync(bool activeOnly = false, SportType? sportFilter = null, CancellationToken ct = default);
    Task<List<Court>> GetByVenueAsync(int venueId, bool activeOnly = false, SportType? sportFilter = null, CancellationToken ct = default);
    Task<Court?> GetByIdAsync(int id, CancellationToken ct = default);
    Task<bool> CodeExistsInVenueAsync(int venueId, string code, int? excludeId = null, CancellationToken ct = default);
    Task<int> CountActiveMarketplaceCourtsAsync(CancellationToken ct = default);
    Task<Court> AddAsync(Court court, CancellationToken ct = default);
    Task UpdateAsync(Court court, CancellationToken ct = default);
}
