using System.Globalization;
using CourtBook.BusinessObjects.Entities;
using CourtBook.BusinessObjects.Enums;
using CourtBook.Services.Interfaces;
using CourtBook.Services.Models;
using CourtBook.Web.Extensions;
using CourtBook.Web.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace CourtBook.Web.Pages.Owner;

[Authorize(Roles = "CourtOwner,Admin")]
public class IndexModel : PageModel
{
    private const int MaxVenueGalleryPhotos = 8;
    private const int MaxCreateGalleryBatch = 6;

    private readonly IBookingService _bookingService;
    private readonly IVenueService _venueService;
    private readonly ICourtService _courtService;
    private readonly IPhotoStorageService _photoStorage;

    public IndexModel(
        IBookingService bookingService,
        IVenueService venueService,
        ICourtService courtService,
        IPhotoStorageService photoStorage)
    {
        _bookingService = bookingService;
        _venueService = venueService;
        _courtService = courtService;
        _photoStorage = photoStorage;
    }

    public OwnerPortalDto Portal { get; set; } = new();
    public DateOnly SelectedDate { get; set; }
    public string ActiveTab { get; set; } = "bookings";
    public int? EditCourtId { get; set; }
    public bool ShowNewVenueForm { get; set; }
    public bool RemoveCourtPhotoState { get; set; }

    public VenueUpsertDto VenueForm { get; set; } = CreateDefaultVenueForm();
    public CourtUpsertDto CourtForm { get; set; } = new();

    public bool IsEditingCourt => EditCourtId.HasValue && CourtForm.Id.HasValue;

    public async Task<IActionResult> OnGetAsync(
        int? venueId,
        string? date,
        string tab = "bookings",
        int? editCourtId = null,
        string? mode = null)
    {
        int? userId = User.GetUserId();
        if (userId is null)
        {
            return Challenge();
        }

        SelectedDate = ParseDateOrToday(date);
        Portal = await _bookingService.GetOwnerPortalAsync(
            userId.Value,
            User.IsAdmin(),
            venueId,
            SelectedDate,
            HttpContext.RequestAborted);

        ShowNewVenueForm = Portal.MyVenues.Count == 0
            || string.Equals(mode, "newvenue", StringComparison.OrdinalIgnoreCase);

        if (ShowNewVenueForm || Portal.SelectedVenue is null)
        {
            VenueForm = CreateDefaultVenueForm();
            return Page();
        }

        ActiveTab = editCourtId.HasValue || string.Equals(tab, "courts", StringComparison.OrdinalIgnoreCase)
            ? "courts"
            : string.Equals(tab, "settings", StringComparison.OrdinalIgnoreCase)
                ? "settings"
                : "bookings";

        Venue selectedVenue = Portal.SelectedVenue;
        VenueForm = MapVenueForm(selectedVenue);

        if (editCourtId.HasValue)
        {
            Court? court = Portal.VenueCourts.FirstOrDefault(c => c.Id == editCourtId.Value)
                ?? await _courtService.GetByIdAsync(editCourtId.Value, HttpContext.RequestAborted);

            if (court is not null && court.VenueId == selectedVenue.Id)
            {
                EditCourtId = court.Id;
                CourtForm = new CourtUpsertDto
                {
                    Id = court.Id,
                    VenueId = court.VenueId,
                    Code = court.Code,
                    Name = court.Name,
                    SportType = court.SportType,
                    IsIndoor = court.IsIndoor,
                    Surface = court.Surface,
                    ImageUrl = court.ImageUrl,
                    BasePricePerHour = court.BasePricePerHour,
                    PeakPricePerHour = court.PeakPricePerHour,
                    IsActive = court.IsActive,
                    DisplayOrder = court.DisplayOrder
                };
            }
            else
            {
                TempData["ToastError"] = "Không tìm thấy thông tin sân cần chỉnh sửa.";
                CourtForm = CreateDefaultCourtForm(selectedVenue.Id, Portal.VenueCourts.Count + 1);
            }
        }
        else
        {
            CourtForm = CreateDefaultCourtForm(selectedVenue.Id, Portal.VenueCourts.Count + 1);
        }

        return Page();
    }

