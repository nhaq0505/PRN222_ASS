using CourtBook.BusinessObjects.Entities;
using CourtBook.BusinessObjects.Enums;
using CourtBook.Services.Implementations;
using CourtBook.Services.Models;
using Microsoft.EntityFrameworkCore;

namespace CourtBook.Tests;

public class BusinessRulesHardeningTests
{
    [Fact]
    public async Task Rule1_LockedOrMissingActor_BlockedAcrossAllMutatingServices()
    {
        using var scope = new TestContextScope();
        SeededTestData seed = await scope.SeedMinimalAsync();
        DateOnly futureDate = new(2026, 11, 22);

        // Seed a confirmed booking and a hold for Member1 while still active
        await scope.BookingService.ToggleHoldSlotAsync(seed.Member1.Id, seed.Venue1Pickleball.Id, futureDate, 14);
        var confirmRes = await scope.BookingService.ConfirmMyHoldsAsync(seed.Member1.Id, seed.Venue1.Id);
        Assert.True(confirmRes.Succeeded);
        int bookingId = confirmRes.Data![0].Id;

        await scope.BookingService.ToggleHoldSlotAsync(seed.Member1.Id, seed.Venue1Pickleball.Id, futureDate, 15);

        // Lock Member1 and Owner1 directly (bypassing ToggleUserActiveAsync hold cleanup to test guards directly)
        seed.Member1.IsActive = false;
        seed.Owner1.IsActive = false;
        await scope.UserRepository.UpdateAsync(seed.Member1);
        await scope.UserRepository.UpdateAsync(seed.Owner1);

        const string expectedMsg = "Tài khoản của bạn đã bị tạm khóa hoặc không tồn tại.";

        // Locked Member1 blocked on all BookingService mutations
        var holdLocked = await scope.BookingService.ToggleHoldSlotAsync(seed.Member1.Id, seed.Venue1Pickleball.Id, futureDate, 16);
        Assert.False(holdLocked.Succeeded);
        Assert.Equal(expectedMsg, holdLocked.ErrorMessage);

        var confirmLocked = await scope.BookingService.ConfirmMyHoldsAsync(seed.Member1.Id, seed.Venue1.Id);
        Assert.False(confirmLocked.Succeeded);
        Assert.Equal(expectedMsg, confirmLocked.ErrorMessage);

        var cancelLocked = await scope.BookingService.CancelBookingAsync(bookingId, seed.Member1.Id, UserRole.Member);
        Assert.False(cancelLocked.Succeeded);
        Assert.Equal(expectedMsg, cancelLocked.ErrorMessage);

        // Locked Owner1 blocked on CheckIn, VenueService, and CourtService mutations
        var checkInLocked = await scope.BookingService.CheckInBookingAsync(bookingId, seed.Owner1.Id, UserRole.CourtOwner);
        Assert.False(checkInLocked.Succeeded);
        Assert.Equal(expectedMsg, checkInLocked.ErrorMessage);

        var createVenueLocked = await scope.VenueService.CreateVenueAsync(
            seed.Owner1.Id,
            new VenueUpsertDto
            {
                Name = "Sân Bị Khóa",
                District = "Quận 1",
                Address = "10 Lê Lợi",
                PhoneNumber = "0909111222"
            });
        Assert.False(createVenueLocked.Succeeded);
        Assert.Equal(expectedMsg, createVenueLocked.ErrorMessage);

        var updateVenueLocked = await scope.VenueService.UpdateVenueAsync(
            seed.Owner1.Id,
            new VenueUpsertDto
            {
                Id = seed.Venue1.Id,
                Name = seed.Venue1.Name,
                District = seed.Venue1.District,
                Address = seed.Venue1.Address,
                PhoneNumber = seed.Venue1.PhoneNumber
            });
        Assert.False(updateVenueLocked.Succeeded);
        Assert.Equal(expectedMsg, updateVenueLocked.ErrorMessage);

        var createCourtLocked = await scope.CourtService.CreateAsync(
            seed.Owner1.Id,
            new CourtUpsertDto
            {
                VenueId = seed.Venue1.Id,
                Code = "PB-99",
                Name = "Sân Mới",
                Surface = "Acrylic",
                BasePricePerHour = 100_000m,
                PeakPricePerHour = 150_000m
            });
        Assert.False(createCourtLocked.Succeeded);
        Assert.Equal(expectedMsg, createCourtLocked.ErrorMessage);

        var updateCourtLocked = await scope.CourtService.UpdateAsync(
            seed.Owner1.Id,
            new CourtUpsertDto
            {
                Id = seed.Venue1Pickleball.Id,
                VenueId = seed.Venue1.Id,
                Code = seed.Venue1Pickleball.Code,
                Name = seed.Venue1Pickleball.Name,
                Surface = seed.Venue1Pickleball.Surface,
                BasePricePerHour = seed.Venue1Pickleball.BasePricePerHour,
                PeakPricePerHour = seed.Venue1Pickleball.PeakPricePerHour
            });
        Assert.False(updateCourtLocked.Succeeded);
        Assert.Equal(expectedMsg, updateCourtLocked.ErrorMessage);

        var toggleCourtLocked = await scope.CourtService.ToggleActiveAsync(seed.Owner1.Id, seed.Venue1Pickleball.Id);
        Assert.False(toggleCourtLocked.Succeeded);
        Assert.Equal(expectedMsg, toggleCourtLocked.ErrorMessage);

        // Missing user ID also rejected
        var missingUser = await scope.BookingService.ToggleHoldSlotAsync(99999, seed.Venue1Pickleball.Id, futureDate, 17);
        Assert.False(missingUser.Succeeded);
        Assert.Equal(expectedMsg, missingUser.ErrorMessage);
    }

    [Fact]
    public async Task Rule1_ToggleUserActiveOff_ImmediatelyExpiresAllActiveHoldsAcrossVenuesAndBroadcasts()
    {
        using var scope = new TestContextScope();
        SeededTestData seed = await scope.SeedMinimalAsync();
        DateOnly futureDate = new(2026, 11, 22);

        await scope.BookingService.ToggleHoldSlotAsync(seed.Member1.Id, seed.Venue1Pickleball.Id, futureDate, 18);
        await scope.BookingService.ToggleHoldSlotAsync(seed.Member1.Id, seed.Venue2Pickleball.Id, futureDate, 19);
        scope.Notifier.Clear();

        ServiceResult<AppUser> lockRes = await scope.PlatformAdminService.ToggleUserActiveAsync(seed.Member1.Id, seed.Admin.Id);
        Assert.True(lockRes.Succeeded);
        Assert.False(lockRes.Data!.IsActive);

        Assert.Equal(2, scope.Notifier.SlotUpdates.Count);
        Assert.All(scope.Notifier.SlotUpdates, u =>
        {
            Assert.Equal(BookingStatus.Expired, u.Status);
            Assert.Null(u.HoldExpiresAtUtc);
            Assert.Equal(seed.Member1.Id, u.UserId);
        });
        Assert.Contains(scope.Notifier.SlotUpdates, u => u.VenueId == seed.Venue1.Id);
        Assert.Contains(scope.Notifier.SlotUpdates, u => u.VenueId == seed.Venue2.Id);

        // Holds are cleared so Member2 can book immediately
        HeldSummaryDto remaining = await scope.BookingService.GetMyHeldSummaryAsync(seed.Member1.Id, seed.Venue1.Id);
        Assert.Equal(0, remaining.Count);
        Assert.True((await scope.BookingService.ToggleHoldSlotAsync(seed.Member2.Id, seed.Venue1Pickleball.Id, futureDate, 18)).Succeeded);

        // Unlocking Member1 restores ability to hold slots
        var unlockRes = await scope.PlatformAdminService.ToggleUserActiveAsync(seed.Member1.Id, seed.Admin.Id);
        Assert.True(unlockRes.Succeeded);
        Assert.True(unlockRes.Data!.IsActive);
        Assert.True((await scope.BookingService.ToggleHoldSlotAsync(seed.Member1.Id, seed.Venue1Pickleball.Id, futureDate, 20)).Succeeded);
    }

