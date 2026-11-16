using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using CourtBook.BusinessObjects.Entities;
using CourtBook.BusinessObjects.Enums;
using CourtBook.Repositories;
using CourtBook.Services.Interfaces;
using CourtBook.Services.Models;
using CourtBook.Services.Validation;

namespace CourtBook.Services.Implementations;

public class VenueService : IVenueService
{
    private static readonly HashSet<string> AllowedAccentColors = new(StringComparer.OrdinalIgnoreCase)
    {
        "emerald",
        "teal",
        "blue",
        "amber"
    };

    private static readonly TimeZoneInfo VietnamTimeZone = ResolveVietnamTimeZone();

    private readonly IVenueRepository _venueRepository;
    private readonly ICourtRepository _courtRepository;
    private readonly IBookingRepository _bookingRepository;
    private readonly TimeProvider _timeProvider;

    public VenueService(
        IVenueRepository venueRepository,
        ICourtRepository courtRepository,
        IBookingRepository bookingRepository,
        TimeProvider? timeProvider = null)
    {
        _venueRepository = venueRepository;
        _courtRepository = courtRepository;
        _bookingRepository = bookingRepository;
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    public Task<MarketplaceBrowseDto> GetMarketplaceAsync(
        string? keyword,
        string? district,
        SportType? sport,
        DateOnly todayVn,
        CancellationToken ct = default)
        => GetMarketplaceAdvancedAsync(keyword, district, sport, todayVn, null, null, ct);

    public async Task<MarketplaceBrowseDto> GetMarketplaceAdvancedAsync(
        string? keyword,
        string? district,
        SportType? sport,
        DateOnly todayVn,
        string? sortBy = null,
        decimal? maxPrice = null,
        CancellationToken ct = default)
    {
        List<Venue> matchedVenues = await _venueRepository.GetMarketplaceVenuesAsync(keyword, district, sport, ct);
        List<string> availableDistricts = await _venueRepository.GetActiveDistrictsAsync(ct);

        bool hasFilter = !string.IsNullOrWhiteSpace(keyword) ||
                         !string.IsNullOrWhiteSpace(district) ||
                         sport.HasValue ||
                         maxPrice.HasValue;

        int totalActiveVenues = hasFilter
            ? (await _venueRepository.GetMarketplaceVenuesAsync(null, null, null, ct)).Count
            : matchedVenues.Count;

        int totalActiveCourts = await _courtRepository.CountActiveMarketplaceCourtsAsync(ct);

        DateTime utcNow = _timeProvider.GetUtcNow().UtcDateTime;
        DateTime nowVn = TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(utcNow, DateTimeKind.Utc), VietnamTimeZone);
        DateOnly currentDateVn = DateOnly.FromDateTime(nowVn);

        List<Booking> todayBookings = await _bookingRepository.GetBookingsForDateAsync(todayVn, activeOnly: true, ct);
        var activeBookedSlotsByCourt = todayBookings
            .Where(b =>
                b.Status == BookingStatus.Confirmed ||
                b.Status == BookingStatus.CheckedIn ||
                (b.Status == BookingStatus.Held && (b.HoldExpiresAtUtc == null || b.HoldExpiresAtUtc > utcNow)))
            .GroupBy(b => b.CourtId)
            .ToDictionary(
                g => g.Key,
                g => g.Select(b => b.StartHour).ToHashSet());

        var cards = new List<VenueCardDto>(matchedVenues.Count);

        foreach (Venue venue in matchedVenues)
        {
            List<Court> activeCourts = venue.Courts.Where(c => c.IsActive).ToList();
            List<Court> priceSource = activeCourts.Count > 0 ? activeCourts : venue.Courts.ToList();

            decimal minPrice = priceSource.Count > 0
                ? priceSource.Min(c => Math.Min(c.BasePricePerHour, c.PeakPricePerHour))
                : 0m;
            decimal maxPriceValue = priceSource.Count > 0
                ? priceSource.Max(c => Math.Max(c.BasePricePerHour, c.PeakPricePerHour))
                : 0m;

            if (maxPrice.HasValue && minPrice > maxPrice.Value)
            {
                continue;
            }

            int[] operatingHours = venue.GetOperatingHours();
            int[] futureHours = todayVn > currentDateVn
                ? operatingHours
                : todayVn < currentDateVn
                    ? Array.Empty<int>()
                    : operatingHours.Where(h => h > nowVn.Hour).ToArray();

            int availableSlotsToday = 0;
            if (futureHours.Length > 0 && activeCourts.Count > 0)
            {
                var futureHourSet = futureHours.ToHashSet();
                int occupiedFutureSlots = 0;

                foreach (Court court in activeCourts)
                {
                    if (activeBookedSlotsByCourt.TryGetValue(court.Id, out HashSet<int>? bookedHours))
                    {
                        occupiedFutureSlots += bookedHours.Count(h => futureHourSet.Contains(h));
                    }
                }

                availableSlotsToday = Math.Max(0, (activeCourts.Count * futureHours.Length) - occupiedFutureSlots);
            }

            IReadOnlyList<string> photoUrls = venue.GetAllPhotos();

            cards.Add(new VenueCardDto
            {
                Id = venue.Id,
                Slug = venue.Slug,
                Name = venue.Name,
                District = venue.District,
                Address = venue.Address,
                PhoneNumber = venue.PhoneNumber,
                Description = venue.Description,
                AccentColor = venue.AccentColor,
                CoverImageUrl = venue.CoverImageUrl,
                PhotoUrls = photoUrls,
                PhotoCount = photoUrls.Count,
                OwnerName = venue.Owner?.FullName ?? string.Empty,
                OpenHour = venue.OpenHour,
                CloseHour = venue.CloseHour,
                PickleballCourtCount = activeCourts.Count(c => c.SportType == SportType.Pickleball),
                BadmintonCourtCount = activeCourts.Count(c => c.SportType == SportType.Badminton),
                TotalActiveCourts = activeCourts.Count,
                MinPriceVnd = minPrice,
                MaxPriceVnd = maxPriceValue,
                AvailableSlotsToday = availableSlotsToday,
                AverageRating = venue.AverageRating,
                ReviewCount = venue.ReviewCount
            });
        }

        if (!string.IsNullOrWhiteSpace(sortBy))
        {
            cards = sortBy.ToLowerInvariant() switch
            {
                "price-asc" => cards.OrderBy(c => c.MinPriceVnd).ToList(),
                "price-desc" => cards.OrderByDescending(c => c.MinPriceVnd).ToList(),
                "rating" => cards.OrderByDescending(c => c.AverageRating).ThenByDescending(c => c.ReviewCount).ToList(),
                "courts" => cards.OrderByDescending(c => c.TotalActiveCourts).ToList(),
                _ => cards
            };
        }

        return new MarketplaceBrowseDto
        {
            Keyword = keyword?.Trim(),
            SelectedDistrict = district?.Trim(),
            SportFilter = sport,
            SortBy = sortBy,
            MaxPrice = maxPrice,
            AvailableDistricts = availableDistricts,
            Venues = cards,
            TotalActiveVenues = totalActiveVenues,
            TotalActiveCourts = totalActiveCourts
        };
    }

