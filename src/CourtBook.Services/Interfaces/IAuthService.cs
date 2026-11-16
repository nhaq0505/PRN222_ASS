using CourtBook.Services.Models;

namespace CourtBook.Services.Interfaces;

public interface IAuthService
{
    Task<ServiceResult<AuthUserDto>> AuthenticateAsync(string email, string password, CancellationToken ct = default);
    Task<ServiceResult<AuthUserDto>> RegisterAsync(RegisterRequestDto request, CancellationToken ct = default);
    Task<AuthUserDto?> GetByIdAsync(int userId, CancellationToken ct = default);
    IReadOnlyList<DemoAccountDto> GetDemoAccounts();
}
