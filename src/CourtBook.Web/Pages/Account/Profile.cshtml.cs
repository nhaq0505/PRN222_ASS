using CourtBook.Services.Interfaces;
using CourtBook.Services.Models;
using CourtBook.Web.Extensions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace CourtBook.Web.Pages.Account;

[Authorize]
public class ProfileModel : PageModel
{
    private readonly IAuthService _authService;

    public ProfileModel(IAuthService authService)
    {
        _authService = authService;
    }

    public UserProfileDto Profile { get; set; } = new();

    [BindProperty]
    public UpdateProfileRequestDto UpdateForm { get; set; } = new();

    [BindProperty]
    public ChangePasswordRequestDto PasswordForm { get; set; } = new();

    public string ActiveTab { get; set; } = "info";

    public async Task<IActionResult> OnGetAsync(string tab = "info")
    {
        int? userId = User.GetUserId();
        if (userId is null)
        {
            return Challenge();
        }

        ActiveTab = tab;
        ServiceResult<UserProfileDto> result = await _authService.GetProfileAsync(userId.Value, HttpContext.RequestAborted);
        if (!result.Succeeded || result.Data is null)
        {
            TempData["ToastError"] = "Không thể tải thông tin tài khoản.";
            return RedirectToPage("/Index");
        }

        Profile = result.Data;
        UpdateForm.FullName = Profile.FullName;
        UpdateForm.PhoneNumber = Profile.PhoneNumber;

        return Page();
    }

    public async Task<IActionResult> OnPostUpdateInfoAsync()
    {
        int? userId = User.GetUserId();
        if (userId is null)
        {
            return Challenge();
        }

        ActiveTab = "info";
        ServiceResult<bool> result = await _authService.UpdateProfileAsync(userId.Value, UpdateForm, HttpContext.RequestAborted);
        if (result.Succeeded)
        {
            TempData["ToastSuccess"] = "Cập nhật thông tin cá nhân thành công!";
        }
        else
        {
            TempData["ToastError"] = result.ErrorMessage ?? "Không thể cập nhật thông tin.";
        }

        return RedirectToPage("/Account/Profile", new { tab = "info" });
    }

    public async Task<IActionResult> OnPostChangePasswordAsync()
    {
        int? userId = User.GetUserId();
        if (userId is null)
        {
            return Challenge();
        }

        ActiveTab = "password";
        ServiceResult<bool> result = await _authService.ChangePasswordAsync(userId.Value, PasswordForm, HttpContext.RequestAborted);
        if (result.Succeeded)
        {
            TempData["ToastSuccess"] = "Đổi mật khẩu thành công! Hãy ghi nhớ mật khẩu mới.";
            return RedirectToPage("/Account/Profile", new { tab = "password" });
        }

        TempData["ToastError"] = result.ErrorMessage ?? "Đổi mật khẩu thất bại.";
        return RedirectToPage("/Account/Profile", new { tab = "password" });
    }
}
