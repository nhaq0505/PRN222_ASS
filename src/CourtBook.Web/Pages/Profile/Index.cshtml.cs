using CourtBook.BusinessObjects.Entities;
using CourtBook.DataAccessObjects;
using CourtBook.Web.Extensions;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;

namespace CourtBook.Web.Pages.Profile;

public class IndexModel : PageModel
{
    public const decimal MinimumDepositAmountVnd = 50_000m;
    public const decimal MaximumDepositAmountVnd = 100_000_000m;
    public const decimal DepositIncrementAmountVnd = 50_000m;

    private readonly CourtBookDbContext _db;

    public IndexModel(CourtBookDbContext db)
    {
        _db = db;
    }

    public AppUser? Profile { get; private set; }
    public List<WalletTransaction> RecentTransactions { get; private set; } = new();
    public List<FavoriteVenue> FavoriteVenues { get; private set; } = new();
    public List<UserNotification> UnreadNotifications { get; private set; } = new();

    public async Task<IActionResult> OnGetAsync()
    {
        int? userId = User.GetUserId();
        if (userId is null)
        {
            return RedirectToPage("/Account/Login", new { ReturnUrl = "/Profile/Index" });
        }

        Profile = await _db.Users
            .FirstOrDefaultAsync(u => u.Id == userId.Value);

        if (Profile is null)
        {
            return RedirectToPage("/Account/Login", new { ReturnUrl = "/Profile/Index" });
        }

        RecentTransactions = await _db.WalletTransactions
            .Where(t => t.UserId == userId.Value)
            .OrderByDescending(t => t.CreatedAtUtc)
            .Take(6)
            .ToListAsync();

        FavoriteVenues = await _db.FavoriteVenues
            .Where(f => f.UserId == userId.Value)
            .Include(f => f.Venue)
            .OrderByDescending(f => f.CreatedAtUtc)
            .Take(3)
            .ToListAsync();

        UnreadNotifications = await _db.UserNotifications
            .Where(n => n.UserId == userId.Value && !n.IsRead)
            .OrderByDescending(n => n.CreatedAtUtc)
            .Take(5)
            .ToListAsync();

        return Page();
    }

    public async Task<IActionResult> OnPostDepositAsync(decimal amount)
    {
        int? userId = User.GetUserId();
        if (userId is null)
        {
            return RedirectToPage("/Account/Login", new { ReturnUrl = "/Profile/Index" });
        }

        if (amount < MinimumDepositAmountVnd
            || amount > MaximumDepositAmountVnd
            || amount % DepositIncrementAmountVnd != 0)
        {
            TempData["ToastError"] = "Số tiền nạp phải là bội số 50.000đ trong khoảng 50.000đ đến 100.000.000đ.";
            return RedirectToPage();
        }

        AppUser? profile = await _db.Users.FirstOrDefaultAsync(u => u.Id == userId.Value);
        if (profile is null)
        {
            TempData["ToastError"] = "Tài khoản không tồn tại.";
            return RedirectToPage();
        }

        profile.WalletBalanceVnd += amount;
        _db.WalletTransactions.Add(new WalletTransaction
        {
            UserId = profile.Id,
            AmountVnd = amount,
            Type = "Deposit",
            Description = "Nạp tiền vào ví",
            ReferenceCode = $"DEP-{profile.Id}-{DateTime.UtcNow:yyyyMMddHHmmss}",
            CreatedAtUtc = DateTime.UtcNow
        });

        await _db.SaveChangesAsync();
        TempData["ToastSuccess"] = $"Nạp tiền thành công {amount:N0}đ vào ví của bạn.";
        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostUpdateProfileAsync(
        string? fullName,
        string? phoneNumber,
        string? bio,
        string? preferredDistrict)
    {
        int? userId = User.GetUserId();
        if (userId is null)
        {
            return RedirectToPage("/Account/Login", new { ReturnUrl = "/Profile/Index" });
        }

        AppUser? profile = await _db.Users.FirstOrDefaultAsync(u => u.Id == userId.Value);
        if (profile is null)
        {
            TempData["ToastError"] = "Không tìm thấy hồ sơ người dùng.";
            return RedirectToPage();
        }

        string trimmedName = (fullName ?? string.Empty).Trim();
        string trimmedPhone = (phoneNumber ?? string.Empty).Trim();
        string trimmedBio = (bio ?? string.Empty).Trim();
        string trimmedDistrict = (preferredDistrict ?? string.Empty).Trim();

        if (trimmedName.Length is < 2 or > 100)
        {
            TempData["ToastError"] = "Họ và tên phải từ 2 đến 100 ký tự.";
            return RedirectToPage();
        }

        if (!string.IsNullOrEmpty(trimmedPhone) && trimmedPhone.Length is < 8 or > 20)
        {
            TempData["ToastError"] = "Số điện thoại phải từ 8 đến 20 ký tự.";
            return RedirectToPage();
        }

        if (trimmedBio.Length > 400)
        {
            TempData["ToastError"] = "Tiểu sử không được vượt quá 400 ký tự.";
            return RedirectToPage();
        }

        if (trimmedDistrict.Length > 100)
        {
            TempData["ToastError"] = "Khu vực ưu tiên không được vượt quá 100 ký tự.";
            return RedirectToPage();
        }

        profile.FullName = trimmedName;
        profile.PhoneNumber = trimmedPhone;
        profile.Bio = trimmedBio;
        profile.PreferredDistrict = trimmedDistrict;

        await _db.SaveChangesAsync();
        TempData["ToastSuccess"] = "Thông tin hồ sơ đã được cập nhật.";
        return RedirectToPage();
    }
}
