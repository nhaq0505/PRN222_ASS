using CourtBook.BusinessObjects.Enums;
using CourtBook.DataAccessObjects;
using CourtBook.Services.Models;
using Microsoft.EntityFrameworkCore;

namespace CourtBook.Tests;

public class AuthServiceTests
{
    [Fact]
    public async Task DbInitializer_SeedsExpectedUsersVenuesCourtsAndBookings_Idempotently()
    {
        using var scope = new TestContextScope();

        await DbInitializer.InitializeAsync(scope.Context);
        await DbInitializer.InitializeAsync(scope.Context);

        Assert.Equal(5, await scope.Context.Users.CountAsync());
        Assert.Equal(4, await scope.Context.Venues.CountAsync());
        Assert.Equal(3, await scope.Context.Venues.CountAsync(v => v.Status == VenueStatus.Active));
        Assert.Equal(1, await scope.Context.Venues.CountAsync(v => v.Status == VenueStatus.PendingApproval));
        Assert.Equal(18, await scope.Context.Courts.CountAsync());
        Assert.Equal(8, await scope.Context.Bookings.CountAsync());
    }

    [Fact]
    public async Task AuthenticateAsync_HandlesValidCaseInsensitiveWrongPasswordAndInactiveUser()
    {
        using var scope = new TestContextScope();
        await DbInitializer.InitializeAsync(scope.Context);

        ServiceResult<AuthUserDto> okResult = await scope.AuthService.AuthenticateAsync("  AN@CourtBook.VN ", "User@123");
        Assert.True(okResult.Succeeded);
        Assert.NotNull(okResult.Data);
        Assert.Equal(UserRole.Member, okResult.Data!.Role);
        Assert.Equal("an@courtbook.vn", okResult.Data.Email);

        ServiceResult<AuthUserDto> ownerOk = await scope.AuthService.AuthenticateAsync("khoi.owner@courtbook.vn", "Owner@123");
        Assert.True(ownerOk.Succeeded);
        Assert.Equal(UserRole.CourtOwner, ownerOk.Data!.Role);

        ServiceResult<AuthUserDto> wrongPass = await scope.AuthService.AuthenticateAsync("an@courtbook.vn", "WrongPass");
        Assert.False(wrongPass.Succeeded);

        var user = await scope.UserRepository.GetByEmailAsync("an@courtbook.vn");
        Assert.NotNull(user);
        user!.IsActive = false;
        await scope.UserRepository.UpdateAsync(user);

        ServiceResult<AuthUserDto> inactiveResult = await scope.AuthService.AuthenticateAsync("an@courtbook.vn", "User@123");
        Assert.False(inactiveResult.Succeeded);
    }

    [Fact]
    public async Task RegisterAsync_SupportsMemberAndCourtOwnerRoles_AndRejectsInvalidOrDuplicate()
    {
        using var scope = new TestContextScope();

        var shortPass = await scope.AuthService.RegisterAsync(new RegisterRequestDto
        {
            FullName = "Lê Văn C",
            Email = "c@courtbook.vn",
            PhoneNumber = "0909888777",
            Password = "123"
        });
        Assert.False(shortPass.Succeeded);

        var badEmail = await scope.AuthService.RegisterAsync(new RegisterRequestDto
        {
            FullName = "Lê Văn C",
            Email = "not-an-email",
            PhoneNumber = "0909888777",
            Password = "User@123"
        });
        Assert.False(badEmail.Succeeded);

        var memberRes = await scope.AuthService.RegisterAsync(new RegisterRequestDto
        {
            FullName = "Lê Văn C",
            Email = "C@CourtBook.vn",
            PhoneNumber = "0909888777",
            Password = "User@123",
            RegisterAsOwner = false
        });
        Assert.True(memberRes.Succeeded);
        Assert.Equal(UserRole.Member, memberRes.Data!.Role);
        Assert.Equal("c@courtbook.vn", memberRes.Data.Email);

        var ownerRes = await scope.AuthService.RegisterAsync(new RegisterRequestDto
        {
            FullName = "Trần Chủ Sân",
            Email = "owner.new@courtbook.vn",
            PhoneNumber = "0909777666",
            Password = "Owner@123",
            RegisterAsOwner = true
        });
        Assert.True(ownerRes.Succeeded);
        Assert.Equal(UserRole.CourtOwner, ownerRes.Data!.Role);

        var duplicate = await scope.AuthService.RegisterAsync(new RegisterRequestDto
        {
            FullName = "Lê Văn C2",
            Email = "c@courtbook.vn",
            PhoneNumber = "0909888777",
            Password = "User@123"
        });
        Assert.False(duplicate.Succeeded);
    }

    [Fact]
    public async Task GetDemoAccounts_ReturnsFiveGroupedDemoAccountsThatAllAuthenticate()
    {
        using var scope = new TestContextScope();
        await DbInitializer.InitializeAsync(scope.Context);

        IReadOnlyList<DemoAccountDto> demos = scope.AuthService.GetDemoAccounts();
        Assert.Equal(5, demos.Count);
        Assert.Equal(2, demos.Count(d => d.Group == "Member"));
        Assert.Equal(2, demos.Count(d => d.Group == "Owner"));
        Assert.Equal(1, demos.Count(d => d.Group == "Admin"));

        foreach (DemoAccountDto demo in demos)
        {
            ServiceResult<AuthUserDto> auth = await scope.AuthService.AuthenticateAsync(demo.Email, demo.Password);
            Assert.True(auth.Succeeded, $"Demo account {demo.Email} should authenticate.");
        }
    }
}