    public async Task<IActionResult> OnPostCreateVenueAsync(
        VenueUpsertDto venueForm,
        IFormFile? coverPhoto,
        List<IFormFile>? galleryPhotos)
    {
        int? userId = User.GetUserId();
        if (userId is null)
        {
            return Challenge();
        }

        NormalizeVenueForm(venueForm);

        if (coverPhoto is { Length: > 0 })
        {
            ServiceResult<string> coverResult = await _photoStorage.SavePhotoAsync(
                coverPhoto,
                "venues",
                HttpContext.RequestAborted);
            if (!coverResult.Succeeded || string.IsNullOrWhiteSpace(coverResult.Data))
            {
                return await RenderCreateVenueErrorAsync(
                    userId.Value,
                    venueForm,
                    coverResult.ErrorMessage ?? "Không thể lưu ảnh bìa cụm sân.");
            }

            venueForm.CoverImageUrl = coverResult.Data;
        }

        if (galleryPhotos is { Count: > 0 })
        {
            List<string> existingGallery = ParseGalleryUrls(venueForm.GalleryImageUrls);
            ServiceResult<List<string>> galleryResult = await _photoStorage.SavePhotosAsync(
                galleryPhotos,
                "venues",
                MaxCreateGalleryBatch,
                HttpContext.RequestAborted);
            if (!galleryResult.Succeeded)
            {
                return await RenderCreateVenueErrorAsync(
                    userId.Value,
                    venueForm,
                    galleryResult.ErrorMessage ?? "Không thể lưu thư viện ảnh cụm sân.");
            }

            List<string> mergedGallery = existingGallery
                .Concat(galleryResult.Data ?? Enumerable.Empty<string>())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Take(MaxVenueGalleryPhotos)
                .ToList();

            venueForm.GalleryImageUrls = string.Join(';', mergedGallery);

            if (string.IsNullOrWhiteSpace(venueForm.CoverImageUrl) && mergedGallery.Count > 0)
            {
                venueForm.CoverImageUrl = mergedGallery[0];
            }
        }

        ServiceResult<Venue> result = await _venueService.CreateVenueAsync(
            userId.Value,
            User.IsAdmin(),
            venueForm,
            HttpContext.RequestAborted);

        if (result.Succeeded && result.Data is not null)
        {
            TempData["ToastSuccess"] = result.Data.Status == VenueStatus.Active
                ? $"Đã tạo và mở hoạt động cụm sân {result.Data.Name}."
                : "Đăng ký cụm sân thành công! Cụm sân đang chờ Quản trị sàn phê duyệt.";

            return RedirectToPage("/Owner/Index", new { venueId = result.Data.Id });
        }

        return await RenderCreateVenueErrorAsync(
            userId.Value,
            venueForm,
            result.ErrorMessage ?? "Không thể đăng ký cụm sân mới.");
    }

