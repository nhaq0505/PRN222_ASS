using CourtBook.Services.Models;

namespace CourtBook.Services.Interfaces;

public interface IAuthService
{
    Task<ServiceResult<AuthUserDto>> AuthenticateAsync(string email, string password, CancellationToken ct = default);
    Task<ServiceResult<AuthUserDto>> RegisterAsync(RegisterRequestDto request, CancellationToken ct = default);
    Task<AuthUserDto?> GetByIdAsync(int userId, CancellationToken ct = default);
    Task<ServiceResult<UserProfileDto>> GetProfileAsync(int userId, CancellationToken ct = default);
    Task<ServiceResult<bool>> UpdateProfileAsync(int userId, UpdateProfileRequestDto req, CancellationToken ct = default);
    Task<ServiceResult<bool>> ChangePasswordAsync(int userId, ChangePasswordRequestDto req, CancellationToken ct = default);
    IReadOnlyList<DemoAccountDto> GetDemoAccounts();
}
