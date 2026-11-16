using System.Text;
using CourtBook.BusinessObjects.Entities;
using CourtBook.BusinessObjects.Enums;
using CourtBook.Services.Models;
using Xunit;

namespace CourtBook.Tests;

public class NewFeaturesTests
{
    [Fact]
    public async Task Profile_GetAndUpdate_Succeeds()
    {
        using var scope = new TestContextScope();
        var data = await scope.SeedMinimalAsync();

        var profile = await scope.AuthService.GetProfileAsync(data.Member1.Id);
        Assert.True(profile.Succeeded);
        Assert.NotNull(profile.Data);
        Assert.Equal(data.Member1.FullName, profile.Data.FullName);
        Assert.Equal(data.Member1.Email, profile.Data.Email);

        var updateReq = new UpdateProfileRequestDto
        {
            FullName = "Nguyen Van Updated",
            PhoneNumber = "0987654321"
        };

        var updated = await scope.AuthService.UpdateProfileAsync(data.Member1.Id, updateReq);
        Assert.True(updated.Succeeded);

        var refreshed = await scope.AuthService.GetProfileAsync(data.Member1.Id);
        Assert.True(refreshed.Succeeded);
        Assert.NotNull(refreshed.Data);
        Assert.Equal("Nguyen Van Updated", refreshed.Data.FullName);
        Assert.Equal("0987654321", refreshed.Data.PhoneNumber);
    }

    [Fact]
    public async Task Profile_ChangePassword_ValidatesOldPassword()
    {
        using var scope = new TestContextScope();
        var data = await scope.SeedMinimalAsync();

        // Wrong old password should fail
        var badOldPass = new ChangePasswordRequestDto
        {
            CurrentPassword = "WrongPassword123!",
            NewPassword = "NewPassword123!",
            ConfirmNewPassword = "NewPassword123!"
        };
        var failed = await scope.AuthService.ChangePasswordAsync(data.Member1.Id, badOldPass);
        Assert.False(failed.Succeeded);

        // Correct old password should succeed
        var correctPass = new ChangePasswordRequestDto
        {
            CurrentPassword = "User@123",
            NewPassword = "NewSecurePass@456",
            ConfirmNewPassword = "NewSecurePass@456"
        };
        var success = await scope.AuthService.ChangePasswordAsync(data.Member1.Id, correctPass);
        Assert.True(success.Succeeded);

        // Subsequent login with new password should succeed
        var loginRes = await scope.AuthService.AuthenticateAsync(data.Member1.Email, "NewSecurePass@456");
        Assert.True(loginRes.Succeeded);
        Assert.NotNull(loginRes.Data);
        Assert.Equal(data.Member1.Id, loginRes.Data.Id);
    }

    [Fact]
    public async Task VenueReviews_AddAndRetrieveReviews_Works()
    {
        using var scope = new TestContextScope();
        var data = await scope.SeedMinimalAsync();

        // Add 5-star review from Member1
        var reviewReq1 = new CreateReviewRequestDto
        {
            VenueId = data.Venue1.Id,
            Rating = 5,
            Comment = "San rat dep, san chuan BWF!"
        };
        var res1 = await scope.ReviewService.AddReviewAsync(data.Member1.Id, reviewReq1);
        Assert.True(res1.Succeeded);
        Assert.Equal(5, res1.Data!.Rating);

        // Add 3-star review from Member2
        var reviewReq2 = new CreateReviewRequestDto
        {
            VenueId = data.Venue1.Id,
            Rating = 3,
            Comment = "San on nhung hoi dong vao cuoi tuan"
        };
        var res2 = await scope.ReviewService.AddReviewAsync(data.Member2.Id, reviewReq2);
        Assert.True(res2.Succeeded);
        Assert.Equal(3, res2.Data!.Rating);

        // Retrieve reviews for Venue1
        var venueReviews = await scope.ReviewService.GetVenueReviewsAsync(data.Venue1.Id);
        Assert.Equal(2, venueReviews.Count);

        // Invalid rating (> 5) should fail validation
        var invalidReq = new CreateReviewRequestDto
        {
            VenueId = data.Venue1.Id,
            Rating = 6,
            Comment = "Invalid rating"
        };
        var invalidRes = await scope.ReviewService.AddReviewAsync(data.Member1.Id, invalidReq);
        Assert.False(invalidRes.Succeeded);
    }

    [Fact]
    public async Task Marketplace_AdvancedFilteringAndSorting_Works()
    {
        using var scope = new TestContextScope();
        var data = await scope.SeedMinimalAsync();

        // Seed a review to give Venue1 high rating
        await scope.ReviewService.AddReviewAsync(data.Member1.Id, new CreateReviewRequestDto
        {
            VenueId = data.Venue1.Id,
            Rating = 5,
            Comment = "Tuyet voi"
        });

        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var result = await scope.VenueService.GetMarketplaceAdvancedAsync(
            keyword: null,
            district: null,
            sport: null,
            todayVn: today,
            sortBy: "rating",
            maxPrice: 500_000m);

        Assert.NotNull(result);
        Assert.NotEmpty(result.Venues);
        // Venue1 has 5-star rating, should be top result
        Assert.Equal(data.Venue1.Id, result.Venues[0].Id);
    }

