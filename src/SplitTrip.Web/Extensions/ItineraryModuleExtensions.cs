using SplitTrip.Services.Itinerary;
using SplitTrip.Web.Workers;

namespace SplitTrip.Web.Extensions;

public static class ItineraryModuleExtensions
{
    public static IServiceCollection AddItineraryModule(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.AddSplitTripItineraryServices();
        services.AddHostedService<DepartureReminderWorker>();
        return services;
    }
}
