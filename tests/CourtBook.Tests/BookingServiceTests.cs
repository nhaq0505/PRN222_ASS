using CourtBook.BusinessObjects.Entities;
using CourtBook.BusinessObjects.Enums;
using CourtBook.Services.Models;
using Microsoft.EntityFrameworkCore;

namespace CourtBook.Tests;

public class BookingServiceTests
{
    [Fact]
    public async Task ToggleHoldSlot_HoldReleaseRehold_UpdatesStateAndEmitsVenueScopedRealtime()
    {
        using var scope = new TestContextScope();
        SeededTestData seed = await scope.SeedMinimalAsync();
        DateOnly futureDate = new(2026, 11, 22);

        var holdRes = await scope.BookingService.ToggleHoldSlotAsync(seed.Member1.Id, seed.Venue1Pickleball.Id, futureDate, 18);
        Assert.True(holdRes.Succeeded);
        Assert.Equal("held", holdRes.Data!.Action);
        Assert.Equal(seed.Venue1.Id, holdRes.Data.UpdatedSlot.VenueId);
        Assert.Equal(1, holdRes.Data.MyHolds.Count);
        Assert.Equal(190_000m, holdRes.Data.MyHolds.TotalPriceVnd);
        Assert.Single(scope.Notifier.SlotUpdates);
        Assert.Equal(BookingStatus.Held, scope.Notifier.SlotUpdates[0].Status);

        var releaseRes = await scope.BookingService.ToggleHoldSlotAsync(seed.Member1.Id, seed.Venue1Pickleball.Id, futureDate, 18);
        Assert.True(releaseRes.Succeeded);
        Assert.Equal("released", releaseRes.Data!.Action);
        Assert.Equal(0, releaseRes.Data.MyHolds.Count);
        Assert.Equal(2, scope.Notifier.SlotUpdates.Count);
        Assert.Equal(BookingStatus.Cancelled, scope.Notifier.SlotUpdates[1].Status);

        var reholdRes = await scope.BookingService.ToggleHoldSlotAsync(seed.Member1.Id, seed.Venue1Pickleball.Id, futureDate, 18);
        Assert.True(reholdRes.Succeeded);
        Assert.Equal("held", reholdRes.Data!.Action);
        Assert.Equal(1, reholdRes.Data.MyHolds.Count);
    }

    [Fact]
    public async Task ToggleHoldSlot_BlocksOtherUserUntilHoldExpires_ThenAllowsOtherUser()
    {
        using var scope = new TestContextScope();
        SeededTestData seed = await scope.SeedMinimalAsync();
        DateOnly futureDate = new(2026, 11, 22);

        var holdUser1 = await scope.BookingService.ToggleHoldSlotAsync(seed.Member1.Id, seed.Venue1Pickleball.Id, futureDate, 19);
        Assert.True(holdUser1.Succeeded);

        var blockedUser2 = await scope.BookingService.ToggleHoldSlotAsync(seed.Member2.Id, seed.Venue1Pickleball.Id, futureDate, 19);
        Assert.False(blockedUser2.Succeeded);

        // Advance past the 2-minute TTL
        scope.TimeProvider.Advance(TimeSpan.FromMinutes(2) + TimeSpan.FromSeconds(5));

        var allowedUser2 = await scope.BookingService.ToggleHoldSlotAsync(seed.Member2.Id, seed.Venue1Pickleball.Id, futureDate, 19);
        Assert.True(allowedUser2.Succeeded);
        Assert.Equal("held", allowedUser2.Data!.Action);
    }

    [Fact]
    public async Task ToggleHoldSlot_ConcurrentRaceOnSameSlot_ExactlyOneSucceeds()
    {
        using var scope = new TestContextScope();
        SeededTestData seed = await scope.SeedMinimalAsync();
        DateOnly futureDate = new(2026, 11, 22);

        Task<ServiceResult<ToggleHoldResponseDto>> t1 =
            scope.BookingService.ToggleHoldSlotAsync(seed.Member1.Id, seed.Venue1Pickleball.Id, futureDate, 17);
        Task<ServiceResult<ToggleHoldResponseDto>> t2 =
            scope.BookingService.ToggleHoldSlotAsync(seed.Member2.Id, seed.Venue1Pickleball.Id, futureDate, 17);

        ServiceResult<ToggleHoldResponseDto>[] results = await Task.WhenAll(t1, t2);

        Assert.Single(results, r => r.Succeeded);
        Assert.Single(results, r => !r.Succeeded);
    }