    public Task<Venue?> GetVenueByIdAsync(int venueId, CancellationToken ct = default)
        => _venueRepository.GetByIdWithDetailsAsync(venueId, ct);

    public async Task<IReadOnlyList<Venue>> GetOwnerVenuesAsync(int ownerUserId, CancellationToken ct = default)
        => await _venueRepository.GetByOwnerAsync(ownerUserId, ct);

    public async Task<ServiceResult<Venue>> CreateVenueAsync(
        int ownerUserId,
        bool isAdmin,
        VenueUpsertDto dto,
        CancellationToken ct = default)
    {
        string? validationError = ValidateDto(
            dto,
            out string name,
            out string district,
            out string address,
            out string phone,
            out string description,
            out string accentColor,
            out string? coverImageUrl,
            out string? galleryImageUrls);

        if (validationError is not null)
        {
            return ServiceResult<Venue>.Fail(validationError);
        }

        string baseSlug = GenerateSlug(name);
        string slug = baseSlug;
        int suffix = 2;
        while (await _venueRepository.SlugExistsAsync(slug, null, ct))
        {
            string suffixText = $"-{suffix}";
            int maxBaseLength = Math.Max(1, 120 - suffixText.Length);
            string truncatedBase = baseSlug.Length > maxBaseLength ? baseSlug[..maxBaseLength].TrimEnd('-') : baseSlug;
            slug = $"{truncatedBase}{suffixText}";
            suffix++;
        }

        var venue = new Venue
        {
            Slug = slug,
            Name = name,
            District = district,
            Address = address,
            PhoneNumber = phone,
            Description = description,
            AccentColor = accentColor,
            CoverImageUrl = coverImageUrl ?? string.Empty,
            GalleryImageUrls = galleryImageUrls ?? string.Empty,
            OpenHour = dto!.OpenHour,
            CloseHour = dto.CloseHour,
            OwnerId = ownerUserId,
            Status = isAdmin ? VenueStatus.Active : VenueStatus.PendingApproval,
            CreatedAtUtc = _timeProvider.GetUtcNow().UtcDateTime
        };

        Venue created = await _venueRepository.AddAsync(venue, ct);
        return ServiceResult<Venue>.Ok(created);
    }

