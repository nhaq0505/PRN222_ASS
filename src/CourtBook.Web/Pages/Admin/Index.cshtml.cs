using System.Globalization;
using CourtBook.BusinessObjects.Entities;
using CourtBook.BusinessObjects.Enums;
using CourtBook.Services.Interfaces;
using CourtBook.Services.Models;
using CourtBook.Web.Extensions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace CourtBook.Web.Pages.Admin;

[Authorize(Roles = "Admin")]
public class IndexModel : PageModel
{
    private readonly IPlatformAdminService _adminService;

    public IndexModel(IPlatformAdminService adminService)
    {
        _adminService = adminService;
    }

    public PlatformAdminDashboardDto Dashboard { get; set; } = new();
    public DateOnly SelectedDate { get; set; }
    public string ActiveTab { get; set; } = "venues";
    public VenueStatus? StatusFilter { get; set; }

    public async Task<IActionResult> OnGetAsync(string? date, string tab = "venues", VenueStatus? status = null)
    {
        SelectedDate = ParseDateOrToday(date);
        ActiveTab = string.Equals(tab, "users", StringComparison.OrdinalIgnoreCase)
            ? "users"
            : "venues";
        StatusFilter = status;

        Dashboard = await _adminService.GetDashboardAsync(
            SelectedDate,
            StatusFilter,
            HttpContext.RequestAborted);

        return Page();
    }

    public async Task<IActionResult> OnPostApproveVenueAsync(int venueId, string? date, VenueStatus? status)
    {
        ServiceResult<Venue> result = await _adminService.ApproveVenueAsync(
            venueId,
            HttpContext.RequestAborted);

        if (result.Succeeded)
        {
            TempData["ToastSuccess"] = "Đã phê duyệt cụm sân lên sàn!";
        }
        else
        {
            TempData["ToastError"] = result.ErrorMessage ?? "Không thể phê duyệt cụm sân.";
        }

        return RedirectToPage("/Admin/Index", new
        {
            date = ParseDateOrToday(date).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            tab = "venues",
            status
        });
    }

    public async Task<IActionResult> OnPostSuspendVenueAsync(int venueId, string? date, VenueStatus? status)
    {
        ServiceResult<Venue> result = await _adminService.SuspendVenueAsync(
            venueId,
            HttpContext.RequestAborted);

        if (result.Succeeded && result.Data is not null)
        {
            TempData["ToastSuccess"] = $"Đã tạm khóa cụm sân {result.Data.Name}.";
        }
        else
        {
            TempData["ToastError"] = result.ErrorMessage ?? "Không thể tạm khóa cụm sân.";
        }

        return RedirectToPage("/Admin/Index", new
        {
            date = ParseDateOrToday(date).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            tab = "venues",
            status
        });
    }

    public async Task<IActionResult> OnPostReactivateVenueAsync(int venueId, string? date, VenueStatus? status)
    {
        ServiceResult<Venue> result = await _adminService.ReactivateVenueAsync(
            venueId,
            HttpContext.RequestAborted);

        if (result.Succeeded && result.Data is not null)
        {
            TempData["ToastSuccess"] = $"Đã mở lại cụm sân {result.Data.Name} lên sàn.";
        }
        else
        {
            TempData["ToastError"] = result.ErrorMessage ?? "Không thể mở lại cụm sân.";
        }

        return RedirectToPage("/Admin/Index", new
        {
            date = ParseDateOrToday(date).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            tab = "venues",
            status
        });
    }

    public async Task<IActionResult> OnPostToggleUserAsync(int targetUserId, string? date)
    {
        int? adminUserId = User.GetUserId();
        if (adminUserId is null)
        {
            return Challenge();
        }

        ServiceResult<AppUser> result = await _adminService.ToggleUserActiveAsync(
            targetUserId,
            adminUserId.Value,
            HttpContext.RequestAborted);

        if (result.Succeeded && result.Data is not null)
        {
            string statusLabel = result.Data.IsActive ? "đã được mở khóa" : "đã bị khóa";
            TempData["ToastSuccess"] = $"Tài khoản {result.Data.FullName} {statusLabel}.";
        }
        else
        {
            TempData["ToastError"] = result.ErrorMessage ?? "Không thể cập nhật trạng thái người dùng.";
        }

        return RedirectToPage("/Admin/Index", new
        {
            date = ParseDateOrToday(date).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            tab = "users"
        });
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
