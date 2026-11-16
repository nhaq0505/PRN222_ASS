using System.Text;
using CourtBook.BusinessObjects.Entities;
using CourtBook.BusinessObjects.Enums;
using CourtBook.Services.Models;

namespace CourtBook.Tests;

public class NewFeaturesTests
{
    [Fact]
    public async Task Profile_GetAndUpdate_Succeeds_AndBlocksLockedUser()
    {
        using var scope = new TestContextScope();
        SeededTestData data = await scope.SeedMinimalAsync();

        var profile = await scope.AuthService.GetProfileAsync(data.Member1.Id);
        Assert.True(profile.Succeeded);
        Assert.NotNull(profile.Data);
        Assert.Equal(data.Member1.FullName, profile.Data.FullName);
        Assert.Equal(data.Member1.Email, profile.Data.Email);
        Assert.Equal(DateTimeKind.Utc, profile.Data.CreatedAtUtc.Kind);

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

        // Lock Member1 -> UpdateProfileAsync & ChangePasswordAsync must be blocked
        data.Member1.IsActive = false;
        await scope.UserRepository.UpdateAsync(data.Member1);

        var lockedUpdate = await scope.AuthService.UpdateProfileAsync(data.Member1.Id, updateReq);
        Assert.False(lockedUpdate.Succeeded);
        Assert.Equal("Tài khoản của bạn đã bị tạm khóa hoặc không tồn tại.", lockedUpdate.ErrorMessage);

        var lockedPassword = await scope.AuthService.ChangePasswordAsync(data.Member1.Id, new ChangePasswordRequestDto
        {
            CurrentPassword = "User@123",
            NewPassword = "NewSecurePass@456",
            ConfirmNewPassword = "NewSecurePass@456"
        });
        Assert.False(lockedPassword.Succeeded);
        Assert.Equal("Tài khoản của bạn đã bị tạm khóa hoặc không tồn tại.", lockedPassword.ErrorMessage);
    }

    [Fact]
    public async Task Profile_ChangePassword_ValidatesOldPassword()
    {
        using var scope = new TestContextScope();
        SeededTestData data = await scope.SeedMinimalAsync();

        var badOldPass = new ChangePasswordRequestDto
        {
            CurrentPassword = "WrongPassword123!",
            NewPassword = "NewPassword123!",
            ConfirmNewPassword = "NewPassword123!"
        };
        var failed = await scope.AuthService.ChangePasswordAsync(data.Member1.Id, badOldPass);
        Assert.False(failed.Succeeded);

        var correctPass = new ChangePasswordRequestDto
        {
            CurrentPassword = "User@123",
            NewPassword = "NewSecurePass@456",
            ConfirmNewPassword = "NewSecurePass@456"
        };
        var success = await scope.AuthService.ChangePasswordAsync(data.Member1.Id, correctPass);
        Assert.True(success.Succeeded);

        var loginRes = await scope.AuthService.AuthenticateAsync(data.Member1.Email, "NewSecurePass@456");
        Assert.True(loginRes.Succeeded);
        Assert.NotNull(loginRes.Data);
        Assert.Equal(data.Member1.Id, loginRes.Data.Id);
    }