    [Fact]
    public async Task Rule2_CheckInBooking_EnforcesSameVietnamPlayDate()
    {
        // 2026-11-20 18:00 UTC == 2026-11-21 01:00 Vietnam time (UTC+7)
        using var scope = new TestContextScope(new DateTimeOffset(2026, 11, 20, 18, 0, 0, TimeSpan.Zero));
        SeededTestData seed = await scope.SeedMinimalAsync();
        DateOnly todayVn = new(2026, 11, 21);
        DateOnly tomorrowVn = todayVn.AddDays(1);
        DateOnly yesterdayVn = todayVn.AddDays(-1);

        var pastBooking = new Booking
        {
            BookingCode = "CB-PAST-CHK",
            CourtId = seed.Venue1Pickleball.Id,
            UserId = seed.Member1.Id,
            PlayDate = yesterdayVn,
            StartHour = 10,
            PriceVnd = 140_000m,
            Status = BookingStatus.Confirmed
        };
        var todayBooking = new Booking
        {
            BookingCode = "CB-TODAY-CHK",
            CourtId = seed.Venue1Pickleball.Id,
            UserId = seed.Member1.Id,
            PlayDate = todayVn,
            StartHour = 10,
            PriceVnd = 140_000m,
            Status = BookingStatus.Confirmed
        };
        var futureBooking = new Booking
        {
            BookingCode = "CB-FUT-CHK",
            CourtId = seed.Venue1Pickleball.Id,
            UserId = seed.Member1.Id,
            PlayDate = tomorrowVn,
            StartHour = 10,
            PriceVnd = 140_000m,
            Status = BookingStatus.Confirmed
        };
        scope.Context.Bookings.AddRange(pastBooking, todayBooking, futureBooking);
        await scope.Context.SaveChangesAsync();

        var failFuture = await scope.BookingService.CheckInBookingAsync(futureBooking.Id, seed.Owner1.Id, UserRole.CourtOwner);
        Assert.False(failFuture.Succeeded);
        Assert.Equal($"Chỉ có thể check-in đúng ngày chơi trên vé ({tomorrowVn:dd/MM/yyyy}).", failFuture.ErrorMessage);

        var failPast = await scope.BookingService.CheckInBookingAsync(pastBooking.Id, seed.Owner1.Id, UserRole.CourtOwner);
        Assert.False(failPast.Succeeded);
        Assert.Equal($"Đơn đặt ngày {yesterdayVn:dd/MM/yyyy} đã qua ngày chơi, không thể check-in.", failPast.ErrorMessage);

        var passToday = await scope.BookingService.CheckInBookingAsync(todayBooking.Id, seed.Owner1.Id, UserRole.CourtOwner);
        Assert.True(passToday.Succeeded);
        Assert.Equal(BookingStatus.CheckedIn, passToday.Data!.Status);
    }

    [Fact]
    public async Task Rule2_GetOwnerPortal_SetsCanCheckInTrueOnlyOnTodayVn()
    {
        using var scope = new TestContextScope(new DateTimeOffset(2026, 11, 20, 3, 0, 0, TimeSpan.Zero)); // 10:00 VN
        SeededTestData seed = await scope.SeedMinimalAsync();
        DateOnly todayVn = new(2026, 11, 20);
        DateOnly tomorrowVn = todayVn.AddDays(1);
        DateOnly yesterdayVn = todayVn.AddDays(-1);

        scope.Context.Bookings.AddRange(
            new Booking
            {
                BookingCode = "CB-YEST",
                CourtId = seed.Venue1Pickleball.Id,
                UserId = seed.Member1.Id,
                PlayDate = yesterdayVn,
                StartHour = 14,
                PriceVnd = 140_000m,
                Status = BookingStatus.Confirmed
            },
            new Booking
            {
                BookingCode = "CB-TOD-CONF",
                CourtId = seed.Venue1Pickleball.Id,
                UserId = seed.Member1.Id,
                PlayDate = todayVn,
                StartHour = 14,
                PriceVnd = 140_000m,
                Status = BookingStatus.Confirmed
            },
            new Booking
            {
                BookingCode = "CB-TOD-CHK",
                CourtId = seed.Venue1Badminton.Id,
                UserId = seed.Member2.Id,
                PlayDate = todayVn,
                StartHour = 15,
                PriceVnd = 100_000m,
                Status = BookingStatus.CheckedIn
            },
            new Booking
            {
                BookingCode = "CB-TOM",
                CourtId = seed.Venue1Pickleball.Id,
                UserId = seed.Member1.Id,
                PlayDate = tomorrowVn,
                StartHour = 14,
                PriceVnd = 140_000m,
                Status = BookingStatus.Confirmed
            });
        await scope.Context.SaveChangesAsync();

        OwnerPortalDto yesterdayPortal = await scope.BookingService.GetOwnerPortalAsync(seed.Owner1.Id, seed.Venue1.Id, yesterdayVn);
        Assert.False(Assert.Single(yesterdayPortal.DateBookings).CanCheckIn);

        OwnerPortalDto tomorrowPortal = await scope.BookingService.GetOwnerPortalAsync(seed.Owner1.Id, seed.Venue1.Id, tomorrowVn);
        Assert.False(Assert.Single(tomorrowPortal.DateBookings).CanCheckIn);

        OwnerPortalDto todayPortal = await scope.BookingService.GetOwnerPortalAsync(seed.Owner1.Id, seed.Venue1.Id, todayVn);
        Assert.True(todayPortal.DateBookings.Single(b => b.BookingCode == "CB-TOD-CONF").CanCheckIn);
        Assert.False(todayPortal.DateBookings.Single(b => b.BookingCode == "CB-TOD-CHK").CanCheckIn);
    }

