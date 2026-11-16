using CourtBook.BusinessObjects.Entities;
using CourtBook.DataAccessObjects;
using CourtBook.Web.Extensions;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;

namespace CourtBook.Web.Pages.Profile;

public class NotificationsModel : PageModel
{
    private readonly CourtBookDbContext _db;

    public NotificationsModel(CourtBookDbContext db)
    {
        _db = db;
    }

    public List<UserNotification> Notifications { get; private set; } = new();

    public async Task<IActionResult> OnGetAsync()
    {
        int? userId = User.GetUserId();
        if (userId is null)
        {
            return RedirectToPage("/Account/Login", new { ReturnUrl = "/Profile/Notifications" });
        }

        Notifications = await _db.UserNotifications
            .Where(n => n.UserId == userId.Value)
            .OrderByDescending(n => n.CreatedAtUtc)
            .ToListAsync();

        return Page();
    }

    public async Task<IActionResult> OnPostMarkReadAsync(int notificationId)
    {
        int? userId = User.GetUserId();
        if (userId is null)
        {
            return RedirectToPage("/Account/Login", new { ReturnUrl = "/Profile/Notifications" });
        }

        UserNotification? notification = await _db.UserNotifications
            .FirstOrDefaultAsync(n => n.Id == notificationId && n.UserId == userId.Value);

        if (notification is not null)
        {
            notification.IsRead = true;
            await _db.SaveChangesAsync();
        }

        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostMarkAllReadAsync()
    {
        int? userId = User.GetUserId();
        if (userId is null)
        {
            return RedirectToPage("/Account/Login", new { ReturnUrl = "/Profile/Notifications" });
        }

        List<UserNotification> notifications = await _db.UserNotifications
            .Where(n => n.UserId == userId.Value && !n.IsRead)
            .ToListAsync();

        foreach (UserNotification item in notifications)
        {
            item.IsRead = true;
        }

        await _db.SaveChangesAsync();
        TempData["ToastSuccess"] = "Tất cả thông báo đã được đánh dấu đã đọc.";
        return RedirectToPage();
    }
}