    [Fact]
    public async Task VenueReviews_AddAndRetrieveReviews_EnforcesBusinessAndRoleRules()
    {
        using var scope = new TestContextScope();
        SeededTestData data = await scope.SeedMinimalAsync();

        var res1 = await scope.ReviewService.AddReviewAsync(data.Member1.Id, new CreateReviewRequestDto
        {
            VenueId = data.Venue1.Id,
            Rating = 5,
            Comment = "San rat dep, san chuan BWF!"
        });
        Assert.True(res1.Succeeded);
        Assert.Equal(5, res1.Data!.Rating);
        Assert.Equal(DateTimeKind.Utc, res1.Data.CreatedAtUtc.Kind);

        var res2 = await scope.ReviewService.AddReviewAsync(data.Member2.Id, new CreateReviewRequestDto
        {
            VenueId = data.Venue1.Id,
            Rating = 3,
            Comment = "San on nhung hoi dong vao cuoi tuan"
        });
        Assert.True(res2.Succeeded);
        Assert.Equal(3, res2.Data!.Rating);

        IReadOnlyList<VenueReviewDto> venueReviews = await scope.ReviewService.GetVenueReviewsAsync(data.Venue1.Id);
        Assert.Equal(2, venueReviews.Count);
        Assert.All(venueReviews, r => Assert.Equal(DateTimeKind.Utc, r.CreatedAtUtc.Kind));

        // Invalid rating (> 5) should fail validation
        var invalidRes = await scope.ReviewService.AddReviewAsync(data.Member1.Id, new CreateReviewRequestDto
        {
            VenueId = data.Venue1.Id,
            Rating = 6,
            Comment = "Invalid rating"
        });
        Assert.False(invalidRes.Succeeded);

        // Admin cannot review venues
        var adminReview = await scope.ReviewService.AddReviewAsync(data.Admin.Id, new CreateReviewRequestDto
        {
            VenueId = data.Venue1.Id,
            Rating = 5,
            Comment = "Admin review attempt"
        });
        Assert.False(adminReview.Succeeded);
        Assert.Equal("Tài khoản Quản trị sàn không tham gia đánh giá sân.", adminReview.ErrorMessage);

        // CourtOwner cannot review their own venue, but can review another owner's active venue
        var selfReview = await scope.ReviewService.AddReviewAsync(data.Owner1.Id, new CreateReviewRequestDto
        {
            VenueId = data.Venue1.Id,
            Rating = 5,
            Comment = "Self review"
        });
        Assert.False(selfReview.Succeeded);
        Assert.Equal("Chủ sân không thể tự đánh giá cụm sân của chính mình.", selfReview.ErrorMessage);

        var otherOwnerReview = await scope.ReviewService.AddReviewAsync(data.Owner1.Id, new CreateReviewRequestDto
        {
            VenueId = data.Venue2.Id,
            Rating = 4,
            Comment = "Sân bạn chất lượng tốt!"
        });
        Assert.True(otherOwnerReview.Succeeded);

        // Inactive venue (PendingApproval) cannot be reviewed
        var pendingVenueRes = await scope.VenueService.CreateVenueAsync(data.Owner1.Id, new VenueUpsertDto
        {
            Name = "Sân Chờ Duyệt",
            District = "Quận 1",
            Address = "12 Lê Lợi",
            PhoneNumber = "0909111222"
        });
        Assert.True(pendingVenueRes.Succeeded);
        var pendingReview = await scope.ReviewService.AddReviewAsync(data.Member1.Id, new CreateReviewRequestDto
        {
            VenueId = pendingVenueRes.Data!.Id,
            Rating = 5,
            Comment = "Review sân chưa duyệt"
        });
        Assert.False(pendingReview.Succeeded);
    }

    [Fact]
    public async Task Marketplace_AdvancedFilteringAndSorting_Works()
    {
        using var scope = new TestContextScope();
        SeededTestData data = await scope.SeedMinimalAsync();

        await scope.ReviewService.AddReviewAsync(data.Member1.Id, new CreateReviewRequestDto
        {
            VenueId = data.Venue1.Id,
            Rating = 5,
            Comment = "Tuyet voi"
        });

        DateOnly todayVn = new(2026, 11, 20);
        MarketplaceBrowseDto result = await scope.VenueService.GetMarketplaceAdvancedAsync(
            keyword: null,
            district: null,
            sport: null,
            todayVn: todayVn,
            sortBy: "rating",
            maxPrice: 500_000m);

        Assert.NotNull(result);
        Assert.NotEmpty(result.Venues);
        Assert.Equal(data.Venue1.Id, result.Venues[0].Id);
        Assert.Equal(5.0, result.Venues[0].AverageRating);
        Assert.Equal(1, result.Venues[0].ReviewCount);
    }

