using CourtBook.BusinessObjects.Entities;
using CourtBook.BusinessObjects.Enums;
using CourtBook.Services.Models;

namespace CourtBook.Tests;

public class CourtServiceTests
{
    [Fact]
    public async Task CreateAndUpdateCourt_AllowsSameCodeAcrossVenues_EnforcesUniqueWithinVenue_AndEmitsRealtime()
    {
        using var scope = new TestContextScope();
        SeededTestData seed = await scope.SeedMinimalAsync();

        // Both Venue1 and Venue2 already have PB-01; adding PB-01 to Venue1 again must fail
        var dupCode = await scope.CourtService.CreateAsync(
            seed.Owner1.Id,
            isAdmin: false,
            new CourtUpsertDto
            {
                VenueId = seed.Venue1.Id,
                Code = "pb-01",
                Name = "Sân Trùng Mã",
                SportType = SportType.Pickleball,
                IsIndoor = true,
                Surface = "Acrylic",
                BasePricePerHour = 120_000m,
                PeakPricePerHour = 160_000m
            });
        Assert.False(dupCode.Succeeded);

        var created = await scope.CourtService.CreateAsync(
            seed.Owner1.Id,
            isAdmin: false,
            new CourtUpsertDto
            {
                VenueId = seed.Venue1.Id,
                Code = "PB-09",
                Name = "Sân Pickleball VIP",
                SportType = SportType.Pickleball,
                IsIndoor = true,
                Surface = "Acrylic Pro",
                BasePricePerHour = 150_000m,
                PeakPricePerHour = 210_000m,
                DisplayOrder = 9
            });
        Assert.True(created.Succeeded);
        Assert.NotNull(created.Data);
        Assert.Single(scope.Notifier.CourtStatusUpdates);
        Assert.Equal(seed.Venue1.Id, scope.Notifier.CourtStatusUpdates[0].VenueId);

        var updated = await scope.CourtService.UpdateAsync(
            seed.Owner1.Id,
            isAdmin: false,
            new CourtUpsertDto
            {
                Id = seed.Venue1Pickleball.Id,
                VenueId = seed.Venue1.Id,
                Code = seed.Venue1Pickleball.Code,
                Name = "Sân Pickleball 1 (Nâng cấp)",
                SportType = seed.Venue1Pickleball.SportType,
                IsIndoor = seed.Venue1Pickleball.IsIndoor,
                Surface = seed.Venue1Pickleball.Surface,
                BasePricePerHour = 155_000m,
                PeakPricePerHour = 205_000m,
                IsActive = true,
                DisplayOrder = 1
            });
        Assert.True(updated.Succeeded);
        Assert.Equal("Sân Pickleball 1 (Nâng cấp)", updated.Data!.Name);
        Assert.Equal(2, scope.Notifier.CourtStatusUpdates.Count);
    }

    [Fact]
    public async Task CourtMutations_EnforceOwnerTenantIsolation_WhileAdminCanManageAnyVenue()
    {
        using var scope = new TestContextScope();
        SeededTestData seed = await scope.SeedMinimalAsync();

        var crossCreate = await scope.CourtService.CreateAsync(
            seed.Owner2.Id,
            isAdmin: false,
            new CourtUpsertDto
            {
                VenueId = seed.Venue1.Id,
                Code = "PB-88",
                Name = "Sân Chui",
                SportType = SportType.Pickleball,
                Surface = "Acrylic",
                BasePricePerHour = 100_000m,
                PeakPricePerHour = 150_000m
            });
        Assert.False(crossCreate.Succeeded);

        var crossUpdate = await scope.CourtService.UpdateAsync(
            seed.Owner2.Id,
            isAdmin: false,
            new CourtUpsertDto
            {
                Id = seed.Venue1Pickleball.Id,
                VenueId = seed.Venue1.Id,
                Code = "PB-01",
                Name = "Hack Name",
                SportType = SportType.Pickleball,
                Surface = "Acrylic",
                BasePricePerHour = 10_000m,
                PeakPricePerHour = 20_000m
            });
        Assert.False(crossUpdate.Succeeded);

        var crossToggle = await scope.CourtService.ToggleActiveAsync(
            seed.Owner2.Id,
            isAdmin: false,
            seed.Venue1Pickleball.Id);
        Assert.False(crossToggle.Succeeded);

        var adminToggle = await scope.CourtService.ToggleActiveAsync(
            999,
            isAdmin: true,
            seed.Venue1Pickleball.Id);
        Assert.True(adminToggle.Succeeded);
        Assert.False(adminToggle.Data!.IsActive);
    }

    [Fact]
    public async Task ToggleActiveAsync_MarksCourtMaintenance_EmitsVenueScopedRealtime_AndBlocksHolds()
    {
        using var scope = new TestContextScope();
        SeededTestData seed = await scope.SeedMinimalAsync();
        DateOnly futureDate = new(2026, 11, 22);

        ServiceResult<Court> toggledOff = await scope.CourtService.ToggleActiveAsync(
            seed.Owner1.Id, isAdmin: false, seed.Venue1Pickleball.Id);
        Assert.True(toggledOff.Succeeded);
        Assert.False(toggledOff.Data!.IsActive);
        Assert.Contains(
            scope.Notifier.CourtStatusUpdates,
            u => u.VenueId == seed.Venue1.Id && u.CourtId == seed.Venue1Pickleball.Id && !u.IsActive);

        ServiceResult<ToggleHoldResponseDto> holdAttempt =
            await scope.BookingService.ToggleHoldSlotAsync(seed.Member1.Id, seed.Venue1Pickleball.Id, futureDate, 18);
        Assert.False(holdAttempt.Succeeded);

        ScheduleMatrixDto? matrix = await scope.BookingService.GetScheduleMatrixAsync(
            seed.Venue1.Id, futureDate, null, seed.Member1.Id);
        Assert.NotNull(matrix);
        CourtRowDto row = Assert.Single(matrix!.Courts, c => c.CourtId == seed.Venue1Pickleball.Id);
        Assert.All(row.Slots, s => Assert.Equal(SlotVisualState.Maintenance, s.State));

        ServiceResult<Court> toggledOn = await scope.CourtService.ToggleActiveAsync(
            seed.Owner1.Id, isAdmin: false, seed.Venue1Pickleball.Id);
        Assert.True(toggledOn.Succeeded);
        Assert.True(toggledOn.Data!.IsActive);
    }

    [Fact]
    public async Task CreateAsync_ValidatesRequiredFieldsAndPositivePrices()
    {
        using var scope = new TestContextScope();
        SeededTestData seed = await scope.SeedMinimalAsync();

        var invalidPrice = await scope.CourtService.CreateAsync(
            seed.Owner1.Id,
            isAdmin: false,
            new CourtUpsertDto
            {
                VenueId = seed.Venue1.Id,
                Code = "PB-09",
                Name = "Sân Lỗi Giá",
                SportType = SportType.Pickleball,
                IsIndoor = true,
                Surface = "Acrylic",
                BasePricePerHour = 0m,
                PeakPricePerHour = 160_000m
            });
        Assert.False(invalidPrice.Succeeded);

        var missingSurface = await scope.CourtService.CreateAsync(
            seed.Owner1.Id,
            isAdmin: false,
            new CourtUpsertDto
            {
                VenueId = seed.Venue1.Id,
                Code = "PB-09",
                Name = "Sân Thiếu Mặt",
                SportType = SportType.Pickleball,
                Surface = "",
                BasePricePerHour = 120_000m,
                PeakPricePerHour = 160_000m
            });
        Assert.False(missingSurface.Succeeded);
    }
}