    [Fact]
    public async Task Booking_ConfirmWithVietQr_QuickCheckInAndPaymentToggle()
    {
        using var scope = new TestContextScope();
        var data = await scope.SeedMinimalAsync();

        var futureDate = new DateOnly(2026, 11, 22);
        var holdRes = await scope.BookingService.ToggleHoldSlotAsync(
            data.Member1.Id,
            data.Venue1Pickleball.Id,
            futureDate,
            14);

        Assert.True(holdRes.Succeeded);
        Assert.Equal("held", holdRes.Data!.Action);

        // Confirm with VietQR payment method
        var confirmRes = await scope.BookingService.ConfirmMyHoldsAsync(
            data.Member1.Id,
            data.Venue1.Id,
            notes: null,
            paymentMethod: "Chuyển khoản VietQR");

        Assert.True(confirmRes.Succeeded);
        var tickets = confirmRes.Data!;
        Assert.Single(tickets);
        var ticket = tickets[0];
        Assert.Equal("Chuyển khoản VietQR", ticket.PaymentMethod);
        Assert.False(ticket.IsPaid);
        Assert.NotNull(ticket.VietQrUrl);
        Assert.Contains("img.vietqr.io", ticket.VietQrUrl);

        // Owner toggles payment status
        var booking = await scope.BookingRepository.GetByIdAsync(ticket.Id);
        Assert.NotNull(booking);
        Assert.False(booking.IsPaid);

        var toggleSuccess = await scope.BookingService.TogglePaymentStatusAsync(
            booking.Id,
            data.Owner1.Id,
            isAdmin: false);
        Assert.True(toggleSuccess.Succeeded);

        var updatedBooking = await scope.BookingRepository.GetByIdAsync(booking.Id);
        Assert.NotNull(updatedBooking);
        Assert.True(updatedBooking.IsPaid);

        // Quick check-in by booking code
        var checkInRes = await scope.BookingService.QuickCheckInByCodeAsync(
            ticket.BookingCode,
            data.Owner1.Id,
            isAdmin: false);
        Assert.True(checkInRes.Succeeded);
        var checkInTicket = checkInRes.Data!;
        Assert.Equal(BookingStatus.CheckedIn, checkInTicket.Status);
    }

    [Fact]
    public async Task Owner_ExportBookingsCsv_GeneratesValidCsv()
    {
        using var scope = new TestContextScope();
        var data = await scope.SeedMinimalAsync();

        var futureDate = new DateOnly(2026, 11, 22);
        var holdRes = await scope.BookingService.ToggleHoldSlotAsync(
            data.Member1.Id,
            data.Venue1Pickleball.Id,
            futureDate,
            10);
        Assert.True(holdRes.Succeeded);

        var confirmRes = await scope.BookingService.ConfirmMyHoldsAsync(
            data.Member1.Id,
            data.Venue1.Id,
            notes: null,
            paymentMethod: "Tại sân");
        Assert.True(confirmRes.Succeeded);

        var csvBytes = await scope.BookingService.ExportOwnerBookingsCsvAsync(
            venueId: data.Venue1.Id,
            actorUserId: data.Owner1.Id,
            isAdmin: false);

        Assert.NotNull(csvBytes);
        string csv = Encoding.UTF8.GetString(csvBytes);
        Assert.Contains("Mã Đặt Sân,Tên Sân", csv);
        Assert.Contains(data.Member1.FullName, csv);
        Assert.Contains("Tại sân", csv);
    }

    [Fact]
    public async Task Admin_PlatformCommissionCalculation_Works()
    {
        using var scope = new TestContextScope();
        var data = await scope.SeedMinimalAsync();

        var futureDate = new DateOnly(2026, 11, 22);
        var holdRes = await scope.BookingService.ToggleHoldSlotAsync(
            data.Member1.Id,
            data.Venue1Pickleball.Id,
            futureDate,
            9);
        Assert.True(holdRes.Succeeded);

        var confirmRes = await scope.BookingService.ConfirmMyHoldsAsync(
            data.Member1.Id,
            data.Venue1.Id,
            notes: null,
            paymentMethod: "Tại sân");
        Assert.True(confirmRes.Succeeded);
        var tickets = confirmRes.Data!;
        Assert.Single(tickets);

        var dashboard = await scope.PlatformAdminService.GetDashboardAsync(futureDate);
        Assert.NotNull(dashboard);
        Assert.Equal(10, dashboard.PlatformCommissionPercent);
        Assert.True(dashboard.TotalAllTimeGmvVnd >= tickets[0].PriceVnd);
        Assert.Equal(dashboard.TotalAllTimeGmvVnd * 10 / 100, dashboard.TotalAllTimeCommissionVnd);
    }
}
