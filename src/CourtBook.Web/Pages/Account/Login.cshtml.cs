using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using CourtBook.BusinessObjects.Enums;
using CourtBook.Services.Interfaces;
using CourtBook.Services.Models;
using CourtBook.Web.Extensions;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace CourtBook.Web.Pages.Account;

public class LoginModel : PageModel
{
    private readonly IAuthService _authService;

    public LoginModel(IAuthService authService)
    {
        _authService = authService;
    }

    [BindProperty]
    public InputModel Input { get; set; } = new();

    [BindProperty(SupportsGet = true)]
    public string? ReturnUrl { get; set; }

    public IReadOnlyList<DemoAccountDto> DemoAccounts { get; private set; } = Array.Empty<DemoAccountDto>();

    public class InputModel
    {
        [Required(ErrorMessage = "Vui lòng nhập email.")]
        [EmailAddress(ErrorMessage = "Email không đúng định dạng.")]
        public string Email { get; set; } = string.Empty;

        [Required(ErrorMessage = "Vui lòng nhập mật khẩu.")]
        [DataType(DataType.Password)]
        public string Password { get; set; } = string.Empty;
    }

    public IActionResult OnGet(string? returnUrl = null)
    {
        if (User.Identity?.IsAuthenticated == true)
        {
            return RedirectAfterLogin(returnUrl ?? ReturnUrl, User.GetUserRole());
        }

        ReturnUrl = returnUrl ?? ReturnUrl;
        DemoAccounts = _authService.GetDemoAccounts();
        return Page();
    }

    public async Task<IActionResult> OnPostAsync(string? returnUrl = null)
    {
        DemoAccounts = _authService.GetDemoAccounts();
        string? targetUrl = returnUrl ?? ReturnUrl;

        if (!ModelState.IsValid)
        {
            return Page();
        }

        ServiceResult<AuthUserDto> result = await _authService.AuthenticateAsync(Input.Email, Input.Password);
        if (!result.Succeeded || result.Data is null)
        {
            ModelState.AddModelError(string.Empty, result.ErrorMessage ?? "Email hoặc mật khẩu không đúng.");
            return Page();
        }

        await SignInUserAsync(result.Data);
        TempData["ToastSuccess"] = $"Xin chào, {result.Data.FullName}!";
        return RedirectAfterLogin(targetUrl, result.Data.Role);
    }

    public async Task<IActionResult> OnPostDemoLoginAsync(string email, string? returnUrl = null)
    {
        DemoAccounts = _authService.GetDemoAccounts();
        string? targetUrl = returnUrl ?? ReturnUrl;

        DemoAccountDto? demo = DemoAccounts.FirstOrDefault(
            a => string.Equals(a.Email, email?.Trim(), StringComparison.OrdinalIgnoreCase));

        if (demo is null)
        {
            ModelState.AddModelError(string.Empty, "Tài khoản demo không hợp lệ.");
            return Page();
        }

        ServiceResult<AuthUserDto> result = await _authService.AuthenticateAsync(demo.Email, demo.Password);
        if (!result.Succeeded || result.Data is null)
        {
            ModelState.AddModelError(string.Empty, result.ErrorMessage ?? "Không thể đăng nhập tài khoản demo.");
            return Page();
        }

        await SignInUserAsync(result.Data);
        TempData["ToastSuccess"] = $"Đã đăng nhập nhanh: {result.Data.FullName}";
        return RedirectAfterLogin(targetUrl, result.Data.Role);
    }

    private async Task SignInUserAsync(AuthUserDto user)
    {
        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, user.Id.ToString()),
            new(ClaimTypes.Name, user.FullName),
            new(ClaimTypes.Email, user.Email),
            new(ClaimTypes.Role, user.Role.ToString()),
            new("PhoneNumber", user.PhoneNumber)
        };

        var identity = new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme);
        var principal = new ClaimsPrincipal(identity);
        var properties = new AuthenticationProperties
        {
            IsPersistent = true,
            AllowRefresh = true
        };

        await HttpContext.SignInAsync(
            CookieAuthenticationDefaults.AuthenticationScheme,
            principal,
            properties);
    }

    private IActionResult RedirectAfterLogin(string? url, UserRole? role)
    {
        if (!string.IsNullOrWhiteSpace(url))
        {
            string trimmed = url.Trim();
            bool isRoot = trimmed == "/"
                || trimmed == "~/"
                || trimmed.Equals("/Index", StringComparison.OrdinalIgnoreCase);

            if (!isRoot && Url.IsLocalUrl(trimmed))
            {
                return LocalRedirect(trimmed);
            }
        }

        return role switch
        {
            UserRole.Admin => RedirectToPage("/Admin/Index"),
            UserRole.CourtOwner => RedirectToPage("/Owner/Index"),
            _ => RedirectToPage("/Index")
        };
    }
}
