using CourtBook.BusinessObjects.Entities;
using CourtBook.BusinessObjects.Enums;
using CourtBook.Repositories;
using CourtBook.Services.Interfaces;
using CourtBook.Services.Models;

namespace CourtBook.Services.Implementations;

public class CourtService : ICourtService
{
    private readonly ICourtRepository _courtRepository;
    private readonly IVenueRepository _venueRepository;
    private readonly ISlotRealtimeNotifier _notifier;

    public CourtService(
        ICourtRepository courtRepository,
        IVenueRepository venueRepository,
        ISlotRealtimeNotifier notifier)
    {
        _courtRepository = courtRepository;
        _venueRepository = venueRepository;
        _notifier = notifier;
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
        bool isAdmin,
        CourtUpsertDto dto,
        CancellationToken ct = default)
    {
        if (dto is null || dto.VenueId <= 0)
        {
            return ServiceResult<Court>.Fail("Không xác định được cụm sân cho sân mới.");
        }

        Venue? venue = await _venueRepository.GetByIdAsync(dto.VenueId, ct);
        if (venue is null)
        {
            return ServiceResult<Court>.Fail("Không tìm thấy cụm sân.");
        }

        if (!isAdmin && venue.OwnerId != actorUserId)
        {
            return ServiceResult<Court>.Fail("Bạn không có quyền thêm sân cho cụm sân của chủ sân khác.");
        }

        string? validationError = ValidateDto(dto, out string code, out string name, out string surface);
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
        bool isAdmin,
        CourtUpsertDto dto,
        CancellationToken ct = default)
    {
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

        if (!isAdmin && venue.OwnerId != actorUserId)
        {
            return ServiceResult<Court>.Fail("Bạn không có quyền chỉnh sửa sân của chủ sân khác.");
        }

        string? validationError = ValidateDto(dto, out string code, out string name, out string surface);
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
        court.BasePricePerHour = dto.BasePricePerHour;
        court.PeakPricePerHour = dto.PeakPricePerHour;
        court.IsActive = dto.IsActive;
        court.DisplayOrder = dto.DisplayOrder > 0 ? dto.DisplayOrder : court.DisplayOrder;

        await _courtRepository.UpdateAsync(court, ct);
        await _notifier.NotifyCourtStatusChangedAsync(court.VenueId, court.Id, court.IsActive);
        return ServiceResult<Court>.Ok(court);
    }

    public async Task<ServiceResult<Court>> ToggleActiveAsync(
        int actorUserId,
        bool isAdmin,
        int courtId,
        CancellationToken ct = default)
    {
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

        if (!isAdmin && venue.OwnerId != actorUserId)
        {
            return ServiceResult<Court>.Fail("Bạn không có quyền thay đổi trạng thái sân của chủ sân khác.");
        }

        court.IsActive = !court.IsActive;
        await _courtRepository.UpdateAsync(court, ct);
        await _notifier.NotifyCourtStatusChangedAsync(court.VenueId, court.Id, court.IsActive);
        return ServiceResult<Court>.Ok(court);
    }

    private static string? ValidateDto(
        CourtUpsertDto? dto,
        out string code,
        out string name,
        out string surface)
    {
        code = dto?.Code?.Trim().ToUpperInvariant() ?? string.Empty;
        name = dto?.Name?.Trim() ?? string.Empty;
        surface = dto?.Surface?.Trim() ?? string.Empty;

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

        if (dto.BasePricePerHour <= 0)
        {
            return "Giá giờ thường phải lớn hơn 0.";
        }

        if (dto.PeakPricePerHour <= 0)
        {
            return "Giá giờ vàng phải lớn hơn 0.";
        }

        return null;
    }
}