    [Fact]
    public async Task Rule3_MemberCancelCutoff_EnforcesTwoHoursForConfirmed_AllowsBoundaryHeldReleaseAndOwnerBypass()
    {
        // Start at 2026-11-20 03:00 UTC == 10:00 VN
        using var scope = new TestContextScope(new DateTimeOffset(2026, 11, 20, 3, 0, 0, TimeSpan.Zero));
        SeededTestData seed = await scope.SeedMinimalAsync();
        DateOnly todayVn = new(2026, 11, 20);

        Assert.Equal(2, BookingService.MemberCancelLeadHours);

        // Book 14:00 (starts at 14:00 VN) and 15:00 (starts at 15:00 VN) and 16:00
        await scope.BookingService.ToggleHoldSlotAsync(seed.Member1.Id, seed.Venue1Pickleball.Id, todayVn, 14);
        await scope.BookingService.ToggleHoldSlotAsync(seed.Member1.Id, seed.Venue1Pickleball.Id, todayVn, 15);
        await scope.BookingService.ToggleHoldSlotAsync(seed.Member1.Id, seed.Venue1Pickleball.Id, todayVn, 16);
        var confirmRes = await scope.BookingService.ConfirmMyHoldsAsync(seed.Member1.Id, seed.Venue1.Id);
        Assert.True(confirmRes.Succeeded);
        int b14 = confirmRes.Data!.Single(t => t.StartHour == 14).Id;
        int b15 = confirmRes.Data!.Single(t => t.StartHour == 15).Id;
        int b16 = confirmRes.Data!.Single(t => t.StartHour == 16).Id;

        // Advance to 12:00:00 VN -> exactly 2h00m before 14:00, 3h00m before 15:00
        scope.TimeProvider.SetUtcNow(new DateTimeOffset(2026, 11, 20, 5, 0, 0, TimeSpan.Zero));
        UserBookingsPageDto at1200 = await scope.BookingService.GetUserBookingsAsync(seed.Member1.Id);
        Assert.True(at1200.Upcoming.Single(t => t.Id == b14).CanCancel);
        Assert.True((await scope.BookingService.CancelBookingAsync(b14, seed.Member1.Id, UserRole.Member)).Succeeded);

        // Advance to 13:01:00 VN -> 1h59m before 15:00 (< 2h cutoff!)
        scope.TimeProvider.SetUtcNow(new DateTimeOffset(2026, 11, 20, 6, 1, 0, TimeSpan.Zero));
        UserBookingsPageDto at1301 = await scope.BookingService.GetUserBookingsAsync(seed.Member1.Id);
        Assert.False(at1301.Upcoming.Single(t => t.Id == b15).CanCancel);
        Assert.True(at1301.Upcoming.Single(t => t.Id == b16).CanCancel);

        var cutoffFail = await scope.BookingService.CancelBookingAsync(b15, seed.Member1.Id, UserRole.Member);
        Assert.False(cutoffFail.Succeeded);
        Assert.Equal(
            "Khách chỉ có thể tự hủy lịch trước giờ chơi ít nhất 2 tiếng. Vui lòng liên hệ trực tiếp Chủ sân để được hỗ trợ.",
            cutoffFail.ErrorMessage);

        // Confirming a slot held within 2h still succeeds, with CanCancel = false on the returned ticket
        await scope.BookingService.ToggleHoldSlotAsync(seed.Member2.Id, seed.Venue1Badminton.Id, todayVn, 14);
        var confirmWithin2h = await scope.BookingService.ConfirmMyHoldsAsync(seed.Member2.Id, seed.Venue1.Id);
        Assert.True(confirmWithin2h.Succeeded);
        Assert.False(confirmWithin2h.Data![0].CanCancel);

        // Releasing an unconfirmed Held slot via CancelBookingAsync is permitted even within 2h
        await scope.BookingService.ToggleHoldSlotAsync(seed.Member2.Id, seed.Venue1Badminton.Id, todayVn, 15);
        int held15Id = (await scope.BookingService.GetMyHeldSummaryAsync(seed.Member2.Id, seed.Venue1.Id)).Items[0].BookingId;
        var cancelHeld = await scope.BookingService.CancelBookingAsync(held15Id, seed.Member2.Id, UserRole.Member);
        Assert.True(cancelHeld.Succeeded);

        // Owner bypasses the 2-hour cutoff and can cancel Member1's b15
        var ownerCancelWithin2h = await scope.BookingService.CancelBookingAsync(b15, seed.Owner1.Id, UserRole.CourtOwner);
        Assert.True(ownerCancelWithin2h.Succeeded);

        // Advance to 16:00 VN (slot started) -> Member1 fails with past/started message on b16
        scope.TimeProvider.SetUtcNow(new DateTimeOffset(2026, 11, 20, 9, 0, 0, TimeSpan.Zero));
        var startedFail = await scope.BookingService.CancelBookingAsync(b16, seed.Member1.Id, UserRole.Member);
        Assert.False(startedFail.Succeeded);
        Assert.Equal("Đã đến hoặc quá giờ chơi, không thể tự hủy lịch.", startedFail.ErrorMessage);
    }

    [Fact]
    public async Task Rule4_RevenueSeparation_SeparatesCheckedInRealizedFromConfirmedExpectedAcrossOwnerAndAdmin()
    {
        using var scope = new TestContextScope(new DateTimeOffset(2026, 11, 20, 3, 0, 0, TimeSpan.Zero));
        SeededTestData seed = await scope.SeedMinimalAsync();
        DateOnly todayVn = new(2026, 11, 20);

        scope.Context.Bookings.AddRange(
            // Venue1: 1 CheckedIn (140k), 1 Confirmed (190k), 1 Cancelled (100k), 1 active Held (140k)
            new Booking
            {
                BookingCode = "CB-R4-1",
                CourtId = seed.Venue1Pickleball.Id,
                UserId = seed.Member1.Id,
                PlayDate = todayVn,
                StartHour = 8,
                PriceVnd = 140_000m,
                Status = BookingStatus.CheckedIn
            },
            new Booking
            {
                BookingCode = "CB-R4-2",
                CourtId = seed.Venue1Pickleball.Id,
                UserId = seed.Member2.Id,
                PlayDate = todayVn,
                StartHour = 18,
                PriceVnd = 190_000m,
                Status = BookingStatus.Confirmed
            },
            new Booking
            {
                BookingCode = "CB-R4-3",
                CourtId = seed.Venue1Badminton.Id,
                UserId = seed.Member1.Id,
                PlayDate = todayVn,
                StartHour = 9,
                PriceVnd = 100_000m,
                Status = BookingStatus.Cancelled
            },
            new Booking
            {
                BookingCode = "CB-R4-4",
                CourtId = seed.Venue1Pickleball.Id,
                UserId = seed.Member2.Id,
                PlayDate = todayVn,
                StartHour = 15,
                PriceVnd = 140_000m,
                Status = BookingStatus.Held,
                HoldExpiresAtUtc = scope.TimeProvider.GetUtcNow().UtcDateTime.AddMinutes(2)
            },
            // Venue2: 1 CheckedIn (130k), 1 Confirmed (175k)
            new Booking
            {
                BookingCode = "CB-R4-5",
                CourtId = seed.Venue2Pickleball.Id,
                UserId = seed.Member1.Id,
                PlayDate = todayVn,
                StartHour = 10,
                PriceVnd = 130_000m,
                Status = BookingStatus.CheckedIn
            },
            new Booking
            {
                BookingCode = "CB-R4-6",
                CourtId = seed.Venue2Pickleball.Id,
                UserId = seed.Member2.Id,
                PlayDate = todayVn,
                StartHour = 19,
                PriceVnd = 175_000m,
                Status = BookingStatus.Confirmed
            });
        await scope.Context.SaveChangesAsync();

        OwnerPortalDto owner1Portal = await scope.BookingService.GetOwnerPortalAsync(seed.Owner1.Id, seed.Venue1.Id, todayVn);
        Assert.Equal(140_000m, owner1Portal.TodayRevenueVnd);
        Assert.Equal(190_000m, owner1Portal.TodayExpectedRevenueVnd);
        Assert.Equal(330_000m, owner1Portal.TodayTotalBookedValueVnd);

        PlatformAdminDashboardDto dash = await scope.PlatformAdminService.GetDashboardAsync(todayVn);
        Assert.Equal(270_000m, dash.TodayRealizedRevenueVnd);
        Assert.Equal(365_000m, dash.TodayExpectedRevenueVnd);
        Assert.Equal(635_000m, dash.TodayPlatformGmvVnd);

        AdminVenueRowDto v1Row = Assert.Single(dash.AllVenues, v => v.Id == seed.Venue1.Id);
        Assert.Equal(140_000m, v1Row.TodayRevenueVnd);
        Assert.Equal(190_000m, v1Row.TodayExpectedRevenueVnd);

        AdminVenueRowDto v2Row = Assert.Single(dash.AllVenues, v => v.Id == seed.Venue2.Id);
        Assert.Equal(130_000m, v2Row.TodayRevenueVnd);
        Assert.Equal(175_000m, v2Row.TodayExpectedRevenueVnd);
    }

