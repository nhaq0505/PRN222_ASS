using CourtBook.BusinessObjects.Entities;
using CourtBook.BusinessObjects.Enums;
using CourtBook.DataAccessObjects;
using CourtBook.Services.Models;

namespace CourtBook.Tests;

public class VenueAndCourtPhotoTests
{
    [Fact]
    public void VenueGetAllPhotos_ReturnsDistinctNonEmptyUrlsStartingWithCover()
    {
        var emptyVenue = new Venue();
        Assert.Empty(emptyVenue.GetAllPhotos());

        var venue = new Venue
        {
            CoverImageUrl = " /uploads/venues/cover.png ",
            GalleryImageUrls = "/uploads/venues/cover.png;/uploads/venues/g1.png | /uploads/venues/G1.png ; ; https://cdn.example.com/g2.webp|"
        };

        IReadOnlyList<string> photos = venue.GetAllPhotos();
        Assert.Equal(3, photos.Count);
        Assert.Equal("/uploads/venues/cover.png", photos[0]);
        Assert.Equal("/uploads/venues/g1.png", photos[1]);
        Assert.Equal("https://cdn.example.com/g2.webp", photos[2]);
    }

    [Fact]
    public async Task SeededVenuesAndCourts_PopulatePhotoUrlsAndMapAcrossAllDtos()
    {
        using var scope = new TestContextScope();
        await DbInitializer.InitializeAsync(scope.Context);
        DateOnly todayVn = new(2026, 11, 20);

        MarketplaceBrowseDto marketplace = await scope.VenueService.GetMarketplaceAsync(null, null, null, todayVn);
        VenueCardDto thaoDien = Assert.Single(marketplace.Venues, v => v.Name == "Thảo Điền Racquet Club");
        Assert.Equal("/uploads/venues/thao-dien-cover.jpg", thaoDien.CoverImageUrl);
        Assert.Equal(3, thaoDien.PhotoCount);
        Assert.Equal(
            new[]
            {
                "/uploads/venues/thao-dien-cover.jpg",
                "/uploads/venues/thao-dien-gallery-1.jpg",
                "/uploads/venues/sala-cover.jpg"
            },
            thaoDien.PhotoUrls);

        ScheduleMatrixDto? matrix = await scope.BookingService.GetScheduleMatrixAsync(thaoDien.Id, todayVn.AddDays(1));
        Assert.NotNull(matrix);
        Assert.Equal("/uploads/venues/thao-dien-cover.jpg", matrix!.VenueCoverImageUrl);
        Assert.Equal(3, matrix.VenuePhotoUrls.Count);
        Assert.False(string.IsNullOrWhiteSpace(matrix.VenueDescription));
        Assert.All(
            matrix.Courts.Where(c => c.SportType == SportType.Pickleball),
            c => Assert.Equal("/uploads/venues/thao-dien-cover.jpg", c.ImageUrl));
        Assert.All(
            matrix.Courts.Where(c => c.SportType == SportType.Badminton),
            c => Assert.Equal("/uploads/venues/thao-dien-gallery-1.jpg", c.ImageUrl));

        PlatformAdminDashboardDto adminDash = await scope.PlatformAdminService.GetDashboardAsync(todayVn);
        AdminVenueRowDto tanBinh = Assert.Single(adminDash.AllVenues, v => v.Name == "Tân Bình Arena");
        Assert.Equal("/uploads/venues/tan-binh-cover.jpg", tanBinh.CoverImageUrl);

        Venue? thaoDienEntity = await scope.VenueService.GetVenueByIdAsync(thaoDien.Id);
        Assert.NotNull(thaoDienEntity);
        OwnerPortalDto ownerPortal = await scope.BookingService.GetOwnerPortalAsync(
            thaoDienEntity!.OwnerId, thaoDien.Id, todayVn);
        Assert.Contains(ownerPortal.MyVenues, v => v.Id == thaoDien.Id && v.CoverImageUrl == "/uploads/venues/thao-dien-cover.jpg");
    }

