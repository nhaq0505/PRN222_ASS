using CourtBook.BusinessObjects.Entities;
using CourtBook.BusinessObjects.Enums;
using CourtBook.DataAccessObjects;

namespace CourtBook.Repositories;

public class CourtRepository : ICourtRepository
{
    private readonly CourtDao _dao;

    public CourtRepository(CourtDao dao)
    {
        _dao = dao;
    }

    public Task<List<Court>> GetAllAsync(bool activeOnly = false, SportType? sportFilter = null, CancellationToken ct = default)
        => _dao.GetAllAsync(activeOnly, sportFilter, ct);

    public Task<List<Court>> GetByVenueAsync(
        int venueId,
        bool activeOnly = false,
        SportType? sportFilter = null,
        CancellationToken ct = default)
        => _dao.GetByVenueAsync(venueId, activeOnly, sportFilter, ct);

    public Task<Court?> GetByIdAsync(int id, CancellationToken ct = default)
        => _dao.GetByIdAsync(id, ct);

    public Task<bool> CodeExistsInVenueAsync(
        int venueId,
        string code,
        int? excludeId = null,
        CancellationToken ct = default)
        => _dao.CodeExistsInVenueAsync(venueId, code, excludeId, ct);

    public Task<int> CountActiveMarketplaceCourtsAsync(CancellationToken ct = default)
        => _dao.CountActiveMarketplaceCourtsAsync(ct);

    public Task<Court> AddAsync(Court court, CancellationToken ct = default)
        => _dao.AddAsync(court, ct);

    public Task UpdateAsync(Court court, CancellationToken ct = default)
        => _dao.UpdateAsync(court, ct);
}
