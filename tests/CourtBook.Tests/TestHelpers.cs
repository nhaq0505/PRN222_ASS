using CourtBook.BusinessObjects.Entities;
using CourtBook.BusinessObjects.Enums;
using CourtBook.BusinessObjects.Security;
using CourtBook.DataAccessObjects;
using CourtBook.Repositories;
using CourtBook.Services.Implementations;
using CourtBook.Services.Interfaces;
using CourtBook.Services.Models;
using Microsoft.EntityFrameworkCore;

namespace CourtBook.Tests;

public sealed class MutableTimeProvider : TimeProvider
{
    private DateTimeOffset _utcNow;

    public MutableTimeProvider(DateTimeOffset initialUtcNow)
    {
        _utcNow = initialUtcNow;
    }

    public override DateTimeOffset GetUtcNow() => _utcNow;

    public void Advance(TimeSpan delta) => _utcNow = _utcNow.Add(delta);

    public void SetUtcNow(DateTimeOffset utcNow) => _utcNow = utcNow;
}

public sealed class RecordingSlotRealtimeNotifier : ISlotRealtimeNotifier
{
    public List<SlotUpdateBroadcastDto> SlotUpdates { get; } = new();
    public List<(int VenueId, int CourtId, bool IsActive)> CourtStatusUpdates { get; } = new();

    public Task NotifySlotUpdatedAsync(SlotUpdateBroadcastDto update)
    {
        SlotUpdates.Add(update);
        return Task.CompletedTask;
    }

    public Task NotifySlotsUpdatedAsync(IEnumerable<SlotUpdateBroadcastDto> updates)
    {
        SlotUpdates.AddRange(updates);
        return Task.CompletedTask;
    }

    public Task NotifyCourtStatusChangedAsync(int venueId, int courtId, bool isActive)
    {
        CourtStatusUpdates.Add((venueId, courtId, isActive));
        return Task.CompletedTask;
    }

    public void Clear()
    {
        SlotUpdates.Clear();
        CourtStatusUpdates.Clear();
    }
}

public sealed record SeededTestData(
    AppUser Owner1,
    AppUser Owner2,
    AppUser Member1,
    AppUser Member2,
    Venue Venue1,
    Venue Venue2,
    Court Venue1Pickleball,
    Court Venue1Badminton,
    Court Venue2Pickleball);

public sealed class TestContextScope : IDisposable
{
    public CourtBookDbContext Context { get; }
    public MutableTimeProvider TimeProvider { get; }
    public RecordingSlotRealtimeNotifier Notifier { get; }
    public IUserRepository UserRepository { get; }
    public IVenueRepository VenueRepository { get; }
    public ICourtRepository CourtRepository { get; }
    public IBookingRepository BookingRepository { get; }
    public AuthService AuthService { get; }
    public VenueService VenueService { get; }
    public CourtService CourtService { get; }
    public BookingService BookingService { get; }
    public PlatformAdminService PlatformAdminService { get; }

    public TestContextScope(DateTimeOffset? initialUtcNow = null)
    {
        var options = new DbContextOptionsBuilder<CourtBookDbContext>()
            .UseInMemoryDatabase($"CourtBookTest_{Guid.NewGuid():N}")
            .Options;

        Context = new CourtBookDbContext(options);
        Context.Database.EnsureCreated();

        // Default: 2026-11-20 03:00 UTC = 10:00 Vietnam time (UTC+7)
        TimeProvider = new MutableTimeProvider(
            initialUtcNow ?? new DateTimeOffset(2026, 11, 20, 3, 0, 0, TimeSpan.Zero));
        Notifier = new RecordingSlotRealtimeNotifier();

        UserRepository = new UserRepository(new UserDao(Context));
        VenueRepository = new VenueRepository(new VenueDao(Context));
        CourtRepository = new CourtRepository(new CourtDao(Context));
        BookingRepository = new BookingRepository(new BookingDao(Context));

        AuthService = new AuthService(UserRepository);
        VenueService = new VenueService(VenueRepository, CourtRepository, BookingRepository, TimeProvider);
        CourtService = new CourtService(CourtRepository, VenueRepository, Notifier);
        BookingService = new BookingService(BookingRepository, CourtRepository, VenueRepository, Notifier, TimeProvider);
        PlatformAdminService = new PlatformAdminService(VenueRepository, UserRepository, BookingRepository);
    }