    [Fact]
    public async Task VenuePhotoPersistence_SupportsCreateUpdatePreservationAndRemoval()
    {
        using var scope = new TestContextScope();
        SeededTestData seed = await scope.SeedMinimalAsync();

        ServiceResult<Venue> created = await scope.VenueService.CreateVenueAsync(
            seed.Owner1.Id,
            new VenueUpsertDto
            {
                Name = "Sân Ảnh Mới",
                District = "Quận 1",
                Address = "12 Nguyễn Huệ",
                PhoneNumber = "0901234567",
                Description = "Cụm sân trung tâm",
                CoverImageUrl = "/uploads/venues/new-cover.jpg",
                GalleryImageUrls = "/uploads/venues/g1.png|https://cdn.courtbook.vn/g2.webp"
            });

        Assert.True(created.Succeeded);
        Assert.Equal("/uploads/venues/new-cover.jpg", created.Data!.CoverImageUrl);
        Assert.Equal("/uploads/venues/g1.png;https://cdn.courtbook.vn/g2.webp", created.Data.GalleryImageUrls);

        // Passing null for CoverImageUrl and GalleryImageUrls must preserve existing photos
        ServiceResult<Venue> preserved = await scope.VenueService.UpdateVenueAsync(
            seed.Owner1.Id,
            new VenueUpsertDto
            {
                Id = created.Data.Id,
                Name = "Sân Ảnh Mới (Đã sửa tên)",
                District = "Quận 1",
                Address = "12 Nguyễn Huệ",
                PhoneNumber = "0901234567",
                Description = "Cụm sân trung tâm",
                OpenHour = 6,
                CloseHour = 22,
                CoverImageUrl = null,
                GalleryImageUrls = null
            });

        Assert.True(preserved.Succeeded);
        Assert.Equal("/uploads/venues/new-cover.jpg", preserved.Data!.CoverImageUrl);
        Assert.Equal("/uploads/venues/g1.png;https://cdn.courtbook.vn/g2.webp", preserved.Data.GalleryImageUrls);

        // Removing one gallery photo and updating cover updates cleanly
        ServiceResult<Venue> updatedPhotos = await scope.VenueService.UpdateVenueAsync(
            seed.Owner1.Id,
            new VenueUpsertDto
            {
                Id = created.Data.Id,
                Name = "Sân Ảnh Mới (Đã sửa tên)",
                District = "Quận 1",
                Address = "12 Nguyễn Huệ",
                PhoneNumber = "0901234567",
                Description = "Cụm sân trung tâm",
                OpenHour = 6,
                CloseHour = 22,
                CoverImageUrl = "https://cdn.courtbook.vn/cover-v2.webp",
                GalleryImageUrls = "https://cdn.courtbook.vn/g2.webp"
            });

        Assert.True(updatedPhotos.Succeeded);
        Assert.Equal("https://cdn.courtbook.vn/cover-v2.webp", updatedPhotos.Data!.CoverImageUrl);
        Assert.Equal("https://cdn.courtbook.vn/g2.webp", updatedPhotos.Data.GalleryImageUrls);
    }

