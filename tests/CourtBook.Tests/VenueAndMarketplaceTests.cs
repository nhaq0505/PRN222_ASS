using CourtBook.BusinessObjects.Entities;
using CourtBook.BusinessObjects.Enums;
using CourtBook.DataAccessObjects;
using CourtBook.Services.Models;

namespace CourtBook.Tests;

public class VenueAndMarketplaceTests
{
    [Fact]
    public async Task GetMarketplaceAsync_FiltersByKeywordDistrictAndSport_AndExcludesPendingOrSuspended()
    {
        using var scope = new TestContextScope();
        await DbInitializer.InitializeAsync(scope.Context);
        DateOnly todayVn = new(2026, 11, 20);

        MarketplaceBrowseDto all = await scope.VenueService.GetMarketplaceAsync(null, null, null, todayVn);
        Assert.Equal(3, all.Venues.Count);
        Assert.Equal(3, all.TotalActiveVenues);
        Assert.Equal(15, all.TotalActiveCourts);
        Assert.DoesNotContain(all.Venues, v => v.Name.Contains("Tân Bình"));
        Assert.Equal(2, all.AvailableDistricts.Count);

        MarketplaceBrowseDto byDistrict = await scope.VenueService.GetMarketplaceAsync(null, "Quận 7", null, todayVn);
        VenueCardDto pmh = Assert.Single(byDistrict.Venues);
        Assert.Equal("Phú Mỹ Hưng Sport Center", pmh.Name);
        Assert.Equal(2, pmh.PickleballCourtCount);
        Assert.Equal(3, pmh.BadmintonCourtCount);
        Assert.Equal(5, pmh.TotalActiveCourts);
        Assert.Equal(95_000m, pmh.MinPriceVnd);
        Assert.Equal(175_000m, pmh.MaxPriceVnd);
        Assert.Equal(3, byDistrict.TotalActiveVenues);

        MarketplaceBrowseDto byKeyword = await scope.VenueService.GetMarketplaceAsync("sala", null, null, todayVn);
        VenueCardDto sala = Assert.Single(byKeyword.Venues);
        Assert.Equal("Sala Riverside Pickleball", sala.Name);
        Assert.Equal(4, sala.PickleballCourtCount);
        Assert.Equal(0, sala.BadmintonCourtCount);

        MarketplaceBrowseDto byBadminton = await scope.VenueService.GetMarketplaceAsync(null, null, SportType.Badminton, todayVn);
        Assert.Equal(2, byBadminton.Venues.Count);
        Assert.DoesNotContain(byBadminton.Venues, v => v.Name.Contains("Sala"));
    }

    [Fact]
    public async Task GetMarketplaceAsync_ComputesAvailableSlotsToday_ExcludingPastAndOccupiedSlots()
    {
        // 2026-11-20 03:00 UTC == 10:00 VN -> future hours are 11..21 (11 hours)
        using var scope = new TestContextScope(new DateTimeOffset(2026, 11, 20, 3, 0, 0, TimeSpan.Zero));
        SeededTestData seed = await scope.SeedMinimalAsync();
        DateOnly todayVn = new(2026, 11, 20);

        // Venue1 has 2 active courts * 11 future hours = 22 future slots
        MarketplaceBrowseDto initial = await scope.VenueService.GetMarketplaceAsync("Thảo Điền", null, null, todayVn);
        Assert.Equal(22, Assert.Single(initial.Venues).AvailableSlotsToday);

        // Hold 18:00 and confirm + hold 19:00 on Venue1Pickleball
        await scope.BookingService.ToggleHoldSlotAsync(seed.Member1.Id, seed.Venue1Pickleball.Id, todayVn, 18);
        await scope.BookingService.ConfirmMyHoldsAsync(seed.Member1.Id, seed.Venue1.Id);
        await scope.BookingService.ToggleHoldSlotAsync(seed.Member2.Id, seed.Venue1Badminton.Id, todayVn, 19);

        // Also seed a past confirmed booking (08:00) and an expired hold (20:00)
        scope.Context.Bookings.AddRange(
            new Booking
            {
                BookingCode = "CB-PAST-01",
                CourtId = seed.Venue1Pickleball.Id,
                UserId = seed.Member1.Id,
                PlayDate = todayVn,
                StartHour = 8,
                PriceVnd = 140_000m,
                Status = BookingStatus.Confirmed
            },
            new Booking
            {
                BookingCode = "CB-EXP-01",
                CourtId = seed.Venue1Pickleball.Id,
                UserId = seed.Member2.Id,
                PlayDate = todayVn,
                StartHour = 20,
                PriceVnd = 190_000m,
                Status = BookingStatus.Held,
                HoldExpiresAtUtc = scope.TimeProvider.GetUtcNow().UtcDateTime.AddMinutes(-1)
            });
        await scope.Context.SaveChangesAsync();

        MarketplaceBrowseDto after = await scope.VenueService.GetMarketplaceAsync("Thảo Điền", null, null, todayVn);
        // Only the 2 future active slots (18:00 confirmed + 19:00 active hold) reduce availability -> 20 left
        Assert.Equal(20, Assert.Single(after.Venues).AvailableSlotsToday);
    }