    public async Task<IActionResult> OnPostUpdateVenueAsync(
        VenueUpsertDto venueForm,
        string? date,
        IFormFile? coverPhoto,
        List<IFormFile>? galleryPhotos)
    {
        int? userId = User.GetUserId();
        if (userId is null)
        {
            return Challenge();
        }

        SelectedDate = ParseDateOrToday(date);
        string formattedDate = SelectedDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

        NormalizeVenueForm(venueForm);
        // Edit Venue always posts hidden GalleryImageUrls; treat null (empty input) as cleared gallery.
        List<string> existingGallery = ParseGalleryUrls(venueForm.GalleryImageUrls);
        venueForm.GalleryImageUrls = string.Join(';', existingGallery.Take(MaxVenueGalleryPhotos));

        if (coverPhoto is { Length: > 0 })
        {
            ServiceResult<string> coverResult = await _photoStorage.SavePhotoAsync(
                coverPhoto,
                "venues",
                HttpContext.RequestAborted);
            if (!coverResult.Succeeded || string.IsNullOrWhiteSpace(coverResult.Data))
            {
                return await RenderUpdateVenueErrorAsync(
                    userId.Value,
                    venueForm,
                    coverResult.ErrorMessage ?? "Không thể lưu ảnh bìa cụm sân.");
            }

            venueForm.CoverImageUrl = coverResult.Data;
        }

        if (galleryPhotos is { Count: > 0 })
        {
            int remainingCapacity = MaxVenueGalleryPhotos - existingGallery.Count;
            if (remainingCapacity <= 0)
            {
                return await RenderUpdateVenueErrorAsync(
                    userId.Value,
                    venueForm,
                    $"Thư viện ảnh cụm sân tối đa {MaxVenueGalleryPhotos} ảnh. Hãy xóa bớt ảnh cũ trước khi tải thêm.");
            }

            if (galleryPhotos.Count > remainingCapacity)
            {
                return await RenderUpdateVenueErrorAsync(
                    userId.Value,
                    venueForm,
                    $"Thư viện ảnh tối đa {MaxVenueGalleryPhotos} ảnh (chỉ có thể thêm tối đa {remainingCapacity} ảnh nữa).");
            }

            ServiceResult<List<string>> galleryResult = await _photoStorage.SavePhotosAsync(
                galleryPhotos,
                "venues",
                remainingCapacity,
                HttpContext.RequestAborted);
            if (!galleryResult.Succeeded)
            {
                return await RenderUpdateVenueErrorAsync(
                    userId.Value,
                    venueForm,
                    galleryResult.ErrorMessage ?? "Không thể lưu thư viện ảnh cụm sân.");
            }

            List<string> mergedGallery = existingGallery
                .Concat(galleryResult.Data ?? Enumerable.Empty<string>())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Take(MaxVenueGalleryPhotos)
                .ToList();

            venueForm.GalleryImageUrls = string.Join(';', mergedGallery);
        }

        ServiceResult<Venue> result = await _venueService.UpdateVenueAsync(
            userId.Value,
            User.IsAdmin(),
            venueForm,
            HttpContext.RequestAborted);

        if (result.Succeeded && result.Data is not null)
        {
            TempData["ToastSuccess"] = $"Đã cập nhật thông tin cụm sân {result.Data.Name}.";
            return RedirectToPage("/Owner/Index", new
            {
                venueId = result.Data.Id,
                date = formattedDate,
                tab = "settings"
            });
        }

        return await RenderUpdateVenueErrorAsync(
            userId.Value,
            venueForm,
            result.ErrorMessage ?? "Không thể cập nhật thông tin cụm sân.");
    }

    public async Task<IActionResult> OnPostCheckInAsync(int bookingId, int venueId, string? date)
    {
        int? actorUserId = User.GetUserId();
        if (actorUserId is null)
        {
            return Challenge();
        }

        UserRole actorRole = User.GetUserRole() ?? UserRole.CourtOwner;
        ServiceResult<BookingTicketDto> result = await _bookingService.CheckInBookingAsync(
            bookingId,
            actorUserId.Value,
            actorRole,
            HttpContext.RequestAborted);

        if (result.Succeeded)
        {
            string code = result.Data?.BookingCode ?? "";
            TempData["ToastSuccess"] = string.IsNullOrEmpty(code)
                ? "Check-in thành công."
                : $"Đã check-in đơn {code} thành công.";
        }
        else
        {
            TempData["ToastError"] = result.ErrorMessage ?? "Không thể check-in đơn đặt sân này.";
        }

        return RedirectToPage("/Owner/Index", new
        {
            venueId,
            date = ParseDateOrToday(date).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            tab = "bookings"
        });
    }

