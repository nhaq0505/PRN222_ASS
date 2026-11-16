using CourtBook.BusinessObjects.Entities;
using CourtBook.BusinessObjects.Enums;
using CourtBook.Repositories;
using CourtBook.Services.Helpers;
using CourtBook.Services.Interfaces;
using CourtBook.Services.Models;
using CourtBook.Services.Validation;

namespace CourtBook.Services.Implementations;

public class CourtService : ICourtService
{
    public const decimal MinPricePerHour = 10_000m;
    public const decimal MaxPricePerHour = 5_000_000m;

    private readonly ICourtRepository _courtRepository;
    private readonly IVenueRepository _venueRepository;
    private readonly IUserRepository _userRepository;
    private readonly IBookingRepository _bookingRepository;
    private readonly ISlotRealtimeNotifier _notifier;
    private readonly TimeProvider _timeProvider;

    public CourtService(
        ICourtRepository courtRepository,
        IVenueRepository venueRepository,
        IUserRepository userRepository,
        IBookingRepository bookingRepository,
        ISlotRealtimeNotifier notifier,
        TimeProvider? timeProvider = null)
    {
        _courtRepository = courtRepository;
        _venueRepository = venueRepository;
        _userRepository = userRepository;
        _bookingRepository = bookingRepository;
        _notifier = notifier;
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    public async Task<IReadOnlyList<Court>> GetCourtsByVenueAsync(
        int venueId,
        bool activeOnly = false,
        SportType? sportFilter = null,
        CancellationToken ct = default)
        => await _courtRepository.GetByVenueAsync(venueId, activeOnly, sportFilter, ct);

    public Task<Court?> GetByIdAsync(int courtId, CancellationToken ct = default)
        => _courtRepository.GetByIdAsync(courtId, ct);

    public async Task<ServiceResult<Court>> CreateAsync(
        int actorUserId,
        CourtUpsertDto dto,
        CancellationToken ct = default)
    {
        string? actorError = await AuthorizeCourtOwnerRoleAsync(actorUserId, ct);
        if (actorError is not null)
        {
            return ServiceResult<Court>.Fail(actorError);
        }

        if (dto is null || dto.VenueId <= 0)
        {
            return ServiceResult<Court>.Fail("Không xác định được cụm sân cho sân mới.");
        }

        Venue? venue = await _venueRepository.GetByIdAsync(dto.VenueId, ct);
        if (venue is null)
        {
            return ServiceResult<Court>.Fail("Không tìm thấy cụm sân.");
        }

        if (venue.OwnerId != actorUserId)
        {
            return ServiceResult<Court>.Fail("Bạn không có quyền thao tác sân của chủ sân khác.");
        }

        string? validationError = ValidateDto(dto, out string code, out string name, out string surface, out string? imageUrl);
        if (validationError is not null)
        {
            return ServiceResult<Court>.Fail(validationError);
        }

        if (await _courtRepository.CodeExistsInVenueAsync(dto.VenueId, code, null, ct))
        {
            return ServiceResult<Court>.Fail($"Mã sân '{code}' đã tồn tại trong cụm sân này.");
        }

        var court = new Court
        {
            VenueId = dto.VenueId,
            Code = code,
            Name = name,
            SportType = dto.SportType,
            IsIndoor = dto.IsIndoor,
            Surface = surface,
            ImageUrl = string.IsNullOrEmpty(imageUrl) ? null : imageUrl,
            BasePricePerHour = dto.BasePricePerHour,
            PeakPricePerHour = dto.PeakPricePerHour,
            IsActive = dto.IsActive,
            DisplayOrder = dto.DisplayOrder > 0 ? dto.DisplayOrder : 1
        };

        Court created = await _courtRepository.AddAsync(court, ct);
        await _notifier.NotifyCourtStatusChangedAsync(created.VenueId, created.Id, created.IsActive);
        return ServiceResult<Court>.Ok(created);
    }

    public async Task<ServiceResult<Court>> UpdateAsync(
        int actorUserId,
        CourtUpsertDto dto,
        CancellationToken ct = default)
    {
        string? actorError = await AuthorizeCourtOwnerRoleAsync(actorUserId, ct);
        if (actorError is not null)
        {
            return ServiceResult<Court>.Fail(actorError);
        }

        if (dto is null || !dto.Id.HasValue || dto.Id.Value <= 0)
        {
            return ServiceResult<Court>.Fail("Không xác định được sân cần cập nhật.");
        }

        Court? court = await _courtRepository.GetByIdAsync(dto.Id.Value, ct);
        if (court is null)
        {
            return ServiceResult<Court>.Fail("Không tìm thấy sân.");
        }

        Venue? venue = court.Venue ?? await _venueRepository.GetByIdAsync(court.VenueId, ct);
        if (venue is null)
        {
            return ServiceResult<Court>.Fail("Không tìm thấy cụm sân.");
        }

        if (venue.OwnerId != actorUserId)
        {
            return ServiceResult<Court>.Fail("Bạn không có quyền thao tác sân của chủ sân khác.");
        }

        string? validationError = ValidateDto(dto, out string code, out string name, out string surface, out string? imageUrl);
        if (validationError is not null)
        {
            return ServiceResult<Court>.Fail(validationError);
        }

        if (await _courtRepository.CodeExistsInVenueAsync(court.VenueId, code, court.Id, ct))
        {
            return ServiceResult<Court>.Fail($"Mã sân '{code}' đã tồn tại trong cụm sân này.");
        }

        court.Code = code;
        court.Name = name;
        court.SportType = dto.SportType;
        court.IsIndoor = dto.IsIndoor;
        court.Surface = surface;
        if (imageUrl is not null)
        {
            court.ImageUrl = string.IsNullOrEmpty(imageUrl) ? null : imageUrl;
        }
        court.BasePricePerHour = dto.BasePricePerHour;
        court.PeakPricePerHour = dto.PeakPricePerHour;
        court.IsActive = dto.IsActive;
        court.DisplayOrder = dto.DisplayOrder > 0 ? dto.DisplayOrder : court.DisplayOrder;

        await _courtRepository.UpdateAsync(court, ct);
        await _notifier.NotifyCourtStatusChangedAsync(court.VenueId, court.Id, court.IsActive);

        string? warning = !court.IsActive
            ? await HandleCourtMaintenanceAsync(court, ct)
            : null;

        return ServiceResult<Court>.Ok(court, warning);
    }

    public async Task<ServiceResult<Court>> ToggleActiveAsync(
        int actorUserId,
        int courtId,
        CancellationToken ct = default)
    {
        string? actorError = await AuthorizeCourtOwnerRoleAsync(actorUserId, ct);
        if (actorError is not null)
        {
            return ServiceResult<Court>.Fail(actorError);
        }

        Court? court = await _courtRepository.GetByIdAsync(courtId, ct);
        if (court is null)
        {
            return ServiceResult<Court>.Fail("Không tìm thấy sân.");
        }

        Venue? venue = court.Venue ?? await _venueRepository.GetByIdAsync(court.VenueId, ct);
        if (venue is null)
        {
            return ServiceResult<Court>.Fail("Không tìm thấy cụm sân.");
        }

        if (venue.OwnerId != actorUserId)
        {
            return ServiceResult<Court>.Fail("Bạn không có quyền thao tác sân của chủ sân khác.");
        }

        court.IsActive = !court.IsActive;
        await _courtRepository.UpdateAsync(court, ct);
        await _notifier.NotifyCourtStatusChangedAsync(court.VenueId, court.Id, court.IsActive);

        string? warning = !court.IsActive
            ? await HandleCourtMaintenanceAsync(court, ct)
            : null;

        return ServiceResult<Court>.Ok(court, warning);
    }

    private async Task<string?> HandleCourtMaintenanceAsync(Court court, CancellationToken ct)
    {
        DateTime utcNow = _timeProvider.GetUtcNow().UtcDateTime;
        List<Booking> activeHolds = await _bookingRepository.GetActiveHoldsByCourtAsync(court.Id, utcNow, ct);
        await HoldExpirationHelper.ExpireHoldsAndBroadcastAsync(
            activeHolds,
            _bookingRepository,
            _notifier,
            fallbackVenueId: court.VenueId,
            overrideCourtActive: false,
            ct: ct);

        DateTime nowVn = VietnamTimeHelper.FromUtc(utcNow);
        DateOnly todayVn = DateOnly.FromDateTime(nowVn);
        int upcomingCount = await _bookingRepository.CountUpcomingConfirmedByCourtAsync(
            court.Id,
            todayVn,
            nowVn.Hour,
            ct);

        return upcomingCount > 0
            ? $"Lưu ý: Sân này đang có {upcomingCount} đơn đã xác nhận sắp tới, vui lòng chủ động liên hệ khách hàng."
            : null;
    }

    private static string? ValidateDto(
        CourtUpsertDto? dto,
        out string code,
        out string name,
        out string surface,
        out string? imageUrl)
    {
        code = dto?.Code?.Trim().ToUpperInvariant() ?? string.Empty;
        name = dto?.Name?.Trim() ?? string.Empty;
        surface = dto?.Surface?.Trim() ?? string.Empty;
        imageUrl = null;

        if (dto is null)
        {
            return "Thông tin sân không hợp lệ.";
        }

        if (string.IsNullOrWhiteSpace(code) || code.Length > 20)
        {
            return "Mã sân không được để trống và tối đa 20 ký tự.";
        }

        if (string.IsNullOrWhiteSpace(name) || name.Length > 100)
        {
            return "Tên sân không được để trống và tối đa 100 ký tự.";
        }

        if (string.IsNullOrWhiteSpace(surface) || surface.Length > 100)
        {
            return "Loại mặt sân không được để trống và tối đa 100 ký tự.";
        }

        if (dto.BasePricePerHour < MinPricePerHour || dto.BasePricePerHour > MaxPricePerHour)
        {
            return "Giá giờ thường phải từ 10.000đ đến 5.000.000đ.";
        }

        if (dto.PeakPricePerHour < MinPricePerHour || dto.PeakPricePerHour > MaxPricePerHour)
        {
            return "Giá giờ vàng phải từ 10.000đ đến 5.000.000đ.";
        }

        if (dto.PeakPricePerHour < dto.BasePricePerHour)
        {
            return "Giá giờ vàng không được thấp hơn giá giờ thường.";
        }

        if (dto.ImageUrl is not null)
        {
            string? imageError = ImageUrlValidator.ValidateSingleUrl(dto.ImageUrl, out string normalizedImageUrl);
            if (imageError is not null)
            {
                return imageError;
            }
            imageUrl = normalizedImageUrl;
        }

        return null;
    }

    private async Task<string?> AuthorizeCourtOwnerRoleAsync(int actorUserId, CancellationToken ct)
    {
        AppUser? actor = await ActorGuard.GetActiveActorAsync(_userRepository, actorUserId, ct);
        if (actor is null)
        {
            return ActorGuard.InactiveOrMissingMessage;
        }

        if (actor.Role == UserRole.Admin)
        {
            return "Quản trị sàn không trực tiếp chỉnh sửa sân con của đối tác.";
        }

        if (actor.Role != UserRole.CourtOwner)
        {
            return "Chỉ tài khoản Chủ cụm sân mới được quản lý sân con.";
        }

        return null;
    }
}
