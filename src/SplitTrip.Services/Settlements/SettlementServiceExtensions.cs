using Microsoft.Extensions.DependencyInjection;
using SplitTrip.DataAccessObjects.Daos.Settlements;
using SplitTrip.Repositories.Settlements;

namespace SplitTrip.Services.Settlements;

public static class SettlementServiceExtensions
{
    public static IServiceCollection AddSplitTripSettlements(this IServiceCollection services)
    {
        services.AddScoped<SettlementDao>();
        services.AddScoped<AdminAnalyticsDao>();

        services.AddScoped<ISettlementRepository, SettlementRepository>();
        services.AddScoped<IAdminAnalyticsRepository, AdminAnalyticsRepository>();

        services.AddScoped<IDebtSimplificationService, MinCashFlowService>();
        services.AddScoped<IVietQrService, VietQrService>();
        services.AddScoped<ISettlementService, SettlementService>();
        services.AddScoped<IAdminAnalyticsService, AdminAnalyticsService>();

        return services;
    }
}