    [Fact]
    public async Task Rule5_AdvanceBookingHorizon_AllowsDay30_RejectsDay31()
    {
        using var scope = new TestContextScope(new DateTimeOffset(2026, 11, 20, 3, 0, 0, TimeSpan.Zero));
        SeededTestData seed = await scope.SeedMinimalAsync();
        DateOnly todayVn = new(2026, 11, 20);

        Assert.Equal(30, BookingService.MaxAdvanceBookingDays);

        var day30 = await scope.BookingService.ToggleHoldSlotAsync(
            seed.Member1.Id, seed.Venue1Pickleball.Id, todayVn.AddDays(30), 18);
        Assert.True(day30.Succeeded);

        var day31 = await scope.BookingService.ToggleHoldSlotAsync(
            seed.Member1.Id, seed.Venue1Pickleball.Id, todayVn.AddDays(31), 18);
        Assert.False(day31.Succeeded);
        Assert.Equal("Chỉ mở đặt lịch trước tối đa 30 ngày.", day31.ErrorMessage);
    }

    [Fact]
    public async Task Rule5_ConfirmMyHolds_RechecksPastOrStartedSlotAfterHoldWasCreated()
    {
        // Start at 2026-11-20 03:59 UTC == 10:59 VN
        using var scope = new TestContextScope(new DateTimeOffset(2026, 11, 20, 3, 59, 0, TimeSpan.Zero));
        SeededTestData seed = await scope.SeedMinimalAsync();
        DateOnly todayVn = new(2026, 11, 20);

        var hold11 = await scope.BookingService.ToggleHoldSlotAsync(seed.Member1.Id, seed.Venue1Pickleball.Id, todayVn, 11);
        Assert.True(hold11.Succeeded);

        // Advance 1 minute to 11:00 VN: hold TTL (2 min) has NOT expired, but slot 11:00 has started!
        scope.TimeProvider.Advance(TimeSpan.FromMinutes(1));

        var confirmRes = await scope.BookingService.ConfirmMyHoldsAsync(seed.Member1.Id, seed.Venue1.Id);
        Assert.False(confirmRes.Succeeded);
        Assert.Equal("Một hoặc nhiều khung giờ giữ chỗ đã bắt đầu hoặc trôi qua. Vui lòng chọn lại.", confirmRes.ErrorMessage);
    }

    [Fact]
    public async Task Rule5_BuildHeldSummary_FormatsDateDdMmForSingleAndMultipleHolds()
    {
        using var scope = new TestContextScope();
        SeededTestData seed = await scope.SeedMinimalAsync();

        var first = await scope.BookingService.ToggleHoldSlotAsync(seed.Member1.Id, seed.Venue1Pickleball.Id, new DateOnly(2026, 11, 22), 8);
        Assert.True(first.Succeeded);
        Assert.Equal("Sân Pickleball 1 · 22/11 (08:00 - 09:00)", first.Data!.MyHolds.SummaryText);

        var second = await scope.BookingService.ToggleHoldSlotAsync(seed.Member1.Id, seed.Venue1Badminton.Id, new DateOnly(2026, 11, 23), 18);
        Assert.True(second.Succeeded);
        Assert.Equal("Sân Pickleball 1 · 22/11 (08:00 - 09:00) và 1 khung giờ khác", second.Data!.MyHolds.SummaryText);
    }

