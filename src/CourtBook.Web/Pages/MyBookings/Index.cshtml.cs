using CourtBook.BusinessObjects.Enums;
using CourtBook.Services.Interfaces;
using CourtBook.Services.Models;
using CourtBook.Web.Extensions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace CourtBook.Web.Pages.MyBookings;

[Authorize(Roles = "Member,CourtOwner")]
public class IndexModel : PageModel
{
    private readonly IBookingService _bookingService;

    public IndexModel(IBookingService bookingService)
    {
        _bookingService = bookingService;
    }

    public UserBookingsPageDto Bookings { get; set; } = new();
    public string ActiveTab { get; set; } = "upcoming";

    public async Task<IActionResult> OnGetAsync(string tab = "upcoming")
    {
        int? userId = User.GetUserId();
        if (userId is null)
        {
            return Challenge();
        }

        ActiveTab = string.Equals(tab, "history", StringComparison.OrdinalIgnoreCase)
            ? "history"
            : "upcoming";

        Bookings = await _bookingService.GetUserBookingsAsync(
            userId.Value,
            HttpContext.RequestAborted);

        return Page();
    }

    public async Task<IActionResult> OnPostCancelAsync(int bookingId)
    {
        int? userId = User.GetUserId();
        if (userId is null)
        {
            return Challenge();
        }

        UserRole role = User.GetUserRole() ?? UserRole.Member;

        ServiceResult<BookingTicketDto> result = await _bookingService.CancelBookingAsync(
            bookingId,
            userId.Value,
            role,
            HttpContext.RequestAborted);

        if (result.Succeeded)
        {
            string code = result.Data?.BookingCode ?? "";
            TempData["ToastSuccess"] = string.IsNullOrEmpty(code)
                ? "Đã hủy lịch đặt sân thành công."
                : $"Đã hủy lịch đặt sân {code} thành công.";
        }
        else
        {
            TempData["ToastError"] = result.ErrorMessage ?? "Không thể hủy lịch đặt sân.";
        }

        return RedirectToPage("/MyBookings/Index");
    }
}