    [Fact]
    public async Task PeakVsBasePricing_AndConfirmMyHolds_TransitionsToConfirmedTicketsWithVenueInfo()
    {
        using var scope = new TestContextScope();
        SeededTestData seed = await scope.SeedMinimalAsync();
        DateOnly futureDate = new(2026, 11, 22);

        // 08:00 = Base (140,000), 18:00 = Peak (190,000)
        var holdBase = await scope.BookingService.ToggleHoldSlotAsync(seed.Member1.Id, seed.Venue1Pickleball.Id, futureDate, 8);
        var holdPeak = await scope.BookingService.ToggleHoldSlotAsync(seed.Member1.Id, seed.Venue1Pickleball.Id, futureDate, 18);

        Assert.True(holdBase.Succeeded);
        Assert.True(holdPeak.Succeeded);
        Assert.Equal(2, holdPeak.Data!.MyHolds.Count);
        Assert.Equal(330_000m, holdPeak.Data.MyHolds.TotalPriceVnd);

        scope.Notifier.Clear();
        ServiceResult<IReadOnlyList<BookingTicketDto>> confirmRes =
            await scope.BookingService.ConfirmMyHoldsAsync(seed.Member1.Id, seed.Venue1.Id, "Thuê thêm bóng thi đấu");

        Assert.True(confirmRes.Succeeded);
        Assert.Equal(2, confirmRes.Data!.Count);
        Assert.All(confirmRes.Data, t =>
        {
            Assert.Equal(BookingStatus.Confirmed, t.Status);
            Assert.Equal("Thuê thêm bóng thi đấu", t.Notes);
            Assert.Equal(seed.Venue1.Id, t.VenueId);
            Assert.Equal(seed.Venue1.Name, t.VenueName);
            Assert.Equal(seed.Venue1.District, t.VenueDistrict);
            Assert.Equal(seed.Venue1.Address, t.VenueAddress);
        });
        Assert.Equal(2, scope.Notifier.SlotUpdates.Count);
        Assert.All(scope.Notifier.SlotUpdates, u =>
        {
            Assert.Equal(BookingStatus.Confirmed, u.Status);
            Assert.Equal(seed.Venue1.Id, u.VenueId);
        });

        HeldSummaryDto remainingHolds = await scope.BookingService.GetMyHeldSummaryAsync(seed.Member1.Id, seed.Venue1.Id);
        Assert.Equal(0, remainingHolds.Count);

        ScheduleMatrixDto? matrixForUser2 = await scope.BookingService.GetScheduleMatrixAsync(
            seed.Venue1.Id, futureDate, null, seed.Member2.Id);
        Assert.NotNull(matrixForUser2);
        CourtRowDto row = Assert.Single(matrixForUser2!.Courts, c => c.CourtId == seed.Venue1Pickleball.Id);
        Assert.Equal(SlotVisualState.BookedByOther, row.Slots.First(s => s.StartHour == 8).State);
        Assert.Equal(SlotVisualState.BookedByOther, row.Slots.First(s => s.StartHour == 18).State);
    }