    [Fact]
    public async Task Booking_ConfirmWithVietQr_QuickCheckInAndPaymentToggle_WorkWithStrictGuards()
    {
        // TestContextScope starts at 2026-11-20 03:00 UTC = 10:00 VN -> slot 14:00 today is valid & same-day!
        using var scope = new TestContextScope();
        SeededTestData data = await scope.SeedMinimalAsync();
        DateOnly todayVn = new(2026, 11, 20);
        DateOnly futureDate = new(2026, 11, 22);

        var holdRes = await scope.BookingService.ToggleHoldSlotAsync(
            data.Member1.Id,
            data.Venue1Pickleball.Id,
            todayVn,
            14);
        Assert.True(holdRes.Succeeded);
        Assert.Equal("held", holdRes.Data!.Action);
        int heldBookingId = holdRes.Data.UpdatedSlot.BookingId!.Value;

        // Held booking cannot toggle payment status
        var toggleHeld = await scope.BookingService.TogglePaymentStatusAsync(
            heldBookingId, data.Owner1.Id, UserRole.CourtOwner);
        Assert.False(toggleHeld.Succeeded);
        Assert.Equal("Chỉ cập nhật thanh toán cho đơn đã xác nhận.", toggleHeld.ErrorMessage);

        // Invalid paymentMethod is rejected
        var invalidPaymentConfirm = await scope.BookingService.ConfirmMyHoldsAsync(
            data.Member1.Id,
            data.Venue1.Id,
            notes: null,
            paymentMethod: "CryptoWallet");
        Assert.False(invalidPaymentConfirm.Succeeded);

        // Confirm with VietQR payment method
        var confirmRes = await scope.BookingService.ConfirmMyHoldsAsync(
            data.Member1.Id,
            data.Venue1.Id,
            notes: null,
            paymentMethod: "Chuyển khoản VietQR");
        Assert.True(confirmRes.Succeeded);
        IReadOnlyList<BookingTicketDto> tickets = confirmRes.Data!;
        Assert.Single(tickets);
        BookingTicketDto ticket = tickets[0];
        Assert.Equal("Chuyển khoản VietQR", ticket.PaymentMethod);
        Assert.False(ticket.IsPaid);
        Assert.NotNull(ticket.VietQrUrl);
        Assert.Contains("img.vietqr.io", ticket.VietQrUrl);
        Assert.Contains("CB%20", ticket.VietQrUrl);

        // Admin & cross-owner cannot toggle payment status
        var adminToggle = await scope.BookingService.TogglePaymentStatusAsync(
            ticket.Id, data.Admin.Id, UserRole.Admin);
        Assert.False(adminToggle.Succeeded);

        var crossOwnerToggle = await scope.BookingService.TogglePaymentStatusAsync(
            ticket.Id, data.Owner2.Id, UserRole.CourtOwner);
        Assert.False(crossOwnerToggle.Succeeded);

        // Owner1 toggles payment status: false -> true -> false -> true
        var toggleTrue = await scope.BookingService.TogglePaymentStatusAsync(
            ticket.Id, data.Owner1.Id, UserRole.CourtOwner);
        Assert.True(toggleTrue.Succeeded);
        Assert.True(toggleTrue.Data);

        var toggleFalse = await scope.BookingService.TogglePaymentStatusAsync(
            ticket.Id, data.Owner1.Id, UserRole.CourtOwner);
        Assert.True(toggleFalse.Succeeded);
        Assert.False(toggleFalse.Data);

        // Admin & cross-owner cannot QuickCheckInByCode
        var adminQuickCheckIn = await scope.BookingService.QuickCheckInByCodeAsync(
            ticket.BookingCode, data.Admin.Id, UserRole.Admin);
        Assert.False(adminQuickCheckIn.Succeeded);
        Assert.Equal(
            "Quản trị sàn chỉ giám sát hệ thống, không trực tiếp check-in thu tiền tại sân của đối tác.",
            adminQuickCheckIn.ErrorMessage);

        var crossOwnerQuickCheckIn = await scope.BookingService.QuickCheckInByCodeAsync(
            ticket.BookingCode, data.Owner2.Id, UserRole.CourtOwner);
        Assert.False(crossOwnerQuickCheckIn.Succeeded);

        // Owner1 QuickCheckInByCode on play date succeeds and marks IsPaid = true
        var checkInRes = await scope.BookingService.QuickCheckInByCodeAsync(
            ticket.BookingCode, data.Owner1.Id, UserRole.CourtOwner);
        Assert.True(checkInRes.Succeeded);
        Assert.Equal(BookingStatus.CheckedIn, checkInRes.Data!.Status);
        Assert.True(checkInRes.Data.IsPaid);

        // Cannot unpay a CheckedIn booking
        var toggleCheckedIn = await scope.BookingService.TogglePaymentStatusAsync(
            ticket.Id, data.Owner1.Id, UserRole.CourtOwner);
        Assert.False(toggleCheckedIn.Succeeded);
        Assert.Equal("Đơn đã check-in được tính là đã thanh toán.", toggleCheckedIn.ErrorMessage);

        // Future-date booking cannot QuickCheckInByCode early (Rule 2 same-day enforcement)
        await scope.BookingService.ToggleHoldSlotAsync(data.Member2.Id, data.Venue1Pickleball.Id, futureDate, 15);
        var futureConfirm = await scope.BookingService.ConfirmMyHoldsAsync(data.Member2.Id, data.Venue1.Id);
        Assert.True(futureConfirm.Succeeded);
        var earlyQuickCheckIn = await scope.BookingService.QuickCheckInByCodeAsync(
            futureConfirm.Data![0].BookingCode, data.Owner1.Id, UserRole.CourtOwner);
        Assert.False(earlyQuickCheckIn.Succeeded);
    }