    [Fact]
    public async Task Rule5_CourtPriceBounds_EnforcesRangeAndPeakNotLowerThanBase()
    {
        using var scope = new TestContextScope();
        SeededTestData seed = await scope.SeedMinimalAsync();

        // Base below 10,000
        var lowBase = await scope.CourtService.CreateAsync(seed.Owner1.Id, new CourtUpsertDto
        {
            VenueId = seed.Venue1.Id,
            Code = "PB-10",
            Name = "Sân Test",
            Surface = "Acrylic",
            BasePricePerHour = 9_000m,
            PeakPricePerHour = 50_000m
        });
        Assert.False(lowBase.Succeeded);
        Assert.Equal("Giá giờ thường phải từ 10.000đ đến 5.000.000đ.", lowBase.ErrorMessage);

        // Base above 5,000,000
        var highBase = await scope.CourtService.CreateAsync(seed.Owner1.Id, new CourtUpsertDto
        {
            VenueId = seed.Venue1.Id,
            Code = "PB-11",
            Name = "Sân Test",
            Surface = "Acrylic",
            BasePricePerHour = 5_100_000m,
            PeakPricePerHour = 5_100_000m
        });
        Assert.False(highBase.Succeeded);
        Assert.Equal("Giá giờ thường phải từ 10.000đ đến 5.000.000đ.", highBase.ErrorMessage);

        // Peak below 10,000
        var lowPeak = await scope.CourtService.CreateAsync(seed.Owner1.Id, new CourtUpsertDto
        {
            VenueId = seed.Venue1.Id,
            Code = "PB-12",
            Name = "Sân Test",
            Surface = "Acrylic",
            BasePricePerHour = 10_000m,
            PeakPricePerHour = 9_999m
        });
        Assert.False(lowPeak.Succeeded);
        Assert.Equal("Giá giờ vàng phải từ 10.000đ đến 5.000.000đ.", lowPeak.ErrorMessage);

        // Peak above 5,000,000
        var highPeak = await scope.CourtService.CreateAsync(seed.Owner1.Id, new CourtUpsertDto
        {
            VenueId = seed.Venue1.Id,
            Code = "PB-13",
            Name = "Sân Test",
            Surface = "Acrylic",
            BasePricePerHour = 100_000m,
            PeakPricePerHour = 5_000_001m
        });
        Assert.False(highPeak.Succeeded);
        Assert.Equal("Giá giờ vàng phải từ 10.000đ đến 5.000.000đ.", highPeak.ErrorMessage);

        // Peak lower than Base on Create and Update
        var peakBelowBase = await scope.CourtService.CreateAsync(seed.Owner1.Id, new CourtUpsertDto
        {
            VenueId = seed.Venue1.Id,
            Code = "PB-14",
            Name = "Sân Test",
            Surface = "Acrylic",
            BasePricePerHour = 150_000m,
            PeakPricePerHour = 140_000m
        });
        Assert.False(peakBelowBase.Succeeded);
        Assert.Equal("Giá giờ vàng không được thấp hơn giá giờ thường.", peakBelowBase.ErrorMessage);

        var updatePeakBelowBase = await scope.CourtService.UpdateAsync(seed.Owner1.Id, new CourtUpsertDto
        {
            Id = seed.Venue1Pickleball.Id,
            VenueId = seed.Venue1.Id,
            Code = seed.Venue1Pickleball.Code,
            Name = seed.Venue1Pickleball.Name,
            Surface = seed.Venue1Pickleball.Surface,
            BasePricePerHour = 200_000m,
            PeakPricePerHour = 180_000m
        });
        Assert.False(updatePeakBelowBase.Succeeded);
        Assert.Equal("Giá giờ vàng không được thấp hơn giá giờ thường.", updatePeakBelowBase.ErrorMessage);

        // Boundary min (10k/10k) and max (5M/5M) succeed
        var minBound = await scope.CourtService.CreateAsync(seed.Owner1.Id, new CourtUpsertDto
        {
            VenueId = seed.Venue1.Id,
            Code = "PB-MIN",
            Name = "Sân Min",
            Surface = "Acrylic",
            BasePricePerHour = 10_000m,
            PeakPricePerHour = 10_000m
        });
        Assert.True(minBound.Succeeded);

        var maxBound = await scope.CourtService.CreateAsync(seed.Owner1.Id, new CourtUpsertDto
        {
            VenueId = seed.Venue1.Id,
            Code = "PB-MAX",
            Name = "Sân Max",
            Surface = "Acrylic",
            BasePricePerHour = 5_000_000m,
            PeakPricePerHour = 5_000_000m
        });
        Assert.True(maxBound.Succeeded);
    }

    [Fact]
    public async Task Rule6_CourtToggleMaintenance_ExpiresActiveHoldsBroadcastsAndWarnsForUpcomingConfirmed()
    {
        // 2026-11-20 03:00 UTC == 10:00 VN
        using var scope = new TestContextScope(new DateTimeOffset(2026, 11, 20, 3, 0, 0, TimeSpan.Zero));
        SeededTestData seed = await scope.SeedMinimalAsync();
        DateOnly todayVn = new(2026, 11, 20);
        DateOnly tomorrowVn = todayVn.AddDays(1);

        // Active hold on Venue1Pickleball (19:00 today) + active hold on Venue1Badminton (to ensure no cross-court expiry)
        await scope.BookingService.ToggleHoldSlotAsync(seed.Member1.Id, seed.Venue1Pickleball.Id, todayVn, 19);
        await scope.BookingService.ToggleHoldSlotAsync(seed.Member2.Id, seed.Venue1Badminton.Id, todayVn, 19);

        // 2 upcoming Confirmed bookings (12:00 today, 08:00 tomorrow), 1 past/current Confirmed (10:00 today), 1 CheckedIn (07:00 today)
        scope.Context.Bookings.AddRange(
            new Booking
            {
                BookingCode = "CB-UP-1",
                CourtId = seed.Venue1Pickleball.Id,
                UserId = seed.Member1.Id,
                PlayDate = todayVn,
                StartHour = 12,
                PriceVnd = 140_000m,
                Status = BookingStatus.Confirmed
            },
            new Booking
            {
                BookingCode = "CB-UP-2",
                CourtId = seed.Venue1Pickleball.Id,
                UserId = seed.Member2.Id,
                PlayDate = tomorrowVn,
                StartHour = 8,
                PriceVnd = 140_000m,
                Status = BookingStatus.Confirmed
            },
            new Booking
            {
                BookingCode = "CB-CUR-PAST",
                CourtId = seed.Venue1Pickleball.Id,
                UserId = seed.Member1.Id,
                PlayDate = todayVn,
                StartHour = 10,
                PriceVnd = 140_000m,
                Status = BookingStatus.Confirmed
            },
            new Booking
            {
                BookingCode = "CB-CHKIN",
                CourtId = seed.Venue1Pickleball.Id,
                UserId = seed.Member1.Id,
                PlayDate = todayVn,
                StartHour = 7,
                PriceVnd = 140_000m,
                Status = BookingStatus.CheckedIn
            });
        await scope.Context.SaveChangesAsync();
        scope.Notifier.Clear();

        ServiceResult<Court> res = await scope.CourtService.ToggleActiveAsync(seed.Owner1.Id, seed.Venue1Pickleball.Id);
        Assert.True(res.Succeeded);
        Assert.False(res.Data!.IsActive);
        Assert.Equal(
            "Lưu ý: Sân này đang có 2 đơn đã xác nhận sắp tới, vui lòng chủ động liên hệ khách hàng.",
            res.WarningMessage);

        SlotUpdateBroadcastDto expiredBroadcast = Assert.Single(scope.Notifier.SlotUpdates);
        Assert.Equal(seed.Venue1Pickleball.Id, expiredBroadcast.CourtId);
        Assert.Equal(BookingStatus.Expired, expiredBroadcast.Status);
        Assert.False(expiredBroadcast.IsCourtActive);
        Assert.Null(expiredBroadcast.HoldExpiresAtUtc);

        // Venue1Badminton hold must remain intact
        HeldSummaryDto member2BadmintonHold = await scope.BookingService.GetMyHeldSummaryAsync(seed.Member2.Id, seed.Venue1.Id);
        Assert.Equal(1, member2BadmintonHold.Count);

        // Turning Venue1Badminton off (0 upcoming confirmed) has null WarningMessage
        ServiceResult<Court> resNoWarn = await scope.CourtService.ToggleActiveAsync(seed.Owner1.Id, seed.Venue1Badminton.Id);
        Assert.True(resNoWarn.Succeeded);
        Assert.Null(resNoWarn.WarningMessage);
    }

