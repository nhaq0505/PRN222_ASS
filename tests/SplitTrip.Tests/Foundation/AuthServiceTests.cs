using Microsoft.EntityFrameworkCore;
using SplitTrip.Services.Auth;
using SplitTrip.Services.Auth.Dtos;

namespace SplitTrip.Tests.Foundation;

public sealed class AuthServiceTests
{
    [Fact]
    public async Task RegisterAndLogin_WithValidCredentials_HashesPasswordWithBCryptAndAuthenticates()
    {
        await using var harness = new FoundationTestHarness();

        var registered = await harness.AuthService.RegisterAsync(
            new RegisterUserRequest("  An.Nguyen@Example.com ", "Secret#123", "Nguyễn Văn An"));

        Assert.Equal("an.nguyen@example.com", registered.Email);
        Assert.Equal(AuthService.UserRole, registered.Role);
        Assert.False(registered.IsSystemAdmin);

        var persisted = await harness.DbContext.Users.SingleAsync(u => u.Id == registered.UserId);
        Assert.NotEqual("Secret#123", persisted.PasswordHash);
        Assert.StartsWith("$2", persisted.PasswordHash);

        var loggedIn = await harness.AuthService.LoginAsync("AN.NGUYEN@example.com", "Secret#123");
        Assert.NotNull(loggedIn);
        Assert.Equal(registered.UserId, loggedIn.UserId);

        var wrongPassword = await harness.AuthService.LoginAsync("an.nguyen@example.com", "WrongPass!");
        Assert.Null(wrongPassword);
    }

    [Fact]
    public async Task Login_WithAppsettingsAdminCredentials_ReturnsSystemAdminPrincipal()
    {
        await using var harness = new FoundationTestHarness();

        var adminLogin = await harness.AuthService.LoginAsync("ADMIN@splittrip.vn", "Admin@123");

        Assert.NotNull(adminLogin);
        Assert.True(adminLogin.IsSystemAdmin);
        Assert.Equal(AuthService.AdminRole, adminLogin.Role);
        Assert.Equal(AuthService.SystemAdminUserId, adminLogin.UserId);

        var invalidAdmin = await harness.AuthService.LoginAsync("admin@splittrip.vn", "WrongAdminPass");
        Assert.Null(invalidAdmin);
    }

    [Fact]
    public async Task Register_WithDuplicateEmailOrAdminEmail_ThrowsInvalidOperationException()
    {
        await using var harness = new FoundationTestHarness();

        await harness.AuthService.RegisterAsync(
            new RegisterUserRequest("member@splittrip.vn", "Secret#123", "Member One"));

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            harness.AuthService.RegisterAsync(
                new RegisterUserRequest("MEMBER@splittrip.vn", "Another#123", "Member Duplicate")));

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            harness.AuthService.RegisterAsync(
                new RegisterUserRequest("admin@splittrip.vn", "Secret#123", "Fake Admin")));
    }

    [Fact]
    public async Task UpdateBankProfile_ResolvesNapasBinAndNormalizesVietnameseAccountHolder()
    {
        await using var harness = new FoundationTestHarness();

        var user = await harness.AuthService.RegisterAsync(
            new RegisterUserRequest("linh@splittrip.vn", "Secret#123", "Trần Đặng Linh"));

        var updated = await harness.AuthService.UpdateBankProfileAsync(
            user.UserId,
            new UpdateBankProfileRequest(
                FullName: "Trần Đặng Linh",
                AvatarUrl: "https://example.com/avatar.png",
                BankCode: "vcb",
                BankBin: null,
                BankAccountNumber: "0123 4567 89",
                BankAccountHolder: "Trần Đặng Linh"));

        Assert.Equal("VCB", updated.BankCode);
        Assert.Equal("970436", updated.BankBin);
        Assert.Equal("0123456789", updated.BankAccountNumber);
        Assert.Equal("TRAN DANG LINH", updated.BankAccountHolder);

        await Assert.ThrowsAsync<ArgumentException>(() =>
            harness.AuthService.UpdateBankProfileAsync(
                user.UserId,
                new UpdateBankProfileRequest(
                    FullName: "Trần Đặng Linh",
                    AvatarUrl: null,
                    BankCode: null,
                    BankBin: null,
                    BankAccountNumber: "99998888",
                    BankAccountHolder: "TRAN DANG LINH")));
    }
}