    [Fact]
    public async Task Owner_ExportBookingsCsv_GeneratesValidCsv_AndBlocksAdminOrCrossOwner()
    {
        using var scope = new TestContextScope();
        SeededTestData data = await scope.SeedMinimalAsync();

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

        // Admin and cross-owner are strictly rejected
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            scope.BookingService.ExportOwnerBookingsCsvAsync(
                venueId: data.Venue1.Id,
                actorUserId: data.Admin.Id,
                actorRole: UserRole.Admin));

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            scope.BookingService.ExportOwnerBookingsCsvAsync(
                venueId: data.Venue1.Id,
                actorUserId: data.Owner2.Id,
                actorRole: UserRole.CourtOwner));

        byte[] csvBytes = await scope.BookingService.ExportOwnerBookingsCsvAsync(
            venueId: data.Venue1.Id,
            actorUserId: data.Owner1.Id,
            actorRole: UserRole.CourtOwner);

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
        SeededTestData data = await scope.SeedMinimalAsync();

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
        IReadOnlyList<BookingTicketDto> tickets = confirmRes.Data!;
        Assert.Single(tickets);

        PlatformAdminDashboardDto dashboard = await scope.PlatformAdminService.GetDashboardAsync(futureDate);
        Assert.NotNull(dashboard);
        Assert.Equal(10m, dashboard.PlatformCommissionPercent);
        Assert.Equal(Math.Round(dashboard.TodayPlatformGmvVnd * 0.10m, 0), dashboard.TodayPlatformCommissionVnd);
        Assert.True(dashboard.TotalAllTimeGmvVnd >= tickets[0].PriceVnd);
        Assert.Equal(Math.Round(dashboard.TotalAllTimeGmvVnd * 0.10m, 0), dashboard.TotalAllTimeCommissionVnd);
    }
}
