using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using SplitTrip.BusinessObjects.Options;
using SplitTrip.DataAccessObjects;
using SplitTrip.DataAccessObjects.Daos;
using SplitTrip.Repositories.Notifications;
using SplitTrip.Repositories.Trips;
using SplitTrip.Repositories.Users;
using SplitTrip.Services.Auth;
using SplitTrip.Services.Notifications;
using SplitTrip.Services.Trips;

namespace SplitTrip.Services;

public static class ServiceCollectionExtensions
{
    private const string DefaultLocalDbConnectionString =
        "Server=(localdb)\\mssqllocaldb;Database=SplitTripDb;Trusted_Connection=True;MultipleActiveResultSets=true;TrustServerCertificate=True";

    public static IServiceCollection AddSplitTripFoundation(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.Configure<AdminAccountOptions>(configuration.GetSection(AdminAccountOptions.SectionName));
        services.Configure<GeminiOptions>(configuration.GetSection(GeminiOptions.SectionName));

        var connectionString = configuration.GetConnectionString("DefaultConnection");
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            connectionString = DefaultLocalDbConnectionString;
        }

        services.AddDbContext<SplitTripDbContext>(options =>
            options.UseSqlServer(connectionString));

        services.AddScoped<UserDao>();
        services.AddScoped<TripDao>();
        services.AddScoped<TripMemberDao>();
        services.AddScoped<NotificationDao>();

        services.AddScoped<IUserRepository, UserRepository>();
        services.AddScoped<ITripRepository, TripRepository>();
        services.AddScoped<ITripMemberRepository, TripMemberRepository>();
        services.AddScoped<INotificationRepository, NotificationRepository>();

        services.AddScoped<IAuthService, AuthService>();
        services.AddScoped<ITripService, TripService>();
        services.AddScoped<INotificationService, NotificationService>();

        return services;
    }

    public static async Task MigrateSplitTripDatabaseAsync(
        this IServiceProvider services,
        CancellationToken ct = default)
    {
        await using var scope = services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<SplitTripDbContext>();
        if (dbContext.Database.IsRelational())
        {
            await dbContext.Database.MigrateAsync(ct);
        }
        else
        {
            await dbContext.Database.EnsureCreatedAsync(ct);
        }
    }
}