    public async Task<IActionResult> OnPostCancelBookingAsync(int bookingId, int venueId, string? date)
    {
        int? actorUserId = User.GetUserId();
        if (actorUserId is null)
        {
            return Challenge();
        }

        UserRole actorRole = User.GetUserRole() ?? UserRole.CourtOwner;
        ServiceResult<BookingTicketDto> result = await _bookingService.CancelBookingAsync(
            bookingId,
            actorUserId.Value,
            actorRole,
            HttpContext.RequestAborted);

        if (result.Succeeded)
        {
            string code = result.Data?.BookingCode ?? "";
            TempData["ToastSuccess"] = string.IsNullOrEmpty(code)
                ? "Đã hủy đơn đặt sân thành công."
                : $"Đã hủy đơn {code} thành công.";
        }
        else
        {
            TempData["ToastError"] = result.ErrorMessage ?? "Không thể hủy đơn đặt sân này.";
        }

        return RedirectToPage("/Owner/Index", new
        {
            venueId,
            date = ParseDateOrToday(date).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            tab = "bookings"
        });
    }

    public async Task<IActionResult> OnGetExportCsvAsync(int venueId, string? date)
    {
        int? actorUserId = User.GetUserId();
        if (actorUserId is null)
        {
            return Challenge();
        }

        try
        {
            byte[] csvBytes = await _bookingService.ExportOwnerBookingsCsvAsync(
                venueId,
                actorUserId.Value,
                User.IsAdmin(),
                null,
                null,
                HttpContext.RequestAborted);

            return File(csvBytes, "text/csv; charset=utf-8", $"courtbook-venue-{venueId}-bookings.csv");
        }
        catch (Exception ex)
        {
            TempData["ToastError"] = ex.Message;
            return RedirectToPage("/Owner/Index", new { venueId, date, tab = "bookings" });
        }
    }

    public async Task<IActionResult> OnPostQuickCheckInAsync(string? bookingCode, int venueId, string? date)
    {
        int? actorUserId = User.GetUserId();
        if (actorUserId is null)
        {
            return Challenge();
        }

        if (string.IsNullOrWhiteSpace(bookingCode))
        {
            TempData["ToastError"] = "Vui lòng nhập mã đặt sân cần check-in.";
            return RedirectToPage("/Owner/Index", new { venueId, date, tab = "bookings" });
        }

        ServiceResult<BookingTicketDto> result = await _bookingService.QuickCheckInByCodeAsync(
            bookingCode,
            actorUserId.Value,
            User.IsAdmin(),
            HttpContext.RequestAborted);

        if (result.Succeeded && result.Data is not null)
        {
            TempData["ToastSuccess"] = $"Check-in thành công đơn #{result.Data.BookingCode} ({result.Data.CustomerName})!";
        }
        else
        {
            TempData["ToastError"] = result.ErrorMessage ?? "Không thể check-in đơn này.";
        }

        return RedirectToPage("/Owner/Index", new { venueId, date, tab = "bookings" });
    }

    public async Task<IActionResult> OnPostTogglePaymentAsync(int bookingId, int venueId, string? date)
    {
        int? actorUserId = User.GetUserId();
        if (actorUserId is null)
        {
            return Challenge();
        }

        ServiceResult<bool> result = await _bookingService.TogglePaymentStatusAsync(
            bookingId,
            actorUserId.Value,
            User.IsAdmin(),
            HttpContext.RequestAborted);

        if (result.Succeeded)
        {
            TempData["ToastSuccess"] = result.Data ? "Đã cập nhật: Đã thanh toán." : "Đã cập nhật: Chưa thanh toán.";
        }
        else
        {
            TempData["ToastError"] = result.ErrorMessage ?? "Không thể cập nhật trạng thái thanh toán.";
        }

        return RedirectToPage("/Owner/Index", new { venueId, date, tab = "bookings" });
    }

    public async Task<IActionResult> OnPostToggleCourtAsync(int courtId, int venueId, string? date)
    {
        int? actorUserId = User.GetUserId();
        if (actorUserId is null)
        {
            return Challenge();
        }

        ServiceResult<Court> result = await _courtService.ToggleActiveAsync(
            actorUserId.Value,
            User.IsAdmin(),
            courtId,
            HttpContext.RequestAborted);

        if (result.Succeeded && result.Data is not null)
        {
            string statusText = result.Data.IsActive ? "đã mở hoạt động" : "đã chuyển bảo trì";
            TempData["ToastSuccess"] = $"{result.Data.Name} {statusText}.";
        }
        else
        {
            TempData["ToastError"] = result.ErrorMessage ?? "Không thể cập nhật trạng thái sân.";
        }

        return RedirectToPage("/Owner/Index", new
        {
            venueId,
            date = ParseDateOrToday(date).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            tab = "courts"
        });
    }