    [Fact]
    public async Task MultiVenueHoldIsolation_ConfirmingVenue1DoesNotConfirmVenue2Holds()
    {
        using var scope = new TestContextScope();
        SeededTestData seed = await scope.SeedMinimalAsync();
        DateOnly futureDate = new(2026, 11, 22);

        var h1 = await scope.BookingService.ToggleHoldSlotAsync(seed.Member1.Id, seed.Venue1Pickleball.Id, futureDate, 17);
        var h2 = await scope.BookingService.ToggleHoldSlotAsync(seed.Member1.Id, seed.Venue2Pickleball.Id, futureDate, 18);

        Assert.True(h1.Succeeded);
        Assert.True(h2.Succeeded);
        Assert.Equal(1, (await scope.BookingService.GetMyHeldSummaryAsync(seed.Member1.Id, seed.Venue1.Id)).Count);
        Assert.Equal(1, (await scope.BookingService.GetMyHeldSummaryAsync(seed.Member1.Id, seed.Venue2.Id)).Count);

        var confirmVenue1 = await scope.BookingService.ConfirmMyHoldsAsync(seed.Member1.Id, seed.Venue1.Id);
        Assert.True(confirmVenue1.Succeeded);
        Assert.Single(confirmVenue1.Data!);
        Assert.Equal(seed.Venue1.Id, confirmVenue1.Data![0].VenueId);

        // Venue1 holds cleared, Venue2 hold remains Held
        Assert.Equal(0, (await scope.BookingService.GetMyHeldSummaryAsync(seed.Member1.Id, seed.Venue1.Id)).Count);
        HeldSummaryDto venue2Holds = await scope.BookingService.GetMyHeldSummaryAsync(seed.Member1.Id, seed.Venue2.Id);
        Assert.Equal(1, venue2Holds.Count);
        Assert.Equal(seed.Venue2.Id, venue2Holds.Items[0].VenueId);
    }

    [Fact]
    public async Task ToggleHoldSlot_EnforcesMaxSixHoldsPerVenue_AndRespectsVenueOperatingHours()
    {
        using var scope = new TestContextScope();
        SeededTestData seed = await scope.SeedMinimalAsync();
        DateOnly futureDate = new(2026, 11, 22);

        for (int hour = 6; hour <= 11; hour++)
        {
            var res = await scope.BookingService.ToggleHoldSlotAsync(seed.Member1.Id, seed.Venue1Pickleball.Id, futureDate, hour);
            Assert.True(res.Succeeded, $"Hour {hour} hold should succeed.");
        }

        var seventh = await scope.BookingService.ToggleHoldSlotAsync(seed.Member1.Id, seed.Venue1Pickleball.Id, futureDate, 12);
        Assert.False(seventh.Succeeded);

        // Holding at Venue2 is isolated from Venue1's 6 holds
        var venue2Hold = await scope.BookingService.ToggleHoldSlotAsync(seed.Member1.Id, seed.Venue2Pickleball.Id, futureDate, 12);
        Assert.True(venue2Hold.Succeeded);

        // Out of Venue1 operating hours (CloseHour == 22 -> 22:00 cannot be booked)
        var afterClose = await scope.BookingService.ToggleHoldSlotAsync(seed.Member2.Id, seed.Venue1Pickleball.Id, futureDate, 22);
        Assert.False(afterClose.Succeeded);
    }

    [Fact]
    public async Task PastSlots_AreMarkedPastInMatrixAndCannotBeHeld()
    {
        // 2026-11-20 03:00 UTC == 10:00 Vietnam time
        using var scope = new TestContextScope(new DateTimeOffset(2026, 11, 20, 3, 0, 0, TimeSpan.Zero));
        SeededTestData seed = await scope.SeedMinimalAsync();
        DateOnly vnToday = new(2026, 11, 20);

        ScheduleMatrixDto? matrix = await scope.BookingService.GetScheduleMatrixAsync(
            seed.Venue1.Id, vnToday, null, seed.Member1.Id);
        Assert.NotNull(matrix);
        CourtRowDto row = Assert.Single(matrix!.Courts, c => c.CourtId == seed.Venue1Pickleball.Id);

        Assert.Equal(SlotVisualState.Past, row.Slots.First(s => s.StartHour == 9).State);
        Assert.Equal(SlotVisualState.Past, row.Slots.First(s => s.StartHour == 10).State);
        Assert.Equal(SlotVisualState.Available, row.Slots.First(s => s.StartHour == 11).State);

        var pastHold = await scope.BookingService.ToggleHoldSlotAsync(seed.Member1.Id, seed.Venue1Pickleball.Id, vnToday, 10);
        Assert.False(pastHold.Succeeded);

        var futureHoldToday = await scope.BookingService.ToggleHoldSlotAsync(seed.Member1.Id, seed.Venue1Pickleball.Id, vnToday, 11);
        Assert.True(futureHoldToday.Succeeded);
    }