    public async Task<ServiceResult<Venue>> UpdateVenueAsync(
        int actorUserId,
        bool isAdmin,
        VenueUpsertDto dto,
        CancellationToken ct = default)
    {
        if (dto is null || !dto.Id.HasValue || dto.Id.Value <= 0)
        {
            return ServiceResult<Venue>.Fail("Không xác định được cụm sân cần cập nhật.");
        }

        Venue? venue = await _venueRepository.GetByIdAsync(dto.Id.Value, ct);
        if (venue is null)
        {
            return ServiceResult<Venue>.Fail("Không tìm thấy cụm sân.");
        }

        if (!isAdmin && venue.OwnerId != actorUserId)
        {
            return ServiceResult<Venue>.Fail("Bạn không có quyền chỉnh sửa cụm sân của chủ sân khác.");
        }

        string? validationError = ValidateDto(
            dto,
            out string name,
            out string district,
            out string address,
            out string phone,
            out string description,
            out string accentColor,
            out string? coverImageUrl,
            out string? galleryImageUrls);

        if (validationError is not null)
        {
            return ServiceResult<Venue>.Fail(validationError);
        }

        venue.Name = name;
        venue.District = district;
        venue.Address = address;
        venue.PhoneNumber = phone;
        venue.Description = description;
        venue.AccentColor = accentColor;
        if (coverImageUrl is not null)
        {
            venue.CoverImageUrl = coverImageUrl;
        }
        if (galleryImageUrls is not null)
        {
            venue.GalleryImageUrls = galleryImageUrls;
        }
        venue.OpenHour = dto.OpenHour;
        venue.CloseHour = dto.CloseHour;

        await _venueRepository.UpdateAsync(venue, ct);
        return ServiceResult<Venue>.Ok(venue);
    }

