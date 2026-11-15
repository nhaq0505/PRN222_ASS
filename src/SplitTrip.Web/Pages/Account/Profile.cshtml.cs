using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using SplitTrip.Services.Auth;
using SplitTrip.Services.Auth.Dtos;
using SplitTrip.Web.Infrastructure;

namespace SplitTrip.Web.Pages.Account;

[Authorize]
public sealed class ProfileModel(IAuthService authService) : PageModel
{
    [BindProperty]
    public InputModel Input { get; set; } = new();

    public string Email { get; private set; } = string.Empty;
    public IReadOnlyList<VietQrBankInfo> Banks => VietQrBankCatalog.SupportedBanks;
    public string? ErrorMessage { get; private set; }

    public sealed class InputModel
    {
        [Required(ErrorMessage = "Vui lòng nhập họ và tên.")]
        [StringLength(150, MinimumLength = 2, ErrorMessage = "Họ và tên phải từ 2 đến 150 ký tự.")]
        public string FullName { get; set; } = string.Empty;

        [Url(ErrorMessage = "Đường dẫn ảnh đại diện không hợp lệ.")]
        public string? AvatarUrl { get; set; }

        public string? SelectedBankBin { get; set; }

        [StringLength(50, ErrorMessage = "Số tài khoản tối đa 50 ký tự.")]
        public string? BankAccountNumber { get; set; }

        [StringLength(150, ErrorMessage = "Tên chủ tài khoản tối đa 150 ký tự.")]
        public string? BankAccountHolder { get; set; }
    }

    public async Task<IActionResult> OnGetAsync(CancellationToken ct)
    {
        if (User.IsSystemAdmin())
        {
            TempData["ErrorMessage"] = "Tài khoản Quản trị viên hệ thống được cấu hình qua appsettings.json.";
            return RedirectToPage("/Index");
        }

        var userId = User.GetRequiredUserId();
        var profile = await authService.GetProfileAsync(userId, ct);
        if (profile is null)
        {
            return RedirectToPage("/Account/Logout");
        }

        PopulateFromProfile(profile);
        return Page();
    }

    public async Task<IActionResult> OnPostAsync(CancellationToken ct)
    {
        if (User.IsSystemAdmin())
        {
            return RedirectToPage("/Index");
        }

        var userId = User.GetRequiredUserId();
        var existing = await authService.GetProfileAsync(userId, ct);
        if (existing is null)
        {
            return RedirectToPage("/Account/Logout");
        }

        Email = existing.Email;

        if (!ModelState.IsValid)
        {
            return Page();
        }

        try
        {
            var matchedBank = VietQrBankCatalog.FindByBinOrCode(Input.SelectedBankBin);

            var updated = await authService.UpdateBankProfileAsync(
                userId,
                new UpdateBankProfileRequest(
                    Input.FullName,
                    Input.AvatarUrl,
                    matchedBank?.BankCode,
                    matchedBank?.BankBin ?? Input.SelectedBankBin,
                    Input.BankAccountNumber,
                    Input.BankAccountHolder),
                ct);

            await RefreshCookieNameClaimAsync(updated);
            PopulateFromProfile(updated);
            TempData["SuccessMessage"] = "Đã lưu thông tin hồ sơ và tài khoản ngân hàng VietQR!";
            return RedirectToPage();
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or KeyNotFoundException)
        {
            ErrorMessage = ex.Message;
            return Page();
        }
    }

    private void PopulateFromProfile(UserProfileDto profile)
    {
        Email = profile.Email;
        Input = new InputModel
        {
            FullName = profile.FullName,
            AvatarUrl = profile.AvatarUrl,
            SelectedBankBin = profile.BankBin ?? VietQrBankCatalog.FindByBinOrCode(profile.BankCode)?.BankBin,
            BankAccountNumber = profile.BankAccountNumber,
            BankAccountHolder = profile.BankAccountHolder
        };
    }

    private async Task RefreshCookieNameClaimAsync(UserProfileDto updated)
    {
        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, updated.UserId.ToString("D")),
            new(ClaimTypes.Email, updated.Email),
            new(ClaimTypes.Name, updated.FullName),
            new(ClaimTypes.Role, AuthService.UserRole)
        };

        var principal = new ClaimsPrincipal(
            new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme));

        await HttpContext.SignInAsync(
            CookieAuthenticationDefaults.AuthenticationScheme,
            principal,
            new AuthenticationProperties { IsPersistent = true, AllowRefresh = true });
    }
}
