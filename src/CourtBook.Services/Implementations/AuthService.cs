using System.Net.Mail;
using CourtBook.BusinessObjects.Entities;
using CourtBook.BusinessObjects.Enums;
using CourtBook.BusinessObjects.Security;
using CourtBook.Repositories;
using CourtBook.Services.Interfaces;
using CourtBook.Services.Models;

namespace CourtBook.Services.Implementations;

public class AuthService : IAuthService
{
    private readonly IUserRepository _userRepository;

    public AuthService(IUserRepository userRepository)
    {
        _userRepository = userRepository;
    }

    public async Task<ServiceResult<AuthUserDto>> AuthenticateAsync(
        string email,
        string password,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(email) || string.IsNullOrEmpty(password))
        {
            return ServiceResult<AuthUserDto>.Fail("Vui lòng nhập đầy đủ email và mật khẩu.");
        }

        string normalizedEmail = email.Trim().ToLowerInvariant();
        AppUser? user = await _userRepository.GetByEmailAsync(normalizedEmail, ct);
        if (user is null || !PasswordHelper.VerifyPassword(password, user.PasswordHash))
        {
            return ServiceResult<AuthUserDto>.Fail("Email hoặc mật khẩu không chính xác.");
        }

        if (!user.IsActive)
        {
            return ServiceResult<AuthUserDto>.Fail("Tài khoản này đã bị tạm khóa.");
        }

        return ServiceResult<AuthUserDto>.Ok(MapToDto(user));
    }

    public async Task<ServiceResult<AuthUserDto>> RegisterAsync(
        RegisterRequestDto request,
        CancellationToken ct = default)
    {
        if (request is null)
        {
            return ServiceResult<AuthUserDto>.Fail("Dữ liệu đăng ký không hợp lệ.");
        }

        string fullName = request.FullName?.Trim() ?? string.Empty;
        string email = request.Email?.Trim().ToLowerInvariant() ?? string.Empty;
        string phone = request.PhoneNumber?.Trim() ?? string.Empty;
        string password = request.Password ?? string.Empty;

        if (string.IsNullOrWhiteSpace(fullName))
        {
            return ServiceResult<AuthUserDto>.Fail("Vui lòng nhập họ và tên.");
        }

        if (!IsValidEmail(email))
        {
            return ServiceResult<AuthUserDto>.Fail("Địa chỉ email không hợp lệ.");
        }

        if (string.IsNullOrWhiteSpace(phone) || phone.Length < 8)
        {
            return ServiceResult<AuthUserDto>.Fail("Số điện thoại phải có ít nhất 8 ký tự.");
        }

        if (password.Length < 6)
        {
            return ServiceResult<AuthUserDto>.Fail("Mật khẩu phải có ít nhất 6 ký tự.");
        }

        if (await _userRepository.EmailExistsAsync(email, ct))
        {
            return ServiceResult<AuthUserDto>.Fail("Email này đã được đăng ký.");
        }

        var user = new AppUser
        {
            FullName = fullName,
            Email = email,
            PhoneNumber = phone,
            PasswordHash = PasswordHelper.HashPassword(password),
            Role = request.RegisterAsOwner ? UserRole.CourtOwner : UserRole.Member,
            IsActive = true,
            CreatedAtUtc = DateTime.UtcNow
        };

        AppUser created = await _userRepository.AddAsync(user, ct);
        return ServiceResult<AuthUserDto>.Ok(MapToDto(created));
    }

    public async Task<AuthUserDto?> GetByIdAsync(int userId, CancellationToken ct = default)
    {
        AppUser? user = await _userRepository.GetByIdAsync(userId, ct);
        return user is null || !user.IsActive ? null : MapToDto(user);
    }

    public IReadOnlyList<DemoAccountDto> GetDemoAccounts() =>
    [
        new()
        {
            Label = "Nguyễn Văn An",
            RoleBadge = "Hội viên",
            Group = "Member",
            Email = "an@courtbook.vn",
            Password = "User@123"
        },
        new()
        {
            Label = "Trần Thị Bình",
            RoleBadge = "Hội viên",
            Group = "Member",
            Email = "binh@courtbook.vn",
            Password = "User@123"
        },
        new()
        {
            Label = "Chủ sân Lê Minh Khôi (2 cụm sân)",
            RoleBadge = "Chủ sân",
            Group = "Owner",
            Email = "khoi.owner@courtbook.vn",
            Password = "Owner@123"
        },
        new()
        {
            Label = "Chủ sân Phạm Thu Hà (Q7 & Tân Bình)",
            RoleBadge = "Chủ sân",
            Group = "Owner",
            Email = "ha.owner@courtbook.vn",
            Password = "Owner@123"
        },
        new()
        {
            Label = "Quản trị sàn CourtBook",
            RoleBadge = "Quản trị sàn",
            Group = "Admin",
            Email = "admin@courtbook.vn",
            Password = "Admin@123"
        }
    ];

    private static bool IsValidEmail(string email)
    {
        if (string.IsNullOrWhiteSpace(email))
        {
            return false;
        }

        return MailAddress.TryCreate(email, out MailAddress? parsed)
            && string.Equals(parsed.Address, email, StringComparison.OrdinalIgnoreCase)
            && email.Contains('.');
    }

    private static AuthUserDto MapToDto(AppUser user) => new()
    {
        Id = user.Id,
        FullName = user.FullName,
        Email = user.Email,
        PhoneNumber = user.PhoneNumber,
        Role = user.Role
    };
}
