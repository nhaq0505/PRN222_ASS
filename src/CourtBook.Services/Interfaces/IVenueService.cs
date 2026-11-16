using CourtBook.BusinessObjects.Entities;
using CourtBook.BusinessObjects.Enums;
using CourtBook.Services.Models;

namespace CourtBook.Services.Interfaces;

public interface IVenueService
{
    Task<MarketplaceBrowseDto> GetMarketplaceAsync(
        string? keyword,
        string? district,
        SportType? sport,
        DateOnly todayVn,
        CancellationToken ct = default);

    Task<MarketplaceBrowseDto> GetMarketplaceAdvancedAsync(
        string? keyword,
        string? district,
        SportType? sport,
        DateOnly todayVn,
        string? sortBy = null,
        decimal? maxPrice = null,
        CancellationToken ct = default);

    Task<Venue?> GetVenueByIdAsync(int venueId, CancellationToken ct = default);

    Task<IReadOnlyList<Venue>> GetOwnerVenuesAsync(int ownerUserId, CancellationToken ct = default);

    Task<ServiceResult<Venue>> CreateVenueAsync(
        int ownerUserId,
        VenueUpsertDto dto,
        CancellationToken ct = default);

    Task<ServiceResult<Venue>> UpdateVenueAsync(
        int actorUserId,
        VenueUpsertDto dto,
        CancellationToken ct = default);
}