    public async Task<SeededTestData> SeedMinimalAsync()
    {
        var owner1 = new AppUser
        {
            FullName = "Lê Minh Khôi",
            Email = "khoi@test.vn",
            PhoneNumber = "0902000011",
            PasswordHash = PasswordHelper.HashPassword("Owner@123"),
            Role = UserRole.CourtOwner,
            IsActive = true
        };
        var owner2 = new AppUser
        {
            FullName = "Phạm Thu Hà",
            Email = "ha@test.vn",
            PhoneNumber = "0902000022",
            PasswordHash = PasswordHelper.HashPassword("Owner@123"),
            Role = UserRole.CourtOwner,
            IsActive = true
        };
        var member1 = new AppUser
        {
            FullName = "Nguyễn Văn An",
            Email = "an@test.vn",
            PhoneNumber = "0909111222",
            PasswordHash = PasswordHelper.HashPassword("User@123"),
            Role = UserRole.Member,
            IsActive = true
        };
        var member2 = new AppUser
        {
            FullName = "Trần Thị Bình",
            Email = "binh@test.vn",
            PhoneNumber = "0909333444",
            PasswordHash = PasswordHelper.HashPassword("User@123"),
            Role = UserRole.Member,
            IsActive = true
        };

        Context.Users.AddRange(owner1, owner2, member1, member2);
        await Context.SaveChangesAsync();

        var venue1 = new Venue
        {
            Slug = "thao-dien-club",
            Name = "Thảo Điền Racquet Club",
            District = "TP. Thủ Đức",
            Address = "28 Xuân Thủy, Thảo Điền",
            PhoneNumber = "0902000011",
            Description = "Cụm sân Thủ Đức",
            AccentColor = "emerald",
            OpenHour = 6,
            CloseHour = 22,
            OwnerId = owner1.Id,
            Status = VenueStatus.Active
        };
        var venue2 = new Venue
        {
            Slug = "phu-my-hung-center",
            Name = "Phú Mỹ Hưng Sport Center",
            District = "Quận 7",
            Address = "105 Nguyễn Đức Cảnh, Quận 7",
            PhoneNumber = "0902000022",
            Description = "Cụm sân Quận 7",
            AccentColor = "blue",
            OpenHour = 6,
            CloseHour = 22,
            OwnerId = owner2.Id,
            Status = VenueStatus.Active
        };

        Context.Venues.AddRange(venue1, venue2);
        await Context.SaveChangesAsync();

        var c1 = new Court
        {
            VenueId = venue1.Id,
            Code = "PB-01",
            Name = "Sân Pickleball 1",
            SportType = SportType.Pickleball,
            IsIndoor = true,
            Surface = "Thảm Acrylic Pro",
            BasePricePerHour = 140_000m,
            PeakPricePerHour = 190_000m,
            IsActive = true,
            DisplayOrder = 1
        };
        var c2 = new Court
        {
            VenueId = venue1.Id,
            Code = "BD-01",
            Name = "Sân Cầu lông 1",
            SportType = SportType.Badminton,
            IsIndoor = true,
            Surface = "Thảm PVC Yonex",
            BasePricePerHour = 100_000m,
            PeakPricePerHour = 140_000m,
            IsActive = true,
            DisplayOrder = 2
        };
        var c3 = new Court
        {
            VenueId = venue2.Id,
            Code = "PB-01",
            Name = "Sân PMH Pickleball 1",
            SportType = SportType.Pickleball,
            IsIndoor = true,
            Surface = "Thảm Acrylic Pro",
            BasePricePerHour = 130_000m,
            PeakPricePerHour = 175_000m,
            IsActive = true,
            DisplayOrder = 1
        };

        Context.Courts.AddRange(c1, c2, c3);
        await Context.SaveChangesAsync();

        return new SeededTestData(owner1, owner2, member1, member2, venue1, venue2, c1, c2, c3);
    }

    public void Dispose() => Context.Dispose();
}
