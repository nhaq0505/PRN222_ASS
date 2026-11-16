using CourtBook.BusinessObjects.Entities;
using CourtBook.BusinessObjects.Enums;
using CourtBook.Services.Models;

namespace CourtBook.Services.Interfaces;

public interface ICourtService
{
    Task<IReadOnlyList<Court>> GetCourtsByVenueAsync(
        int venueId,
        bool activeOnly = false,
        SportType? sportFilter = null,
        CancellationToken ct = default);

    Task<Court?> GetByIdAsync(int courtId, CancellationToken ct = default);

    Task<ServiceResult<Court>> CreateAsync(
        int actorUserId,
        CourtUpsertDto dto,
        CancellationToken ct = default);

    Task<ServiceResult<Court>> UpdateAsync(
        int actorUserId,
        CourtUpsertDto dto,
        CancellationToken ct = default);

    Task<ServiceResult<Court>> ToggleActiveAsync(
        int actorUserId,
        int courtId,
        CancellationToken ct = default);
}