    [Fact]
    public async Task Rule6_CourtUpdateSetInactive_ExpiresActiveHoldsAndWarnsForUpcomingConfirmed()
    {
        using var scope = new TestContextScope(new DateTimeOffset(2026, 11, 20, 3, 0, 0, TimeSpan.Zero));
        SeededTestData seed = await scope.SeedMinimalAsync();
        DateOnly tomorrowVn = new(2026, 11, 21);

        await scope.BookingService.ToggleHoldSlotAsync(seed.Member1.Id, seed.Venue1Pickleball.Id, tomorrowVn, 18);
        scope.Context.Bookings.Add(new Booking
        {
            BookingCode = "CB-UPD-CONF",
            CourtId = seed.Venue1Pickleball.Id,
            UserId = seed.Member2.Id,
            PlayDate = tomorrowVn,
            StartHour = 20,
            PriceVnd = 190_000m,
            Status = BookingStatus.Confirmed
        });
        await scope.Context.SaveChangesAsync();
        scope.Notifier.Clear();

        ServiceResult<Court> updateRes = await scope.CourtService.UpdateAsync(seed.Owner1.Id, new CourtUpsertDto
        {
            Id = seed.Venue1Pickleball.Id,
            VenueId = seed.Venue1.Id,
            Code = seed.Venue1Pickleball.Code,
            Name = seed.Venue1Pickleball.Name,
            SportType = seed.Venue1Pickleball.SportType,
            IsIndoor = seed.Venue1Pickleball.IsIndoor,
            Surface = seed.Venue1Pickleball.Surface,
            BasePricePerHour = seed.Venue1Pickleball.BasePricePerHour,
            PeakPricePerHour = seed.Venue1Pickleball.PeakPricePerHour,
            IsActive = false,
            DisplayOrder = 1
        });

        Assert.True(updateRes.Succeeded);
        Assert.False(updateRes.Data!.IsActive);
        Assert.Equal(
            "Lưu ý: Sân này đang có 1 đơn đã xác nhận sắp tới, vui lòng chủ động liên hệ khách hàng.",
            updateRes.WarningMessage);
        SlotUpdateBroadcastDto expired = Assert.Single(scope.Notifier.SlotUpdates);
        Assert.Equal(BookingStatus.Expired, expired.Status);
        Assert.Equal(0, (await scope.BookingService.GetMyHeldSummaryAsync(seed.Member1.Id, seed.Venue1.Id)).Count);
    }

    [Fact]
    public async Task Rule6_VenueUpdateHours_ExpiresOnlyOutOfRangeHoldsAndBroadcasts()
    {
        using var scope = new TestContextScope();
        SeededTestData seed = await scope.SeedMinimalAsync();
        DateOnly futureDate = new(2026, 11, 22);

        await scope.BookingService.ToggleHoldSlotAsync(seed.Member1.Id, seed.Venue1Pickleball.Id, futureDate, 6);
        await scope.BookingService.ToggleHoldSlotAsync(seed.Member1.Id, seed.Venue1Pickleball.Id, futureDate, 10);
        await scope.BookingService.ToggleHoldSlotAsync(seed.Member1.Id, seed.Venue1Pickleball.Id, futureDate, 21);
        scope.Notifier.Clear();

        ServiceResult<Venue> updateRes = await scope.VenueService.UpdateVenueAsync(
            seed.Owner1.Id,
            new VenueUpsertDto
            {
                Id = seed.Venue1.Id,
                Name = seed.Venue1.Name,
                District = seed.Venue1.District,
                Address = seed.Venue1.Address,
                PhoneNumber = seed.Venue1.PhoneNumber,
                Description = seed.Venue1.Description,
                AccentColor = seed.Venue1.AccentColor,
                OpenHour = 8,
                CloseHour = 20
            });

        Assert.True(updateRes.Succeeded);
        Assert.Equal(2, scope.Notifier.SlotUpdates.Count);
        Assert.Contains(scope.Notifier.SlotUpdates, u => u.StartHour == 6 && u.Status == BookingStatus.Expired);
        Assert.Contains(scope.Notifier.SlotUpdates, u => u.StartHour == 21 && u.Status == BookingStatus.Expired);
        Assert.DoesNotContain(scope.Notifier.SlotUpdates, u => u.StartHour == 10);

        HeldSummaryDto remaining = await scope.BookingService.GetMyHeldSummaryAsync(seed.Member1.Id, seed.Venue1.Id);
        HeldItemDto kept = Assert.Single(remaining.Items);
        Assert.Equal(10, kept.StartHour);
    }

    [Fact]
    public async Task Rule6_AdminSuspendVenue_ExpiresAllActiveHoldsOnThatVenueOnlyAndBroadcasts()
    {
        using var scope = new TestContextScope();
        SeededTestData seed = await scope.SeedMinimalAsync();
        DateOnly futureDate = new(2026, 11, 22);

        await scope.BookingService.ToggleHoldSlotAsync(seed.Member1.Id, seed.Venue1Pickleball.Id, futureDate, 18);
        await scope.BookingService.ToggleHoldSlotAsync(seed.Member2.Id, seed.Venue1Badminton.Id, futureDate, 19);
        await scope.BookingService.ToggleHoldSlotAsync(seed.Member1.Id, seed.Venue2Pickleball.Id, futureDate, 18);
        scope.Notifier.Clear();

        ServiceResult<Venue> suspendRes = await scope.PlatformAdminService.SuspendVenueAsync(seed.Venue1.Id);
        Assert.True(suspendRes.Succeeded);
        Assert.Equal(VenueStatus.Suspended, suspendRes.Data!.Status);

        Assert.Equal(2, scope.Notifier.SlotUpdates.Count);
        Assert.All(scope.Notifier.SlotUpdates, u =>
        {
            Assert.Equal(seed.Venue1.Id, u.VenueId);
            Assert.Equal(BookingStatus.Expired, u.Status);
        });

        Assert.Equal(0, (await scope.BookingService.GetMyHeldSummaryAsync(seed.Member1.Id, seed.Venue1.Id)).Count);
        Assert.Equal(0, (await scope.BookingService.GetMyHeldSummaryAsync(seed.Member2.Id, seed.Venue1.Id)).Count);
        Assert.Equal(1, (await scope.BookingService.GetMyHeldSummaryAsync(seed.Member1.Id, seed.Venue2.Id)).Count);

        int expiredCount = await scope.Context.Bookings.CountAsync(b => b.Status == BookingStatus.Expired);
        Assert.Equal(2, expiredCount);
    }

