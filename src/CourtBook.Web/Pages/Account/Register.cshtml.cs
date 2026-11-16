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

public class RegisterModel : PageModel
{
    private readonly IAuthService _authService;

    public RegisterModel(IAuthService authService)
    {
        _authService = authService;
    }

    [BindProperty]
    public InputModel Input { get; set; } = new();

    [BindProperty(SupportsGet = true)]
    public string? ReturnUrl { get; set; }

    public class InputModel
    {
        public bool RegisterAsOwner { get; set; }

        [Required(ErrorMessage = "Vui lòng nhập họ và tên.")]
        [StringLength(100, MinimumLength = 2, ErrorMessage = "Họ và tên phải từ 2 đến 100 ký tự.")]
        public string FullName { get; set; } = string.Empty;

        [Required(ErrorMessage = "Vui lòng nhập email.")]
        [EmailAddress(ErrorMessage = "Email không đúng định dạng.")]
        [StringLength(150)]
        public string Email { get; set; } = string.Empty;

        [Required(ErrorMessage = "Vui lòng nhập số điện thoại.")]
        [StringLength(20, MinimumLength = 8, ErrorMessage = "Số điện thoại phải từ 8 đến 20 ký tự.")]
        public string PhoneNumber { get; set; } = string.Empty;

        [Required(ErrorMessage = "Vui lòng nhập mật khẩu.")]
        [MinLength(6, ErrorMessage = "Mật khẩu tối thiểu 6 ký tự.")]
        [DataType(DataType.Password)]
        public string Password { get; set; } = string.Empty;

        [Required(ErrorMessage = "Vui lòng xác nhận mật khẩu.")]
        [Compare(nameof(Password), ErrorMessage = "Mật khẩu xác nhận không khớp.")]
        [DataType(DataType.Password)]
        public string ConfirmPassword { get; set; } = string.Empty;
    }

    public IActionResult OnGet(string? returnUrl = null, bool? owner = null)
    {
        if (User.Identity?.IsAuthenticated == true)
        {
            return RedirectAfterRegister(returnUrl ?? ReturnUrl, User.GetUserRole());
        }

        ReturnUrl = returnUrl ?? ReturnUrl;
        if (owner == true)
        {
            Input.RegisterAsOwner = true;
        }
        return Page();
    }

    public async Task<IActionResult> OnPostAsync(string? returnUrl = null)
    {
        string? targetUrl = returnUrl ?? ReturnUrl;

        if (!ModelState.IsValid)
        {
            return Page();
        }

        ServiceResult<AuthUserDto> result = await _authService.RegisterAsync(new RegisterRequestDto
        {
            FullName = Input.FullName,
            Email = Input.Email,
            PhoneNumber = Input.PhoneNumber,
            Password = Input.Password,
            RegisterAsOwner = Input.RegisterAsOwner
        });

        if (!result.Succeeded || result.Data is null)
        {
            ModelState.AddModelError(string.Empty, result.ErrorMessage ?? "Không thể tạo tài khoản.");
            return Page();
        }

        AuthUserDto user = result.Data;
        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, user.Id.ToString()),
            new(ClaimTypes.Name, user.FullName),
            new(ClaimTypes.Email, user.Email),
            new(ClaimTypes.Role, user.Role.ToString()),
            new("PhoneNumber", user.PhoneNumber)
        };

        var identity = new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme);
        await HttpContext.SignInAsync(
            CookieAuthenticationDefaults.AuthenticationScheme,
            new ClaimsPrincipal(identity),
            new AuthenticationProperties { IsPersistent = true, AllowRefresh = true });

        if (user.Role == UserRole.CourtOwner)
        {
            TempData["ToastSuccess"] = "Chào mừng đối tác! Hãy đăng ký cụm sân đầu tiên để bắt đầu nhận lịch.";
            return RedirectToPage("/Owner/Index", new { mode = "newvenue" });
        }

        TempData["ToastSuccess"] = "Đăng ký tài khoản thành công!";
        return RedirectAfterRegister(targetUrl, user.Role);
    }

    private IActionResult RedirectAfterRegister(string? url, UserRole? role)
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