    [Fact]
    public async Task CancelAndCheckInBooking_EnforceRoleAndMultiOwnerTenantIsolation()
    {
        using var scope = new TestContextScope();
        SeededTestData seed = await scope.SeedMinimalAsync();
        DateOnly futureDate = new(2026, 11, 22);

        await scope.BookingService.ToggleHoldSlotAsync(seed.Member1.Id, seed.Venue1Pickleball.Id, futureDate, 14);
        await scope.BookingService.ToggleHoldSlotAsync(seed.Member1.Id, seed.Venue1Pickleball.Id, futureDate, 15);
        await scope.BookingService.ToggleHoldSlotAsync(seed.Member1.Id, seed.Venue1Pickleball.Id, futureDate, 16);
        var confirmed = await scope.BookingService.ConfirmMyHoldsAsync(seed.Member1.Id, seed.Venue1.Id);
        Assert.True(confirmed.Succeeded);

        int b14 = confirmed.Data!.First(t => t.StartHour == 14).Id;
        int b15 = confirmed.Data!.First(t => t.StartHour == 15).Id;
        int b16 = confirmed.Data!.First(t => t.StartHour == 16).Id;

        UserBookingsPageDto userBookings = await scope.BookingService.GetUserBookingsAsync(seed.Member1.Id);
        Assert.Equal(3, userBookings.Upcoming.Count);
        Assert.All(userBookings.Upcoming, b =>
        {
            Assert.True(b.CanCancel);
            Assert.Equal(seed.Venue1.Name, b.VenueName);
        });

        // Member2 and Owner2 cannot cancel or check-in Member1's booking at Owner1's Venue1
        Assert.False((await scope.BookingService.CancelBookingAsync(b14, seed.Member2.Id, UserRole.Member)).Succeeded);
        Assert.False((await scope.BookingService.CancelBookingAsync(b14, seed.Owner2.Id, UserRole.CourtOwner)).Succeeded);
        Assert.False((await scope.BookingService.CheckInBookingAsync(b15, seed.Member1.Id, UserRole.Member)).Succeeded);
        Assert.False((await scope.BookingService.CheckInBookingAsync(b15, seed.Owner2.Id, UserRole.CourtOwner)).Succeeded);

        // Member1 cancels own b14 -> frees slot
        scope.Notifier.Clear();
        var memberCancel = await scope.BookingService.CancelBookingAsync(b14, seed.Member1.Id, UserRole.Member);
        Assert.True(memberCancel.Succeeded);
        Assert.Equal(BookingStatus.Cancelled, memberCancel.Data!.Status);
        Assert.Single(scope.Notifier.SlotUpdates);
        Assert.Equal(seed.Venue1.Id, scope.Notifier.SlotUpdates[0].VenueId);

        // Another user can rehold 14:00
        Assert.True((await scope.BookingService.ToggleHoldSlotAsync(seed.Member2.Id, seed.Venue1Pickleball.Id, futureDate, 14)).Succeeded);

        // Advance to play date so same-day check-in succeeds; Owner1 checks in b15 at Venue1 -> cannot cancel afterwards
        scope.TimeProvider.SetUtcNow(new DateTimeOffset(2026, 11, 22, 3, 0, 0, TimeSpan.Zero));
        var ownerCheckIn = await scope.BookingService.CheckInBookingAsync(b15, seed.Owner1.Id, UserRole.CourtOwner);
        Assert.True(ownerCheckIn.Succeeded);
        Assert.Equal(BookingStatus.CheckedIn, ownerCheckIn.Data!.Status);
        Assert.False((await scope.BookingService.CancelBookingAsync(b15, seed.Owner1.Id, UserRole.CourtOwner)).Succeeded);

        // Admin cannot check in b16 on partner venues; only owning CourtOwner can
        var adminCheckIn = await scope.BookingService.CheckInBookingAsync(b16, seed.Admin.Id, UserRole.Admin);
        Assert.False(adminCheckIn.Succeeded);
        var ownerCheckInB16 = await scope.BookingService.CheckInBookingAsync(b16, seed.Owner1.Id, UserRole.CourtOwner);
        Assert.True(ownerCheckInB16.Succeeded);
    }

