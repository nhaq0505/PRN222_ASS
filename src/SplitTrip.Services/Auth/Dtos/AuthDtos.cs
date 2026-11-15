namespace SplitTrip.Services.Auth.Dtos;

public sealed record AuthenticatedUserDto(
    Guid UserId,
    string Email,
    string FullName,
    string Role,
    bool IsSystemAdmin,
    string? AvatarUrl = null);

public sealed record RegisterUserRequest(
    string Email,
    string Password,
    string FullName);

public sealed record UpdateBankProfileRequest(
    string FullName,
    string? AvatarUrl,
    string? BankCode,
    string? BankBin,
    string? BankAccountNumber,
    string? BankAccountHolder);

public sealed record UserProfileDto(
    Guid UserId,
    string Email,
    string FullName,
    string? AvatarUrl,
    string? BankCode,
    string? BankBin,
    string? BankAccountNumber,
    string? BankAccountHolder,
    DateTime CreatedAtUtc);

public sealed record VietQrBankInfo(
    string BankCode,
    string BankBin,
    string ShortName,
    string FullName);
