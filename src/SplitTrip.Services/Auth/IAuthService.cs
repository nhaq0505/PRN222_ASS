using SplitTrip.Services.Auth.Dtos;

namespace SplitTrip.Services.Auth;

public interface IAuthService
{
    Task<AuthenticatedUserDto> RegisterAsync(RegisterUserRequest request, CancellationToken ct = default);
    Task<AuthenticatedUserDto?> LoginAsync(string email, string password, CancellationToken ct = default);
    Task<UserProfileDto?> GetProfileAsync(Guid userId, CancellationToken ct = default);
    Task<UserProfileDto> UpdateBankProfileAsync(Guid userId, UpdateBankProfileRequest request, CancellationToken ct = default);
}
