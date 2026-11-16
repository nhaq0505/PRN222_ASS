using System.Globalization;
using System.Security.Claims;
using CourtBook.BusinessObjects.Enums;
using CourtBook.Services.Interfaces;
using CourtBook.Services.Models;
using CourtBook.Web.Extensions;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace CourtBook.Web.Pages.Venues;

[EnsureAuthenticatedBeforeAntiforgeryOnBookingPost]
public class DetailsModel : PageModel
{
    private readonly IBookingService _bookingService;

    public DetailsModel(IBookingService bookingService)
    {
        _bookingService = bookingService;
    }

    public ScheduleMatrixDto Matrix { get; set; } = new();
    public IReadOnlyList<DayTileViewModel> Days { get; set; } = Array.Empty<DayTileViewModel>();
    public DateOnly SelectedDate { get; set; }
    public SportType? SportFilter { get; set; }
    public int? CurrentUserId { get; set; }

    public async Task<IActionResult> OnGetAsync(int id, string? date, SportType? sport)
    {
        DateOnly vnToday = GetVietnamToday();
        DateOnly minDate = vnToday;
        DateOnly maxDate = vnToday.AddDays(30);

        DateOnly selected = vnToday;
        if (!string.IsNullOrWhiteSpace(date)
            && DateOnly.TryParseExact(date.Trim(), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out DateOnly parsed))
        {
            if (parsed < minDate)
            {
                selected = minDate;
            }
            else if (parsed > maxDate)
            {
                selected = maxDate;
            }
            else
            {
                selected = parsed;
            }
        }

        SelectedDate = selected;
        SportFilter = sport;
        CurrentUserId = User.GetUserId();

        ScheduleMatrixDto? matrix = await _bookingService.GetScheduleMatrixAsync(
            id,
            SelectedDate,
            SportFilter,
            CurrentUserId,
            HttpContext.RequestAborted);

        if (matrix is null)
        {
            TempData["ToastError"] = "Cụm sân không tồn tại hoặc hiện đang tạm ngưng.";
            return RedirectToPage("/Index");
        }

        Matrix = matrix;

        var dayTiles = new List<DayTileViewModel>(7);
        for (int i = 0; i < 7; i++)
        {
            DateOnly day = vnToday.AddDays(i);
            dayTiles.Add(new DayTileViewModel
            {
                Date = day,
                UrlDate = day.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                DayOfWeekLabel = i == 0 ? "Hôm nay" : FormatVietnameseDayOfWeek(day.DayOfWeek),
                DayMonthLabel = day.ToString("dd/MM", CultureInfo.InvariantCulture),
                IsSelected = day == SelectedDate
            });
        }
        Days = dayTiles;

        return Page();
    }

    public async Task<IActionResult> OnPostToggleHoldAsync(int id, [FromBody] ToggleHoldRequest? req)
    {
        int? userId = User.GetUserId();
        if (User.Identity?.IsAuthenticated != true || userId is null)
        {
            return new JsonResult(new
            {
                ok = false,
                requiresLogin = true,
                loginUrl = $"/Account/Login?ReturnUrl=/Venues/{id}"
            })
            {
                StatusCode = StatusCodes.Status401Unauthorized
            };
        }

        if (req is null
            || req.CourtId <= 0
            || !DateOnly.TryParseExact(req.PlayDate, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out DateOnly playDate))
        {
            return new JsonResult(new
            {
                ok = false,
                error = "Dữ liệu khung giờ không hợp lệ."
            });
        }

        ServiceResult<ToggleHoldResponseDto> result = await _bookingService.ToggleHoldSlotAsync(
            userId.Value,
            req.CourtId,
            playDate,
            req.StartHour,
            HttpContext.RequestAborted);

        return new JsonResult(new
        {
            ok = result.Succeeded,
            error = result.ErrorMessage,
            data = result.Data
        });
    }

    public async Task<IActionResult> OnGetMyHoldsAsync(int id)
    {
        int? userId = User.GetUserId();
        if (User.Identity?.IsAuthenticated != true || userId is null)
        {
            return new JsonResult(new HeldSummaryDto());
        }

        HeldSummaryDto summary = await _bookingService.GetMyHeldSummaryAsync(
            userId.Value,
            id,
            HttpContext.RequestAborted);

        return new JsonResult(summary);
    }