    [Fact]
    public async Task ReleaseExpiredHoldsAsync_ExpiresStaleHoldsAndEmitsVenueScopedBatchUpdate()
    {
        using var scope = new TestContextScope();
        SeededTestData seed = await scope.SeedMinimalAsync();
        DateOnly futureDate = new(2026, 11, 22);

        await scope.BookingService.ToggleHoldSlotAsync(seed.Member1.Id, seed.Venue1Pickleball.Id, futureDate, 20);
        await scope.BookingService.ToggleHoldSlotAsync(seed.Member2.Id, seed.Venue2Pickleball.Id, futureDate, 20);
        scope.Notifier.Clear();

        scope.TimeProvider.Advance(TimeSpan.FromMinutes(3));
        int releasedCount = await scope.BookingService.ReleaseExpiredHoldsAsync();

        Assert.Equal(2, releasedCount);
        Assert.Equal(2, scope.Notifier.SlotUpdates.Count);
        Assert.All(scope.Notifier.SlotUpdates, u => Assert.Equal(BookingStatus.Expired, u.Status));
        Assert.Contains(scope.Notifier.SlotUpdates, u => u.VenueId == seed.Venue1.Id);
        Assert.Contains(scope.Notifier.SlotUpdates, u => u.VenueId == seed.Venue2.Id);

        int expiredInDb = await scope.Context.Bookings.CountAsync(b => b.Status == BookingStatus.Expired);
        Assert.Equal(2, expiredInDb);
    }

    [Fact]
    public async Task GetOwnerPortalAsync_ScopesVenuesAndMetricsToOwner_AndPreventsCrossOwnerAccess()
    {
        // 2026-11-22 00:00 UTC == 07:00 VN
        using var scope = new TestContextScope(new DateTimeOffset(2026, 11, 22, 0, 0, 0, TimeSpan.Zero));
        SeededTestData seed = await scope.SeedMinimalAsync();
        DateOnly todayVn = new(2026, 11, 22);

        // Member1 books 2 slots on Venue1 (08:00 = 140k, 18:00 = 190k), Owner1 checks in 1 (08:00)
        await scope.BookingService.ToggleHoldSlotAsync(seed.Member1.Id, seed.Venue1Pickleball.Id, todayVn, 8);
        await scope.BookingService.ToggleHoldSlotAsync(seed.Member1.Id, seed.Venue1Pickleball.Id, todayVn, 18);
        var confirmed = await scope.BookingService.ConfirmMyHoldsAsync(seed.Member1.Id, seed.Venue1.Id);
        var checkIn = await scope.BookingService.CheckInBookingAsync(confirmed.Data![0].Id, seed.Owner1.Id, UserRole.CourtOwner);
        Assert.True(checkIn.Succeeded);

        // Member2 holds 1 slot on Venue1Badminton (09:00) and confirms 1 slot on Venue2
        await scope.BookingService.ToggleHoldSlotAsync(seed.Member2.Id, seed.Venue1Badminton.Id, todayVn, 9);
        await scope.BookingService.ToggleHoldSlotAsync(seed.Member2.Id, seed.Venue2Pickleball.Id, todayVn, 10);
        await scope.BookingService.ConfirmMyHoldsAsync(seed.Member2.Id, seed.Venue2.Id);

        // Owner1 passes Owner2's venueId -> isolated to Owner1's own Venue1!
        OwnerPortalDto owner1Portal = await scope.BookingService.GetOwnerPortalAsync(
            seed.Owner1.Id, selectedVenueId: seed.Venue2.Id, selectedDate: todayVn);

        Assert.Single(owner1Portal.MyVenues);
        Assert.Equal(seed.Venue1.Id, owner1Portal.SelectedVenue!.Id);
        Assert.Equal(2, owner1Portal.TodayConfirmedCount);
        Assert.Equal(1, owner1Portal.TodayCheckedInCount);
        Assert.Equal(140_000m, owner1Portal.TodayRevenueVnd);
        Assert.Equal(190_000m, owner1Portal.TodayExpectedRevenueVnd);
        Assert.Equal(330_000m, owner1Portal.TodayTotalBookedValueVnd);
        Assert.Equal(1, owner1Portal.ActiveHoldsCount);
        Assert.Equal(Math.Round(2.0 / 32.0 * 100.0, 1), owner1Portal.OccupancyPercent);
        Assert.Equal(3, owner1Portal.DateBookings.Count);
        Assert.Equal(2, owner1Portal.VenueCourts.Count);

        // Owner2 sees only Venue2
        OwnerPortalDto owner2Portal = await scope.BookingService.GetOwnerPortalAsync(
            seed.Owner2.Id, selectedVenueId: seed.Venue2.Id, selectedDate: todayVn);
        Assert.Single(owner2Portal.MyVenues);
        Assert.Equal(seed.Venue2.Id, owner2Portal.SelectedVenue!.Id);
        Assert.Equal(1, owner2Portal.TodayConfirmedCount);
        Assert.Equal(0m, owner2Portal.TodayRevenueVnd);
        Assert.Equal(130_000m, owner2Portal.TodayExpectedRevenueVnd);

        // Admin owns no venues -> GetOwnerPortalAsync returns empty
        OwnerPortalDto adminPortal = await scope.BookingService.GetOwnerPortalAsync(
            seed.Admin.Id, selectedVenueId: seed.Venue2.Id, selectedDate: todayVn);
        Assert.Empty(adminPortal.MyVenues);
        Assert.Null(adminPortal.SelectedVenue);
    }