    private static string? ValidateDto(
        VenueUpsertDto? dto,
        out string name,
        out string district,
        out string address,
        out string phone,
        out string description,
        out string accentColor,
        out string? coverImageUrl,
        out string? galleryImageUrls)
    {
        name = dto?.Name?.Trim() ?? string.Empty;
        district = dto?.District?.Trim() ?? string.Empty;
        address = dto?.Address?.Trim() ?? string.Empty;
        phone = dto?.PhoneNumber?.Trim() ?? string.Empty;
        description = dto?.Description?.Trim() ?? string.Empty;
        string rawColor = dto?.AccentColor?.Trim().ToLowerInvariant() ?? "emerald";
        accentColor = AllowedAccentColors.Contains(rawColor) ? rawColor : "emerald";
        coverImageUrl = null;
        galleryImageUrls = null;

        if (dto is null)
        {
            return "Thông tin cụm sân không hợp lệ.";
        }

        if (string.IsNullOrWhiteSpace(name) || name.Length > 150)
        {
            return "Tên cụm sân không được để trống và tối đa 150 ký tự.";
        }

        if (string.IsNullOrWhiteSpace(district) || district.Length > 100)
        {
            return "Khu vực / Quận không được để trống và tối đa 100 ký tự.";
        }

        if (string.IsNullOrWhiteSpace(address) || address.Length > 250)
        {
            return "Địa chỉ cụm sân không được để trống và tối đa 250 ký tự.";
        }

        if (string.IsNullOrWhiteSpace(phone) || phone.Length < 8 || phone.Length > 25)
        {
            return "Số điện thoại liên hệ phải từ 8 đến 25 ký tự.";
        }

        if (description.Length > 600)
        {
            return "Mô tả cụm sân tối đa 600 ký tự.";
        }

        if (dto.OpenHour < 5 || dto.CloseHour > 23 || dto.OpenHour >= dto.CloseHour || (dto.CloseHour - dto.OpenHour) < 4)
        {
            return "Giờ hoạt động không hợp lệ (hỗ trợ 05:00 - 23:00, tối thiểu 4 giờ/ngày).";
        }

        if (dto.CoverImageUrl is not null)
        {
            string? coverError = ImageUrlValidator.ValidateSingleUrl(dto.CoverImageUrl, out string normalizedCover);
            if (coverError is not null)
            {
                return coverError;
            }
            coverImageUrl = normalizedCover;
        }

        if (dto.GalleryImageUrls is not null)
        {
            string? galleryError = ImageUrlValidator.ValidateGalleryUrls(dto.GalleryImageUrls, out string normalizedGallery);
            if (galleryError is not null)
            {
                return galleryError;
            }
            galleryImageUrls = normalizedGallery;
        }

        return null;
    }

    private static string GenerateSlug(string name)
    {
        string ascii = RemoveVietnameseDiacritics(name).ToLowerInvariant();
        string slug = Regex.Replace(ascii, @"[^a-z0-9]+", "-").Trim('-');
        if (string.IsNullOrEmpty(slug))
        {
            return "cum-san";
        }

        return slug.Length > 100 ? slug[..100].TrimEnd('-') : slug;
    }

    private static string RemoveVietnameseDiacritics(string input)
    {
        string replaced = input.Replace('đ', 'd').Replace('Đ', 'D');
        string normalized = replaced.Normalize(NormalizationForm.FormD);
        var sb = new StringBuilder(normalized.Length);
        foreach (char ch in normalized)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(ch) != UnicodeCategory.NonSpacingMark)
            {
                sb.Append(ch);
            }
        }
        return sb.ToString().Normalize(NormalizationForm.FormC);
    }

    private static TimeZoneInfo ResolveVietnamTimeZone()
    {
        try
        {
            return TimeZoneInfo.FindSystemTimeZoneById("Asia/Ho_Chi_Minh");
        }
        catch (TimeZoneNotFoundException)
        {
            try
            {
                return TimeZoneInfo.FindSystemTimeZoneById("SE Asia Standard Time");
            }
            catch (TimeZoneNotFoundException)
            {
                return TimeZoneInfo.CreateCustomTimeZone("ICT", TimeSpan.FromHours(7), "Indochina Time", "ICT");
            }
        }
    }
}
