using CourtBook.BusinessObjects.Enums;

namespace CourtBook.Services.Models;

public class VenueCardDto
{
    public int Id { get; set; }
    public string Slug { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string District { get; set; } = string.Empty;
    public string Address { get; set; } = string.Empty;
    public string PhoneNumber { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string AccentColor { get; set; } = "emerald";
    public string CoverImageUrl { get; set; } = string.Empty;
    public IReadOnlyList<string> PhotoUrls { get; set; } = Array.Empty<string>();
    public int PhotoCount { get; set; }
    public string OwnerName { get; set; } = string.Empty;
    public int OpenHour { get; set; } = 6;
    public int CloseHour { get; set; } = 22;
    public int PickleballCourtCount { get; set; }
    public int BadmintonCourtCount { get; set; }
    public int TotalActiveCourts { get; set; }
    public decimal MinPriceVnd { get; set; }
    public decimal MaxPriceVnd { get; set; }
    public int AvailableSlotsToday { get; set; }
}

public class MarketplaceBrowseDto
{
    public string? Keyword { get; set; }
    public string? SelectedDistrict { get; set; }
    public SportType? SportFilter { get; set; }
    public IReadOnlyList<string> AvailableDistricts { get; set; } = Array.Empty<string>();
    public IReadOnlyList<VenueCardDto> Venues { get; set; } = Array.Empty<VenueCardDto>();
    public int TotalActiveVenues { get; set; }
    public int TotalActiveCourts { get; set; }
}

public class VenueUpsertDto
{
    public int? Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string District { get; set; } = string.Empty;
    public string Address { get; set; } = string.Empty;
    public string PhoneNumber { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string AccentColor { get; set; } = "emerald";
    public string? CoverImageUrl { get; set; }
    public string? GalleryImageUrls { get; set; }
    public int OpenHour { get; set; } = 6;
    public int CloseHour { get; set; } = 22;
}