    public async Task<IActionResult> OnPostSaveCourtAsync(
        CourtUpsertDto courtForm,
        string? date,
        IFormFile? courtPhoto,
        bool removeCourtPhoto = false)
    {
        int? actorUserId = User.GetUserId();
        if (actorUserId is null)
        {
            return Challenge();
        }

        SelectedDate = ParseDateOrToday(date);
        string formattedDate = SelectedDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

        courtForm.Code = (courtForm.Code ?? string.Empty).Trim().ToUpperInvariant();
        courtForm.Name = (courtForm.Name ?? string.Empty).Trim();
        courtForm.Surface = (courtForm.Surface ?? string.Empty).Trim();
        courtForm.ImageUrl = string.IsNullOrWhiteSpace(courtForm.ImageUrl) ? null : courtForm.ImageUrl.Trim();

        bool isUpdate = courtForm.Id.HasValue && courtForm.Id.Value > 0;

        if (courtPhoto is { Length: > 0 })
        {
            ServiceResult<string> photoResult = await _photoStorage.SavePhotoAsync(
                courtPhoto,
                "courts",
                HttpContext.RequestAborted);
            if (!photoResult.Succeeded || string.IsNullOrWhiteSpace(photoResult.Data))
            {
                return await RenderSaveCourtErrorAsync(
                    actorUserId.Value,
                    courtForm,
                    isUpdate,
                    removeCourtPhoto,
                    photoResult.ErrorMessage ?? "Không thể lưu ảnh sân.");
            }

            courtForm.ImageUrl = photoResult.Data;
        }
        else if (removeCourtPhoto)
        {
            courtForm.ImageUrl = string.Empty;
        }

        ServiceResult<Court> result = isUpdate
            ? await _courtService.UpdateAsync(actorUserId.Value, User.IsAdmin(), courtForm, HttpContext.RequestAborted)
            : await _courtService.CreateAsync(actorUserId.Value, User.IsAdmin(), courtForm, HttpContext.RequestAborted);

        if (result.Succeeded && result.Data is not null)
        {
            TempData["ToastSuccess"] = isUpdate
                ? $"Đã cập nhật sân {result.Data.Name}."
                : $"Đã thêm sân mới {result.Data.Name}.";

            return RedirectToPage("/Owner/Index", new
            {
                venueId = result.Data.VenueId,
                date = formattedDate,
                tab = "courts"
            });
        }

        return await RenderSaveCourtErrorAsync(
            actorUserId.Value,
            courtForm,
            isUpdate,
            removeCourtPhoto,
            result.ErrorMessage ?? "Không thể lưu thông tin sân.");
    }

