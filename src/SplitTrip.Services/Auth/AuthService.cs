using System.Net.Mail;
using Microsoft.Extensions.Options;
using SplitTrip.BusinessObjects.Entities;
using SplitTrip.BusinessObjects.Options;
using SplitTrip.Repositories.Users;
using SplitTrip.Services.Auth.Dtos;

namespace SplitTrip.Services.Auth;

public sealed class AuthService(
    IUserRepository userRepository,
    IOptions<AdminAccountOptions> adminOptions) : IAuthService
{
    public static readonly Guid SystemAdminUserId = Guid.Parse("00000000-0000-0000-0000-000000000001");
    public const string AdminRole = "Admin";
    public const string UserRole = "User";

    public async Task<AuthenticatedUserDto> RegisterAsync(RegisterUserRequest request, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var normalizedEmail = NormalizeAndValidateEmail(request.Email);
        var fullName = (request.FullName ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(fullName) || fullName.Length > 150)
        {
            throw new ArgumentException("Họ và tên phải từ 1 đến 150 ký tự.", nameof(request));
        }

        if (string.IsNullOrWhiteSpace(request.Password) || request.Password.Length < 6)
        {
            throw new ArgumentException("Mật khẩu phải có ít nhất 6 ký tự.", nameof(request));
        }

        var admin = adminOptions.Value;
        if (!string.IsNullOrWhiteSpace(admin.Email) &&
            string.Equals(normalizedEmail, admin.Email.Trim(), StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Email này đã được sử dụng cho tài khoản quản trị hệ thống.");
        }

        if (await userRepository.EmailExistsAsync(normalizedEmail, ct))
        {
            throw new InvalidOperationException("Email này đã được đăng ký.");
        }

        var user = new User
        {
            Id = Guid.NewGuid(),
            Email = normalizedEmail,
            PasswordHash = BCrypt.Net.BCrypt.HashPassword(request.Password),
            FullName = fullName,
            CreatedAtUtc = DateTime.UtcNow
        };

        await userRepository.AddAsync(user, ct);

        return new AuthenticatedUserDto(
            user.Id,
            user.Email,
            user.FullName,
            UserRole,
            IsSystemAdmin: false,
            user.AvatarUrl);
    }

    public async Task<AuthenticatedUserDto?> LoginAsync(string email, string password, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(email) || string.IsNullOrEmpty(password))
        {
            return null;
        }

        var normalizedEmail = email.Trim().ToLowerInvariant();
        var admin = adminOptions.Value;

        if (!string.IsNullOrWhiteSpace(admin.Email) &&
            !string.IsNullOrEmpty(admin.Password) &&
            string.Equals(normalizedEmail, admin.Email.Trim(), StringComparison.OrdinalIgnoreCase))
        {
            if (password == admin.Password)
            {
                var adminName = string.IsNullOrWhiteSpace(admin.FullName)
                    ? "System Administrator"
                    : admin.FullName.Trim();

                return new AuthenticatedUserDto(
                    SystemAdminUserId,
                    admin.Email.Trim().ToLowerInvariant(),
                    adminName,
                    AdminRole,
                    IsSystemAdmin: true);
            }

            return null;
        }

        var user = await userRepository.GetByEmailAsync(normalizedEmail, ct);
        if (user is null)
        {
            return null;
        }

        if (!BCrypt.Net.BCrypt.Verify(password, user.PasswordHash))
        {
            return null;
        }

        return new AuthenticatedUserDto(
            user.Id,
            user.Email,
            user.FullName,
            UserRole,
            IsSystemAdmin: false,
            user.AvatarUrl);
    }

    public async Task<UserProfileDto?> GetProfileAsync(Guid userId, CancellationToken ct = default)
    {
        var user = await userRepository.GetByIdAsync(userId, ct);
        return user is null ? null : MapToProfileDto(user);
    }

    public async Task<UserProfileDto> UpdateBankProfileAsync(
        Guid userId,
        UpdateBankProfileRequest request,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var user = await userRepository.GetByIdAsync(userId, ct)
            ?? throw new KeyNotFoundException("Không tìm thấy người dùng.");

        var fullName = (request.FullName ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(fullName) || fullName.Length > 150)
        {
            throw new ArgumentException("Họ và tên phải từ 1 đến 150 ký tự.", nameof(request));
        }

        var bankCode = CleanOptional(request.BankCode, 32)?.ToUpperInvariant();
        var bankBin = CleanOptional(request.BankBin, 16);
        var accountNumber = CleanOptional(request.BankAccountNumber, 50)?.Replace(" ", string.Empty);
        var accountHolder = string.IsNullOrWhiteSpace(request.BankAccountHolder)
            ? null
            : VietQrBankCatalog.NormalizeAsciiUpper(request.BankAccountHolder);

        var matchedBank = VietQrBankCatalog.FindByBinOrCode(bankBin ?? bankCode);
        if (matchedBank is not null)
        {
            bankCode = matchedBank.BankCode;
            bankBin = matchedBank.BankBin;
        }

        if (!string.IsNullOrWhiteSpace(accountNumber) && string.IsNullOrWhiteSpace(bankBin) && string.IsNullOrWhiteSpace(bankCode))
        {
            throw new ArgumentException("Vui lòng chọn ngân hàng thụ hưởng khi nhập số tài khoản.", nameof(request));
        }

        user.FullName = fullName;
        user.AvatarUrl = CleanOptional(request.AvatarUrl, 500);
        user.BankCode = bankCode;
        user.BankBin = bankBin;
        user.BankAccountNumber = accountNumber;
        user.BankAccountHolder = accountHolder;

        await userRepository.UpdateAsync(user, ct);

        return MapToProfileDto(user);
    }

    private static string NormalizeAndValidateEmail(string email)
    {
        var trimmed = (email ?? string.Empty).Trim().ToLowerInvariant();
        if (string.IsNullOrWhiteSpace(trimmed) || trimmed.Length > 256)
        {
            throw new ArgumentException("Địa chỉ email không hợp lệ.", nameof(email));
        }

        try
        {
            var addr = new MailAddress(trimmed);
            if (!string.Equals(addr.Address, trimmed, StringComparison.OrdinalIgnoreCase))
            {
                throw new ArgumentException("Địa chỉ email không hợp lệ.", nameof(email));
            }
        }
        catch (FormatException ex)
        {
            throw new ArgumentException("Địa chỉ email không hợp lệ.", nameof(email), ex);
        }

        return trimmed;
    }

    private static string? CleanOptional(string? value, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;

        var trimmed = value.Trim();
        return trimmed.Length <= maxLength ? trimmed : trimmed[..maxLength];
    }

    private static UserProfileDto MapToProfileDto(User user)
        => new(
            user.Id,
            user.Email,
            user.FullName,
            user.AvatarUrl,
            user.BankCode,
            user.BankBin,
            user.BankAccountNumber,
            user.BankAccountHolder,
            user.CreatedAtUtc);
}