    [Fact]
    public async Task OwnerVenueLifecycle_CreatePendingVenue_BlocksPublicMatrixAndHold_UntilAdminApproves()
    {
        using var scope = new TestContextScope();
        SeededTestData seed = await scope.SeedMinimalAsync();
        DateOnly futureDate = new(2026, 11, 22);

        ServiceResult<Venue> createRes = await scope.VenueService.CreateVenueAsync(
            seed.Owner1.Id,
            new VenueUpsertDto
            {
                Name = "Cụm Sân Đảo Kim Cương",
                District = "TP. Thủ Đức",
                Address = "01 Trần Quý Kiên, Bình Trưng Tây",
                PhoneNumber = "0902999888",
                Description = "Sân mới ven sông",
                AccentColor = "teal",
                OpenHour = 7,
                CloseHour = 21
            });

        Assert.True(createRes.Succeeded);
        Venue newVenue = createRes.Data!;
        Assert.Equal(VenueStatus.PendingApproval, newVenue.Status);
        Assert.Equal("cum-san-dao-kim-cuong", newVenue.Slug);

        ServiceResult<Court> addCourtRes = await scope.CourtService.CreateAsync(
            seed.Owner1.Id,
            new CourtUpsertDto
            {
                VenueId = newVenue.Id,
                Code = "PB-01",
                Name = "Sân Kim Cương 1",
                SportType = SportType.Pickleball,
                IsIndoor = true,
                Surface = "Acrylic Pro",
                BasePricePerHour = 150_000m,
                PeakPricePerHour = 200_000m
            });
        Assert.True(addCourtRes.Succeeded);

        // Pending venue is hidden from marketplace, public matrix returns null, and holding is blocked
        MarketplaceBrowseDto browseBefore = await scope.VenueService.GetMarketplaceAsync("Kim Cương", null, null, futureDate);
        Assert.Empty(browseBefore.Venues);
        Assert.Null(await scope.BookingService.GetScheduleMatrixAsync(newVenue.Id, futureDate, null, seed.Member1.Id));

        ServiceResult<ToggleHoldResponseDto> holdBeforeApprove =
            await scope.BookingService.ToggleHoldSlotAsync(seed.Member1.Id, addCourtRes.Data!.Id, futureDate, 18);
        Assert.False(holdBeforeApprove.Succeeded);

        // Admin approves the venue -> unlocked everywhere!
        ServiceResult<Venue> approveRes = await scope.PlatformAdminService.ApproveVenueAsync(newVenue.Id);
        Assert.True(approveRes.Succeeded);
        Assert.Equal(VenueStatus.Active, approveRes.Data!.Status);

        ScheduleMatrixDto? matrixAfter = await scope.BookingService.GetScheduleMatrixAsync(newVenue.Id, futureDate, null, seed.Member1.Id);
        Assert.NotNull(matrixAfter);
        Assert.Equal(14, matrixAfter!.Hours.Count); // 07:00 .. 20:00
        Assert.Equal(7, matrixAfter.OpenHour);
        Assert.Equal(21, matrixAfter.CloseHour);

        ServiceResult<ToggleHoldResponseDto> holdAfterApprove =
            await scope.BookingService.ToggleHoldSlotAsync(seed.Member1.Id, addCourtRes.Data!.Id, futureDate, 18);
        Assert.True(holdAfterApprove.Succeeded);
    }

    [Fact]
    public async Task CreateVenueAsync_ValidatesOperatingHoursAndGeneratesUniqueSlugs()
    {
        using var scope = new TestContextScope();
        SeededTestData seed = await scope.SeedMinimalAsync();

        var badHours = await scope.VenueService.CreateVenueAsync(
            seed.Owner1.Id,
            new VenueUpsertDto
            {
                Name = "Sân Test Giờ",
                District = "Quận 1",
                Address = "10 Lê Lợi",
                PhoneNumber = "0902111222",
                OpenHour = 10,
                CloseHour = 12
            });
        Assert.False(badHours.Succeeded);

        var first = await scope.VenueService.CreateVenueAsync(
            seed.Owner1.Id,
            new VenueUpsertDto
            {
                Name = "Sân Đồng Danh",
                District = "Quận 2",
                Address = "12 Thảo Điền",
                PhoneNumber = "0902111222"
            });
        var second = await scope.VenueService.CreateVenueAsync(
            seed.Owner2.Id,
            new VenueUpsertDto
            {
                Name = "Sân Đồng Danh",
                District = "Quận 7",
                Address = "99 Nguyễn Văn Linh",
                PhoneNumber = "0902333444"
            });

        Assert.True(first.Succeeded);
        Assert.True(second.Succeeded);
        Assert.Equal(VenueStatus.PendingApproval, first.Data!.Status);
        Assert.Equal(VenueStatus.PendingApproval, second.Data!.Status);
        Assert.Equal("san-dong-danh", first.Data.Slug);
        Assert.Equal("san-dong-danh-2", second.Data.Slug);
    }