    public async Task<IActionResult> OnPostConfirmAsync(int id, string? notes, string? date, SportType? sport)
    {
        int? userId = User.GetUserId();
        if (User.Identity?.IsAuthenticated != true || userId is null)
        {
            string? returnUrl = Url.Page("/Venues/Details", new { id, date, sport });
            return RedirectToPage("/Account/Login", new { ReturnUrl = returnUrl });
        }

        string? normalizedNotes = string.IsNullOrWhiteSpace(notes) ? null : notes.Trim();
        if (normalizedNotes is { Length: > 120 })
        {
            normalizedNotes = normalizedNotes[..120];
        }

        ServiceResult<IReadOnlyList<BookingTicketDto>> result = await _bookingService.ConfirmMyHoldsAsync(
            userId.Value,
            id,
            normalizedNotes,
            HttpContext.RequestAborted);

        if (result.Succeeded)
        {
            int count = result.Data?.Count ?? 0;
            TempData["ToastSuccess"] = $"Đặt sân thành công ({count} khung giờ)!";
            return RedirectToPage("/MyBookings/Index");
        }

        TempData["ToastError"] = result.ErrorMessage ?? "Không thể xác nhận đặt sân. Vui lòng thử lại.";
        return RedirectToPage("/Venues/Details", new { id, date, sport });
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

    private static string FormatVietnameseDayOfWeek(DayOfWeek dow) => dow switch
    {
        DayOfWeek.Monday => "T2",
        DayOfWeek.Tuesday => "T3",
        DayOfWeek.Wednesday => "T4",
        DayOfWeek.Thursday => "T5",
        DayOfWeek.Friday => "T6",
        DayOfWeek.Saturday => "T7",
        DayOfWeek.Sunday => "CN",
        _ => ""
    };

    public class ToggleHoldRequest
    {
        public int CourtId { get; set; }
        public string PlayDate { get; set; } = string.Empty;
        public int StartHour { get; set; }
    }

    public class DayTileViewModel
    {
        public DateOnly Date { get; set; }
        public string UrlDate { get; set; } = string.Empty;
        public string DayOfWeekLabel { get; set; } = string.Empty;
        public string DayMonthLabel { get; set; } = string.Empty;
        public bool IsSelected { get; set; }
    }
}

/// <summary>
/// Returns 401/Login redirect before Razor Pages antiforgery validation when a user's cookie was rejected or missing on mutation POST handlers.
/// </summary>
[AttributeUsage(AttributeTargets.Class)]
file sealed class EnsureAuthenticatedBeforeAntiforgeryOnBookingPostAttribute : Attribute, IAsyncAuthorizationFilter, IOrderedFilter
{
    public int Order => 500;

    public Task OnAuthorizationAsync(AuthorizationFilterContext context)
    {
        HttpRequest request = context.HttpContext.Request;
        if (!HttpMethods.IsPost(request.Method))
        {
            return Task.CompletedTask;
        }

        string? handler = context.RouteData.Values["handler"] as string;
        if (string.IsNullOrWhiteSpace(handler))
        {
            handler = request.Query["handler"].ToString();
        }

        bool isToggleHold = string.Equals(handler, "ToggleHold", StringComparison.OrdinalIgnoreCase);
        bool isConfirm = string.Equals(handler, "Confirm", StringComparison.OrdinalIgnoreCase);
        if (!isToggleHold && !isConfirm)
        {
            return Task.CompletedTask;
        }

        ClaimsPrincipal user = context.HttpContext.User;
        if (user.Identity?.IsAuthenticated == true && user.GetUserId() is not null)
        {
            return Task.CompletedTask;
        }

        string? idValue = context.RouteData.Values["id"]?.ToString();
        string venuePath = int.TryParse(idValue, out int venueId) ? $"/Venues/{venueId}" : "/Venues";

        if (isToggleHold)
        {
            context.Result = new JsonResult(new
            {
                ok = false,
                requiresLogin = true,
                loginUrl = $"/Account/Login?ReturnUrl={venuePath}"
            })
            {
                StatusCode = StatusCodes.Status401Unauthorized
            };
        }
        else
        {
            context.Result = new RedirectToPageResult("/Account/Login", new { ReturnUrl = venuePath });
        }

        return Task.CompletedTask;
    }
}