    [Fact]
    public async Task DtoUtcDates_AlwaysPreserveUtcKind_EvenWhenMaterializedUnspecified()
    {
        using var scope = new TestContextScope();
        SeededTestData seed = await scope.SeedMinimalAsync();
        DateOnly futureDate = new(2026, 11, 22);

        DateTime unspecifiedExpires = DateTime.SpecifyKind(
            scope.TimeProvider.GetUtcNow().UtcDateTime.AddMinutes(2),
            DateTimeKind.Unspecified);
        DateTime unspecifiedCreated = DateTime.SpecifyKind(
            scope.TimeProvider.GetUtcNow().UtcDateTime,
            DateTimeKind.Unspecified);

        scope.Context.Bookings.Add(new Booking
        {
            BookingCode = "CB-261122-UNSPEC",
            CourtId = seed.Venue1Pickleball.Id,
            UserId = seed.Member1.Id,
            PlayDate = futureDate,
            StartHour = 16,
            PriceVnd = seed.Venue1Pickleball.BasePricePerHour,
            Status = BookingStatus.Held,
            HoldExpiresAtUtc = unspecifiedExpires,
            CreatedAtUtc = unspecifiedCreated
        });
        await scope.Context.SaveChangesAsync();

        ScheduleMatrixDto? matrix = await scope.BookingService.GetScheduleMatrixAsync(
            seed.Venue1.Id, futureDate, null, seed.Member1.Id);
        Assert.NotNull(matrix);
        SlotCellDto heldCell = matrix!.Courts.Single(c => c.CourtId == seed.Venue1Pickleball.Id).Slots.Single(s => s.StartHour == 16);

        Assert.NotNull(heldCell.HoldExpiresAtUtc);
        Assert.Equal(DateTimeKind.Utc, heldCell.HoldExpiresAtUtc!.Value.Kind);
        Assert.NotNull(matrix.MyHolds.EarliestExpiresAtUtc);
        Assert.Equal(DateTimeKind.Utc, matrix.MyHolds.EarliestExpiresAtUtc!.Value.Kind);
        Assert.Equal(DateTimeKind.Utc, matrix.MyHolds.Items[0].HoldExpiresAtUtc!.Value.Kind);

        var confirmed = await scope.BookingService.ConfirmMyHoldsAsync(seed.Member1.Id, seed.Venue1.Id);
        Assert.True(confirmed.Succeeded);
        Assert.Equal(DateTimeKind.Utc, confirmed.Data![0].CreatedAtUtc.Kind);
    }
}