    [Fact]
    public async Task UpdateVenueAsync_EnforcesTenantIsolation_RejectsCrossOwnerAndAdmin()
    {
        using var scope = new TestContextScope();
        SeededTestData seed = await scope.SeedMinimalAsync();

        var crossOwnerAttempt = await scope.VenueService.UpdateVenueAsync(
            seed.Owner2.Id,
            new VenueUpsertDto
            {
                Id = seed.Venue1.Id,
                Name = "Cướp quyền đổi tên",
                District = seed.Venue1.District,
                Address = seed.Venue1.Address,
                PhoneNumber = seed.Venue1.PhoneNumber,
                OpenHour = 6,
                CloseHour = 22
            });
        Assert.False(crossOwnerAttempt.Succeeded);

        var ownerUpdate = await scope.VenueService.UpdateVenueAsync(
            seed.Owner1.Id,
            new VenueUpsertDto
            {
                Id = seed.Venue1.Id,
                Name = "Thảo Điền Racquet Club Pro",
                District = seed.Venue1.District,
                Address = seed.Venue1.Address,
                PhoneNumber = seed.Venue1.PhoneNumber,
                Description = "Nâng cấp mặt sân mới",
                AccentColor = "blue",
                OpenHour = 6,
                CloseHour = 22
            });
        Assert.True(ownerUpdate.Succeeded);
        Assert.Equal("Thảo Điền Racquet Club Pro", ownerUpdate.Data!.Name);

        var adminUpdate = await scope.VenueService.UpdateVenueAsync(
            seed.Admin.Id,
            new VenueUpsertDto
            {
                Id = seed.Venue1.Id,
                Name = "Thảo Điền Racquet Club (Verified)",
                District = seed.Venue1.District,
                Address = seed.Venue1.Address,
                PhoneNumber = seed.Venue1.PhoneNumber,
                OpenHour = 6,
                CloseHour = 22
            });
        Assert.False(adminUpdate.Succeeded);
    }

    [Fact]
    public async Task PlatformAdmin_GovernanceAndDashboardMetrics_WorkAccurately()
    {
        using var scope = new TestContextScope();
        await DbInitializer.InitializeAsync(scope.Context);
        DateOnly todayVn = DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, TimeZoneInfo.CreateCustomTimeZone("ICT", TimeSpan.FromHours(7), "ICT", "ICT")));

        PlatformAdminDashboardDto dash = await scope.PlatformAdminService.GetDashboardAsync(todayVn);
        Assert.Equal(3, dash.ActiveVenuesCount);
        Assert.Equal(1, dash.PendingVenuesCount);
        Assert.Equal(2, dash.TotalOwnersCount);
        Assert.Equal(2, dash.TotalMembersCount);
        Assert.True(dash.TodayBookingsCount > 0);
        Assert.True(dash.TodayPlatformGmvVnd > 0m);
        Assert.True(dash.TodayRealizedRevenueVnd > 0m);
        Assert.True(dash.TodayExpectedRevenueVnd > 0m);
        Assert.Equal(dash.TodayPlatformGmvVnd, dash.TodayRealizedRevenueVnd + dash.TodayExpectedRevenueVnd);
        Assert.Equal(4, dash.AllVenues.Count);
        Assert.Equal(5, dash.AllUsers.Count);

        AdminVenueRowDto pendingRow = Assert.Single(dash.AllVenues, v => v.Status == VenueStatus.PendingApproval);
        Assert.True((await scope.PlatformAdminService.ApproveVenueAsync(pendingRow.Id)).Succeeded);
        Assert.True((await scope.PlatformAdminService.SuspendVenueAsync(pendingRow.Id)).Succeeded);
        Assert.Null(await scope.BookingService.GetScheduleMatrixAsync(pendingRow.Id, todayVn.AddDays(1), null, null));
        Assert.True((await scope.PlatformAdminService.ReactivateVenueAsync(pendingRow.Id)).Succeeded);
        Assert.NotNull(await scope.BookingService.GetScheduleMatrixAsync(pendingRow.Id, todayVn.AddDays(1), null, null));

        AdminUserRowDto adminRow = Assert.Single(dash.AllUsers, u => u.Role == UserRole.Admin);
        AdminUserRowDto memberRow = dash.AllUsers.First(u => u.Role == UserRole.Member);
        Assert.False((await scope.PlatformAdminService.ToggleUserActiveAsync(adminRow.Id, adminRow.Id)).Succeeded);

        var lockMember = await scope.PlatformAdminService.ToggleUserActiveAsync(memberRow.Id, adminRow.Id);
        Assert.True(lockMember.Succeeded);
        Assert.False(lockMember.Data!.IsActive);
    }
}