    [Theory]
    [InlineData("JavaScript:alert(1)")]
    [InlineData("javascript:void(0)")]
    [InlineData("data:text/html,<script>alert(1)</script>")]
    [InlineData("http://insecure.example.com/pic.png")]
    [InlineData("/uploads/../secrets/pic.png")]
    [InlineData("//evil.example.com/pic.png")]
    public async Task VenueAndCourtServices_RejectUnsafeImageUrls(string unsafeUrl)
    {
        using var scope = new TestContextScope();
        SeededTestData seed = await scope.SeedMinimalAsync();

        ServiceResult<Venue> badCover = await scope.VenueService.CreateVenueAsync(
            seed.Owner1.Id,
            new VenueUpsertDto
            {
                Name = "Sân Lỗi Ảnh",
                District = "Quận 3",
                Address = "10 Võ Văn Tần",
                PhoneNumber = "0901112233",
                CoverImageUrl = unsafeUrl
            });
        Assert.False(badCover.Succeeded);

        ServiceResult<Venue> badGallery = await scope.VenueService.UpdateVenueAsync(
            seed.Owner1.Id,
            new VenueUpsertDto
            {
                Id = seed.Venue1.Id,
                Name = seed.Venue1.Name,
                District = seed.Venue1.District,
                Address = seed.Venue1.Address,
                PhoneNumber = seed.Venue1.PhoneNumber,
                OpenHour = seed.Venue1.OpenHour,
                CloseHour = seed.Venue1.CloseHour,
                GalleryImageUrls = $"/uploads/venues/valid.png;{unsafeUrl}"
            });
        Assert.False(badGallery.Succeeded);

        ServiceResult<Court> badCourtCreate = await scope.CourtService.CreateAsync(
            seed.Owner1.Id,
            new CourtUpsertDto
            {
                VenueId = seed.Venue1.Id,
                Code = "PB-99",
                Name = "Sân Lỗi Ảnh",
                SportType = SportType.Pickleball,
                Surface = "Acrylic",
                BasePricePerHour = 120_000m,
                PeakPricePerHour = 160_000m,
                ImageUrl = unsafeUrl
            });
        Assert.False(badCourtCreate.Succeeded);

        ServiceResult<Court> badCourtUpdate = await scope.CourtService.UpdateAsync(
            seed.Owner1.Id,
            new CourtUpsertDto
            {
                Id = seed.Venue1Pickleball.Id,
                VenueId = seed.Venue1.Id,
                Code = seed.Venue1Pickleball.Code,
                Name = seed.Venue1Pickleball.Name,
                SportType = seed.Venue1Pickleball.SportType,
                Surface = seed.Venue1Pickleball.Surface,
                BasePricePerHour = seed.Venue1Pickleball.BasePricePerHour,
                PeakPricePerHour = seed.Venue1Pickleball.PeakPricePerHour,
                ImageUrl = unsafeUrl
            });
        Assert.False(badCourtUpdate.Succeeded);
    }

    [Fact]
    public async Task CourtImageUrl_PersistsPreservesAndClearsProperly()
    {
        using var scope = new TestContextScope();
        SeededTestData seed = await scope.SeedMinimalAsync();

        ServiceResult<Court> created = await scope.CourtService.CreateAsync(
            seed.Owner1.Id,
            new CourtUpsertDto
            {
                VenueId = seed.Venue1.Id,
                Code = "PB-77",
                Name = "Sân Ảnh Riêng",
                SportType = SportType.Pickleball,
                IsIndoor = true,
                Surface = "Acrylic Pro",
                BasePricePerHour = 150_000m,
                PeakPricePerHour = 200_000m,
                ImageUrl = "/uploads/courts/court-77.png"
            });

        Assert.True(created.Succeeded);
        Assert.Equal("/uploads/courts/court-77.png", created.Data!.ImageUrl);

        // Null preserves existing ImageUrl
        ServiceResult<Court> preserved = await scope.CourtService.UpdateAsync(
            seed.Owner1.Id,
            new CourtUpsertDto
            {
                Id = created.Data.Id,
                VenueId = seed.Venue1.Id,
                Code = "PB-77",
                Name = "Sân Ảnh Riêng Updated",
                SportType = SportType.Pickleball,
                IsIndoor = true,
                Surface = "Acrylic Pro",
                BasePricePerHour = 155_000m,
                PeakPricePerHour = 205_000m,
                ImageUrl = null
            });

        Assert.True(preserved.Succeeded);
        Assert.Equal("/uploads/courts/court-77.png", preserved.Data!.ImageUrl);

        // Empty string clears ImageUrl
        ServiceResult<Court> cleared = await scope.CourtService.UpdateAsync(
            seed.Owner1.Id,
            new CourtUpsertDto
            {
                Id = created.Data.Id,
                VenueId = seed.Venue1.Id,
                Code = "PB-77",
                Name = "Sân Ảnh Riêng Cleared",
                SportType = SportType.Pickleball,
                IsIndoor = true,
                Surface = "Acrylic Pro",
                BasePricePerHour = 155_000m,
                PeakPricePerHour = 205_000m,
                ImageUrl = ""
            });

        Assert.True(cleared.Succeeded);
        Assert.Null(cleared.Data!.ImageUrl);
    }
}
