using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using SplitTrip.BusinessObjects.Entities;
using SplitTrip.BusinessObjects.Options;
using SplitTrip.DataAccessObjects;
using SplitTrip.DataAccessObjects.Daos;
using SplitTrip.DataAccessObjects.Daos.Itinerary;
using SplitTrip.Repositories.Itinerary;
using SplitTrip.Repositories.Notifications;
using SplitTrip.Repositories.Trips;
using SplitTrip.Repositories.Users;
using SplitTrip.Services.Auth;
using SplitTrip.Services.Itinerary;
using SplitTrip.Services.Notifications;
using SplitTrip.Services.Trips;
using SplitTrip.Services.Trips.Dtos;
using SplitTrip.Web.Hubs;
using SplitTrip.Web.Workers;

namespace SplitTrip.Tests.Itinerary;

internal sealed class ItineraryTestHarness : IAsyncDisposable
{
    public ServiceProvider ServiceProvider { get; }
    public SplitTripDbContext DbContext { get; }
    public IAuthService AuthService { get; }
    public ITripService TripService { get; }
    public IItineraryService ItineraryService { get; }
    public INotificationService NotificationService { get; }
    public FakeTripHubContext FakeHubContext { get; }

    public ItineraryTestHarness(string apiKey = "", HttpClient? customHttpClient = null)
    {
        var dbName = $"SplitTrip_Itinerary_{Guid.NewGuid():N}";
        var services = new ServiceCollection();

        services.AddDbContext<SplitTripDbContext>(opts => opts.UseInMemoryDatabase(dbName));
        services.AddSingleton<IOptions<AdminAccountOptions>>(Options.Create(new AdminAccountOptions
        {
            Email = "admin@splittrip.vn",
            Password = "Admin@123",
            FullName = "System Admin"
        }));
        services.AddSingleton<IOptions<GeminiOptions>>(Options.Create(new GeminiOptions
        {
            ApiKey = apiKey,
            Model = "gemini-2.5-flash"
        }));

        services.AddScoped<UserDao>();
        services.AddScoped<TripDao>();
        services.AddScoped<TripMemberDao>();
        services.AddScoped<NotificationDao>();
        services.AddScoped<TripDayDao>();
        services.AddScoped<ItineraryItemDao>();
        services.AddScoped<ItineraryVoteDao>();

        services.AddScoped<IUserRepository, UserRepository>();
        services.AddScoped<ITripRepository, TripRepository>();
        services.AddScoped<ITripMemberRepository, TripMemberRepository>();
        services.AddScoped<INotificationRepository, NotificationRepository>();
        services.AddScoped<ITripDayRepository, TripDayRepository>();
        services.AddScoped<IItineraryItemRepository, ItineraryItemRepository>();
        services.AddScoped<IItineraryVoteRepository, ItineraryVoteRepository>();

        services.AddScoped<IAuthService, AuthService>();
        services.AddScoped<ITripService, TripService>();
        services.AddScoped<INotificationService, NotificationService>();
        services.AddScoped<IItineraryService, ItineraryService>();
        services.AddScoped<IAiTripPlannerService>(sp => new GeminiTripPlannerService(
            sp.GetRequiredService<ITripRepository>(),
            sp.GetRequiredService<ITripDayRepository>(),
            sp.GetRequiredService<IItineraryItemRepository>(),
            sp.GetRequiredService<ITripService>(),
            sp.GetRequiredService<IOptions<GeminiOptions>>(),
            customHttpClient));

        FakeHubContext = new FakeTripHubContext();
        services.AddSingleton<IHubContext<TripHub>>(FakeHubContext);

        ServiceProvider = services.BuildServiceProvider();
        DbContext = ServiceProvider.GetRequiredService<SplitTripDbContext>();
        AuthService = ServiceProvider.GetRequiredService<IAuthService>();
        TripService = ServiceProvider.GetRequiredService<ITripService>();
        ItineraryService = ServiceProvider.GetRequiredService<IItineraryService>();
        NotificationService = ServiceProvider.GetRequiredService<INotificationService>();
    }

    public IAiTripPlannerService CreateAiPlanner(string apiKey = "", HttpClient? httpClient = null)
        => new GeminiTripPlannerService(
            ServiceProvider.GetRequiredService<ITripRepository>(),
            ServiceProvider.GetRequiredService<ITripDayRepository>(),
            ServiceProvider.GetRequiredService<IItineraryItemRepository>(),
            TripService,
            Options.Create(new GeminiOptions { ApiKey = apiKey, Model = "gemini-2.5-flash" }),
            httpClient);

    public DepartureReminderWorker CreateReminderWorker()
        => new(
            ServiceProvider.GetRequiredService<IServiceScopeFactory>(),
            FakeHubContext,
            NullLogger<DepartureReminderWorker>.Instance);

    public async Task<User> CreateUserAsync(string email, string fullName)
    {
        var user = new User
        {
            Id = Guid.NewGuid(),
            Email = email,
            PasswordHash = "hashed",
            FullName = fullName,
            CreatedAtUtc = DateTime.UtcNow
        };
        DbContext.Users.Add(user);
        await DbContext.SaveChangesAsync();
        return user;
    }

    public Task<TripDetailsDto> CreateSampleTripAsync(
        Guid ownerId,
        string destination = "Đà Lạt",
        DateOnly? startDate = null,
        int days = 3,
        decimal budgetPerPerson = 2_500_000m)
    {
        var start = startDate ?? new DateOnly(2026, 12, 10);
        return TripService.CreateTripAsync(
            ownerId,
            new CreateTripRequest(
                $"Chuyến đi {destination}",
                destination,
                "Du lịch nhóm cuối tuần",
                start,
                start.AddDays(days - 1),
                budgetPerPerson));
    }

    public ValueTask DisposeAsync() => ServiceProvider.DisposeAsync();
}

internal sealed class FakeTripHubContext : IHubContext<TripHub>
{
    public FakeHubClients FakeClients { get; } = new();
    public IHubClients Clients => FakeClients;
    public IGroupManager Groups => throw new NotSupportedException();
}

internal sealed class FakeHubClients : IHubClients
{
    public List<(string GroupName, string Method, object?[] Args)> GroupMessages { get; } = [];

    public IClientProxy All => new RecordingClientProxy("all", GroupMessages);
    public IClientProxy AllExcept(IReadOnlyList<string> excludedConnectionIds) => new RecordingClientProxy("allExcept", GroupMessages);
    public IClientProxy Client(string connectionId) => new RecordingClientProxy($"client:{connectionId}", GroupMessages);
    public IClientProxy Clients(IReadOnlyList<string> connectionIds) => new RecordingClientProxy("clients", GroupMessages);
    public IClientProxy Group(string groupName) => new RecordingClientProxy(groupName, GroupMessages);
    public IClientProxy GroupExcept(string groupName, IReadOnlyList<string> excludedConnectionIds) => new RecordingClientProxy(groupName, GroupMessages);
    public IClientProxy Groups(IReadOnlyList<string> groupNames) => new RecordingClientProxy(string.Join(",", groupNames), GroupMessages);
    public IClientProxy User(string userId) => new RecordingClientProxy($"user:{userId}", GroupMessages);
    public IClientProxy Users(IReadOnlyList<string> userIds) => new RecordingClientProxy("users", GroupMessages);
}

internal sealed class RecordingClientProxy(
    string targetName,
    List<(string GroupName, string Method, object?[] Args)> sink) : IClientProxy
{
    public Task SendCoreAsync(string method, object?[] args, CancellationToken cancellationToken = default)
    {
        sink.Add((targetName, method, args));
        return Task.CompletedTask;
    }
}