    public static List<string> ParseGalleryUrls(string? galleryImageUrls)
    {
        if (string.IsNullOrWhiteSpace(galleryImageUrls))
        {
            return new List<string>();
        }

        string[] parts = galleryImageUrls.Split(
            [';', '|'],
            StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        var result = new List<string>(parts.Length);
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (string part in parts)
        {
            if (!string.IsNullOrWhiteSpace(part) && seen.Add(part))
            {
                result.Add(part);
            }
        }

        return result;
    }

    private async Task<IActionResult> RenderCreateVenueErrorAsync(
        int userId,
        VenueUpsertDto venueForm,
        string errorMessage)
    {
        TempData["ToastError"] = errorMessage;
        SelectedDate = GetVietnamToday();
        Portal = await _bookingService.GetOwnerPortalAsync(
            userId,
            User.IsAdmin(),
            null,
            SelectedDate,
            HttpContext.RequestAborted);
        ShowNewVenueForm = true;
        VenueForm = venueForm;
        return Page();
    }

    private async Task<IActionResult> RenderUpdateVenueErrorAsync(
        int userId,
        VenueUpsertDto venueForm,
        string errorMessage)
    {
        TempData["ToastError"] = errorMessage;
        Portal = await _bookingService.GetOwnerPortalAsync(
            userId,
            User.IsAdmin(),
            venueForm.Id,
            SelectedDate,
            HttpContext.RequestAborted);
        ActiveTab = "settings";
        ShowNewVenueForm = false;
        VenueForm = venueForm;
        if (Portal.SelectedVenue is not null)
        {
            CourtForm = CreateDefaultCourtForm(Portal.SelectedVenue.Id, Portal.VenueCourts.Count + 1);
        }

        return Page();
    }

    private async Task<IActionResult> RenderSaveCourtErrorAsync(
        int actorUserId,
        CourtUpsertDto courtForm,
        bool isUpdate,
        bool removeCourtPhoto,
        string errorMessage)
    {
        TempData["ToastError"] = errorMessage;
        Portal = await _bookingService.GetOwnerPortalAsync(
            actorUserId,
            User.IsAdmin(),
            courtForm.VenueId > 0 ? courtForm.VenueId : null,
            SelectedDate,
            HttpContext.RequestAborted);
        ActiveTab = "courts";
        ShowNewVenueForm = false;
        EditCourtId = isUpdate ? courtForm.Id : null;
        RemoveCourtPhotoState = removeCourtPhoto && string.IsNullOrEmpty(courtForm.ImageUrl);
        CourtForm = courtForm;
        if (Portal.SelectedVenue is not null)
        {
            VenueForm = MapVenueForm(Portal.SelectedVenue);
        }

        return Page();
    }

    private static VenueUpsertDto CreateDefaultVenueForm() => new()
    {
        AccentColor = "emerald",
        OpenHour = 6,
        CloseHour = 22
    };

    private static VenueUpsertDto MapVenueForm(Venue venue) => new()
    {
        Id = venue.Id,
        Name = venue.Name,
        District = venue.District,
        Address = venue.Address,
        PhoneNumber = venue.PhoneNumber,
        Description = venue.Description,
        AccentColor = string.IsNullOrWhiteSpace(venue.AccentColor) ? "emerald" : venue.AccentColor,
        CoverImageUrl = venue.CoverImageUrl,
        GalleryImageUrls = string.Join(';', ParseGalleryUrls(venue.GalleryImageUrls)),
        OpenHour = venue.OpenHour,
        CloseHour = venue.CloseHour
    };

    private static CourtUpsertDto CreateDefaultCourtForm(int venueId, int displayOrder) => new()
    {
        VenueId = venueId,
        SportType = SportType.Pickleball,
        IsIndoor = true,
        BasePricePerHour = 130_000m,
        PeakPricePerHour = 180_000m,
        IsActive = true,
        DisplayOrder = Math.Max(1, displayOrder)
    };

    private static void NormalizeVenueForm(VenueUpsertDto dto)
    {
        dto.Name = (dto.Name ?? string.Empty).Trim();
        dto.District = (dto.District ?? string.Empty).Trim();
        dto.Address = (dto.Address ?? string.Empty).Trim();
        dto.PhoneNumber = (dto.PhoneNumber ?? string.Empty).Trim();
        dto.Description = (dto.Description ?? string.Empty).Trim();
        dto.AccentColor = string.IsNullOrWhiteSpace(dto.AccentColor) ? "emerald" : dto.AccentColor.Trim().ToLowerInvariant();
    }

    private static DateOnly ParseDateOrToday(string? date)
    {
        if (!string.IsNullOrWhiteSpace(date)
            && DateOnly.TryParseExact(date.Trim(), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out DateOnly parsed))
        {
            return parsed;
        }

        return GetVietnamToday();
    }

    private static DateOnly GetVietnamToday()
    {
        foreach (string timeZoneId in new[] { "Asia/Ho_Chi_Minh", "SE Asia Standard Time" })
        {
            try
            {
                TimeZoneInfo tz = TimeZoneInfo.FindSystemTimeZoneById(timeZoneId);
                return DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, tz));
            }
            catch (TimeZoneNotFoundException)
            {
            }
        }

        return DateOnly.FromDateTime(DateTime.UtcNow.AddHours(7));
    }
}
