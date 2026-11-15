using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using SplitTrip.BusinessObjects.Options;
using SplitTrip.DataAccessObjects;
using SplitTrip.DataAccessObjects.Daos;
using SplitTrip.Repositories.Notifications;
using SplitTrip.Repositories.Trips;
using SplitTrip.Repositories.Users;
using SplitTrip.Services.Auth;
using SplitTrip.Services.Notifications;
using SplitTrip.Services.Trips;

namespace SplitTrip.Tests.Foundation;

internal sealed class FoundationTestHarness : IAsyncDisposable
{
    public SplitTripDbContext DbContext { get; }
    public IAuthService AuthService { get; }
    public ITripService TripService { get; }
    public INotificationService NotificationService { get; }

    public FoundationTestHarness()
    {
        var options = new DbContextOptionsBuilder<SplitTripDbContext>()
            .UseInMemoryDatabase($"SplitTrip_Foundation_{Guid.NewGuid():N}")
            .Options;

        DbContext = new SplitTripDbContext(options);

        var userDao = new UserDao(DbContext);
        var tripDao = new TripDao(DbContext);
        var tripMemberDao = new TripMemberDao(DbContext);
        var notificationDao = new NotificationDao(DbContext);

        var userRepo = new UserRepository(userDao);
        var tripRepo = new TripRepository(tripDao);
        var tripMemberRepo = new TripMemberRepository(tripMemberDao);
        var notificationRepo = new NotificationRepository(notificationDao);

        var adminOptions = Options.Create(new AdminAccountOptions
        {
            Email = "admin@splittrip.vn",
            Password = "Admin@123",
            FullName = "SplitTrip System Admin"
        });

        AuthService = new AuthService(userRepo, adminOptions);
        TripService = new TripService(tripRepo, tripMemberRepo, userRepo, notificationRepo);
        NotificationService = new NotificationService(notificationRepo);
    }

    public ValueTask DisposeAsync() => DbContext.DisposeAsync();
}
