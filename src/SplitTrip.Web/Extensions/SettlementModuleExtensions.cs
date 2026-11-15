using SplitTrip.Services.Settlements;
using SplitTrip.Web.Workers;

namespace SplitTrip.Web.Extensions;

public static class SettlementModuleExtensions
{
    public static IServiceCollection AddSettlementModule(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.AddSplitTripSettlements();
        services.AddHostedService<TripSettlementWorker>();

        return services;
    }
}
