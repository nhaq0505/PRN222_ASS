using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using SplitTrip.Services.Auth;
using SplitTrip.Services.Auth.Dtos;

namespace SplitTrip.Web.Pages.Account;

public sealed class RegisterModel(IAuthService authService) : PageModel
{
    [BindProperty]
    public InputModel Input { get; set; } = new();

    [BindProperty(SupportsGet = true)]
    public string? ReturnUrl { get; set; }

    public string? ErrorMessage { get; set; }

    public sealed class InputModel
    {
        [Required(ErrorMessage = "Vui lòng nhập họ và tên.")]
        [StringLength(150, MinimumLength = 2, ErrorMessage = "Họ và tên phải từ 2 đến 150 ký tự.")]
        public string FullName { get; set; } = string.Empty;

        [Required(ErrorMessage = "Vui lòng nhập địa chỉ email.")]
        [EmailAddress(ErrorMessage = "Địa chỉ email không hợp lệ.")]
        public string Email { get; set; } = string.Empty;

        [Required(ErrorMessage = "Vui lòng nhập mật khẩu.")]
        [MinLength(6, ErrorMessage = "Mật khẩu phải có ít nhất 6 ký tự.")]
        [DataType(DataType.Password)]
        public string Password { get; set; } = string.Empty;

        [Required(ErrorMessage = "Vui lòng xác nhận mật khẩu.")]
        [Compare(nameof(Password), ErrorMessage = "Mật khẩu xác nhận không khớp.")]
        [DataType(DataType.Password)]
        public string ConfirmPassword { get; set; } = string.Empty;
    }

    public IActionResult OnGet()
    {
        if (User.Identity?.IsAuthenticated == true)
        {
            return RedirectToPage("/Index");
        }

        return Page();
    }

    public async Task<IActionResult> OnPostAsync(CancellationToken ct)
    {
        if (!ModelState.IsValid)
        {
            return Page();
        }

        try
        {
            var registered = await authService.RegisterAsync(
                new RegisterUserRequest(Input.Email, Input.Password, Input.FullName),
                ct);

            var claims = new List<Claim>
            {
                new(ClaimTypes.NameIdentifier, registered.UserId.ToString("D")),
                new(ClaimTypes.Email, registered.Email),
                new(ClaimTypes.Name, registered.FullName),
                new(ClaimTypes.Role, registered.Role)
            };

            var principal = new ClaimsPrincipal(
                new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme));

            await HttpContext.SignInAsync(
                CookieAuthenticationDefaults.AuthenticationScheme,
                principal,
                new AuthenticationProperties { IsPersistent = true, AllowRefresh = true });

            TempData["SuccessMessage"] = "Đăng ký tài khoản thành công! Hãy cập nhật ngân hàng VietQR hoặc tạo chuyến đi đầu tiên.";

            if (!string.IsNullOrWhiteSpace(ReturnUrl) && Url.IsLocalUrl(ReturnUrl))
            {
                return LocalRedirect(ReturnUrl);
            }

            return RedirectToPage("/Index");
        }
        catch (Exception ex) when (ex is InvalidOperationException or ArgumentException)
        {
            ErrorMessage = ex.Message;
            return Page();
        }
    }
}