    [Fact]
    public async Task V5_AdminSeparationOfDuties_BlocksBookingVenueAndCourtOperations()
    {
        using var scope = new TestContextScope(new DateTimeOffset(2026, 11, 20, 3, 0, 0, TimeSpan.Zero)); // 10:00 VN
        SeededTestData seed = await scope.SeedMinimalAsync();
        DateOnly todayVn = new(2026, 11, 20);

        // Seed a Confirmed booking today and an active Held slot for Member1 on Venue1
        await scope.BookingService.ToggleHoldSlotAsync(seed.Member1.Id, seed.Venue1Pickleball.Id, todayVn, 15);
        var confirmedRes = await scope.BookingService.ConfirmMyHoldsAsync(seed.Member1.Id, seed.Venue1.Id);
        Assert.True(confirmedRes.Succeeded);
        int confirmedBookingId = confirmedRes.Data![0].Id;

        await scope.BookingService.ToggleHoldSlotAsync(seed.Member1.Id, seed.Venue1Badminton.Id, todayVn, 16);
        int heldBookingId = (await scope.BookingService.GetMyHeldSummaryAsync(seed.Member1.Id, seed.Venue1.Id)).Items[0].BookingId;

        // 1. Admin cannot CheckInBookingAsync
        var adminCheckIn = await scope.BookingService.CheckInBookingAsync(confirmedBookingId, seed.Admin.Id, UserRole.Admin);
        Assert.False(adminCheckIn.Succeeded);
        Assert.Equal(
            "Quản trị sàn chỉ giám sát hệ thống, không trực tiếp check-in thu tiền tại sân của đối tác.",
            adminCheckIn.ErrorMessage);

        // 2. Admin cannot CancelBookingAsync (neither Confirmed nor Held)
        var adminCancelConfirmed = await scope.BookingService.CancelBookingAsync(confirmedBookingId, seed.Admin.Id, UserRole.Admin);
        Assert.False(adminCancelConfirmed.Succeeded);
        Assert.Equal(
            "Quản trị sàn không trực tiếp hủy đơn lẻ của đối tác; vui lòng dùng chức năng Tạm khóa cụm sân nếu vi phạm.",
            adminCancelConfirmed.ErrorMessage);

        var adminCancelHeld = await scope.BookingService.CancelBookingAsync(heldBookingId, seed.Admin.Id, UserRole.Admin);
        Assert.False(adminCancelHeld.Succeeded);
        Assert.Equal(
            "Quản trị sàn không trực tiếp hủy đơn lẻ của đối tác; vui lòng dùng chức năng Tạm khóa cụm sân nếu vi phạm.",
            adminCancelHeld.ErrorMessage);

        // 3. Admin cannot ToggleHoldSlotAsync or ConfirmMyHoldsAsync
        var adminHold = await scope.BookingService.ToggleHoldSlotAsync(seed.Admin.Id, seed.Venue1Pickleball.Id, todayVn, 18);
        Assert.False(adminHold.Succeeded);
        Assert.Equal(
            "Tài khoản Quản trị sàn dùng để điều hành hệ thống, không tham gia đặt lịch sân.",
            adminHold.ErrorMessage);

        var adminConfirm = await scope.BookingService.ConfirmMyHoldsAsync(seed.Admin.Id, seed.Venue1.Id);
        Assert.False(adminConfirm.Succeeded);
        Assert.Equal(
            "Tài khoản Quản trị sàn dùng để điều hành hệ thống, không tham gia đặt lịch sân.",
            adminConfirm.ErrorMessage);

        // 4. Admin and Member cannot VenueService.CreateVenueAsync or UpdateVenueAsync
        const string expectedVenueRoleMsg = "Chỉ tài khoản Chủ cụm sân mới được đăng ký / quản lý cụm sân.";
        var venueDto = new VenueUpsertDto
        {
            Id = seed.Venue1.Id,
            Name = "Sân Admin Test",
            District = "Quận 1",
            Address = "10 Lê Lợi",
            PhoneNumber = "0902111222",
            OpenHour = 6,
            CloseHour = 22
        };

        var adminCreateVenue = await scope.VenueService.CreateVenueAsync(seed.Admin.Id, venueDto);
        Assert.False(adminCreateVenue.Succeeded);
        Assert.Equal(expectedVenueRoleMsg, adminCreateVenue.ErrorMessage);

        var adminUpdateVenue = await scope.VenueService.UpdateVenueAsync(seed.Admin.Id, venueDto);
        Assert.False(adminUpdateVenue.Succeeded);
        Assert.Equal(expectedVenueRoleMsg, adminUpdateVenue.ErrorMessage);

        var memberCreateVenue = await scope.VenueService.CreateVenueAsync(seed.Member1.Id, venueDto);
        Assert.False(memberCreateVenue.Succeeded);
        Assert.Equal(expectedVenueRoleMsg, memberCreateVenue.ErrorMessage);

        var memberUpdateVenue = await scope.VenueService.UpdateVenueAsync(seed.Member1.Id, venueDto);
        Assert.False(memberUpdateVenue.Succeeded);
        Assert.Equal(expectedVenueRoleMsg, memberUpdateVenue.ErrorMessage);

        // 5. Admin and Member cannot CourtService.CreateAsync, UpdateAsync, or ToggleActiveAsync
        const string expectedAdminCourtMsg = "Quản trị sàn không trực tiếp chỉnh sửa sân con của đối tác.";
        var courtDto = new CourtUpsertDto
        {
            Id = seed.Venue1Pickleball.Id,
            VenueId = seed.Venue1.Id,
            Code = "PB-ADM",
            Name = "Sân Admin",
            SportType = SportType.Pickleball,
            Surface = "Acrylic",
            BasePricePerHour = 120_000m,
            PeakPricePerHour = 160_000m
        };

        var adminCreateCourt = await scope.CourtService.CreateAsync(seed.Admin.Id, courtDto);
        Assert.False(adminCreateCourt.Succeeded);
        Assert.Equal(expectedAdminCourtMsg, adminCreateCourt.ErrorMessage);

        var adminUpdateCourt = await scope.CourtService.UpdateAsync(seed.Admin.Id, courtDto);
        Assert.False(adminUpdateCourt.Succeeded);
        Assert.Equal(expectedAdminCourtMsg, adminUpdateCourt.ErrorMessage);

        var adminToggleCourt = await scope.CourtService.ToggleActiveAsync(seed.Admin.Id, seed.Venue1Pickleball.Id);
        Assert.False(adminToggleCourt.Succeeded);
        Assert.Equal(expectedAdminCourtMsg, adminToggleCourt.ErrorMessage);

        Assert.False((await scope.CourtService.CreateAsync(seed.Member1.Id, courtDto)).Succeeded);
        Assert.False((await scope.CourtService.UpdateAsync(seed.Member1.Id, courtDto)).Succeeded);
        Assert.False((await scope.CourtService.ToggleActiveAsync(seed.Member1.Id, seed.Venue1Pickleball.Id)).Succeeded);

        // 6. CourtOwner creating a venue ALWAYS starts at PendingApproval
        var ownerCreateVenue = await scope.VenueService.CreateVenueAsync(seed.Owner1.Id, venueDto);
        Assert.True(ownerCreateVenue.Succeeded);
        Assert.Equal(VenueStatus.PendingApproval, ownerCreateVenue.Data!.Status);
    }

    [Fact]
    public async Task V5_CourtOwnerAntiSelfDealing_BlocksOwnVenueBooking_AllowsOtherOwnersVenue()
    {
        // Start at 2026-11-20 03:00 UTC == 10:00 VN
        using var scope = new TestContextScope(new DateTimeOffset(2026, 11, 20, 3, 0, 0, TimeSpan.Zero));
        SeededTestData seed = await scope.SeedMinimalAsync();
        DateOnly todayVn = new(2026, 11, 20);
        const string expectedSelfDealingMsg =
            "Bạn là chủ sở hữu cụm sân này, không thể tự đặt lịch trên sân của chính mình.";

        // Owner1 cannot hold or confirm on their own Venue1
        var selfHold = await scope.BookingService.ToggleHoldSlotAsync(
            seed.Owner1.Id, seed.Venue1Pickleball.Id, todayVn, 18);
        Assert.False(selfHold.Succeeded);
        Assert.Equal(expectedSelfDealingMsg, selfHold.ErrorMessage);

        var selfConfirm = await scope.BookingService.ConfirmMyHoldsAsync(seed.Owner1.Id, seed.Venue1.Id);
        Assert.False(selfConfirm.Succeeded);
        Assert.Equal(expectedSelfDealingMsg, selfConfirm.ErrorMessage);

        // ScheduleMatrix exposes OwnerId so UI can render read-only oversight notice for owning CourtOwner
        ScheduleMatrixDto? matrix = await scope.BookingService.GetScheduleMatrixAsync(
            seed.Venue1.Id, todayVn, null, seed.Owner1.Id);
        Assert.NotNull(matrix);
        Assert.Equal(seed.Owner1.Id, matrix!.OwnerId);

        // Owner1 CAN hold & confirm slots at Owner2's Venue2 (11:00 and 19:00 today)
        var crossHold11 = await scope.BookingService.ToggleHoldSlotAsync(
            seed.Owner1.Id, seed.Venue2Pickleball.Id, todayVn, 11);
        Assert.True(crossHold11.Succeeded);

        var crossHold19 = await scope.BookingService.ToggleHoldSlotAsync(
            seed.Owner1.Id, seed.Venue2Pickleball.Id, todayVn, 19);
        Assert.True(crossHold19.Succeeded);

        var crossConfirm = await scope.BookingService.ConfirmMyHoldsAsync(seed.Owner1.Id, seed.Venue2.Id);
        Assert.True(crossConfirm.Succeeded);
        Assert.Equal(2, crossConfirm.Data!.Count);

        int b11 = crossConfirm.Data.Single(b => b.StartHour == 11).Id;
        int b19 = crossConfirm.Data.Single(b => b.StartHour == 19).Id;

        // On Owner2's venue, Owner1 is treated as a regular customer for cancellation:
        // 11:00 is < 2h away (10:00 VN -> 1h away) -> Owner1 self-cancel fails, Owner2 venue-owner cancel succeeds!
        var owner1CancelWithin2h = await scope.BookingService.CancelBookingAsync(b11, seed.Owner1.Id, UserRole.CourtOwner);
        Assert.False(owner1CancelWithin2h.Succeeded);

        // 19:00 is >= 2h away -> Owner1 self-cancel succeeds
        var owner1CancelEarly = await scope.BookingService.CancelBookingAsync(b19, seed.Owner1.Id, UserRole.CourtOwner);
        Assert.True(owner1CancelEarly.Succeeded);

        // Owner2 (actual owner of Venue2) can cancel b11 and check in another Venue2 booking, while Owner1 cannot check in Venue2
        var owner2Cancel = await scope.BookingService.CancelBookingAsync(b11, seed.Owner2.Id, UserRole.CourtOwner);
        Assert.True(owner2Cancel.Succeeded);
    }

    [Fact]
    public async Task V5_RoleSpoofingGuards_EnforcePersistedDatabaseRoleOnCheckInAndCancel()
    {
        using var scope = new TestContextScope(new DateTimeOffset(2026, 11, 20, 3, 0, 0, TimeSpan.Zero));
        SeededTestData seed = await scope.SeedMinimalAsync();
        DateOnly todayVn = new(2026, 11, 20);

        await scope.BookingService.ToggleHoldSlotAsync(seed.Member2.Id, seed.Venue1Pickleball.Id, todayVn, 15);
        var confirmed = await scope.BookingService.ConfirmMyHoldsAsync(seed.Member2.Id, seed.Venue1.Id);
        Assert.True(confirmed.Succeeded);
        int bookingId = confirmed.Data![0].Id;

        // Member1 spoofing actorRole = CourtOwner is still rejected for CheckIn and cross-user Cancel
        var memberSpoofCheckIn = await scope.BookingService.CheckInBookingAsync(bookingId, seed.Member1.Id, UserRole.CourtOwner);
        Assert.False(memberSpoofCheckIn.Succeeded);

        var memberSpoofCancel = await scope.BookingService.CancelBookingAsync(bookingId, seed.Member1.Id, UserRole.CourtOwner);
        Assert.False(memberSpoofCancel.Succeeded);

        // Admin spoofing actorRole = CourtOwner is still rejected with the Admin separation-of-duties message
        var adminSpoofCheckIn = await scope.BookingService.CheckInBookingAsync(bookingId, seed.Admin.Id, UserRole.CourtOwner);
        Assert.False(adminSpoofCheckIn.Succeeded);
        Assert.Equal(
            "Quản trị sàn chỉ giám sát hệ thống, không trực tiếp check-in thu tiền tại sân của đối tác.",
            adminSpoofCheckIn.ErrorMessage);

        var adminSpoofCancel = await scope.BookingService.CancelBookingAsync(bookingId, seed.Admin.Id, UserRole.CourtOwner);
        Assert.False(adminSpoofCancel.Succeeded);
        Assert.Equal(
            "Quản trị sàn không trực tiếp hủy đơn lẻ của đối tác; vui lòng dùng chức năng Tạm khóa cụm sân nếu vi phạm.",
            adminSpoofCancel.ErrorMessage);
    }

    [Fact]
    public async Task V5_CourtOwnerCrossVenueBooking_AllowsHeldReleaseAndBlocksCrossOwnerCheckIn()
    {
        using var scope = new TestContextScope(new DateTimeOffset(2026, 11, 20, 3, 0, 0, TimeSpan.Zero)); // 10:00 VN
        SeededTestData seed = await scope.SeedMinimalAsync();
        DateOnly todayVn = new(2026, 11, 20);

        // Owner1 holds 11:00 at Owner2's Venue2 (< 2h away), releases Held slot via CancelBookingAsync -> allowed
        var hold11 = await scope.BookingService.ToggleHoldSlotAsync(seed.Owner1.Id, seed.Venue2Pickleball.Id, todayVn, 11);
        Assert.True(hold11.Succeeded);
        int holdId = hold11.Data!.UpdatedSlot.BookingId!.Value;
        var releaseHeld = await scope.BookingService.CancelBookingAsync(holdId, seed.Owner1.Id, UserRole.CourtOwner);
        Assert.True(releaseHeld.Succeeded);

        // Owner1 books 15:00 at Venue2 -> Owner1 CANNOT check-in their own booking at Venue2, only Owner2 can!
        await scope.BookingService.ToggleHoldSlotAsync(seed.Owner1.Id, seed.Venue2Pickleball.Id, todayVn, 15);
        var confirm15 = await scope.BookingService.ConfirmMyHoldsAsync(seed.Owner1.Id, seed.Venue2.Id);
        Assert.True(confirm15.Succeeded);
        int b15 = confirm15.Data![0].Id;

        var owner1CheckInAttempt = await scope.BookingService.CheckInBookingAsync(b15, seed.Owner1.Id, UserRole.CourtOwner);
        Assert.False(owner1CheckInAttempt.Succeeded);

        var owner2CheckIn = await scope.BookingService.CheckInBookingAsync(b15, seed.Owner2.Id, UserRole.CourtOwner);
        Assert.True(owner2CheckIn.Succeeded);
        Assert.Equal(BookingStatus.CheckedIn, owner2CheckIn.Data!.Status);
    }
}
