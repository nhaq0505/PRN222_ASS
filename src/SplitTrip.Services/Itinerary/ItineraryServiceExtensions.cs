using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using SplitTrip.BusinessObjects.Options;
using SplitTrip.DataAccessObjects.Daos.Itinerary;
using SplitTrip.Repositories.Itinerary;
using SplitTrip.Repositories.Trips;
using SplitTrip.Services.Trips;

namespace SplitTrip.Services.Itinerary;

public static class ItineraryServiceExtensions
{
    public static IServiceCollection AddSplitTripItineraryServices(this IServiceCollection services)
    {
        services.AddScoped<TripDayDao>();
        services.AddScoped<ItineraryItemDao>();
        services.AddScoped<ItineraryVoteDao>();

        services.AddScoped<ITripDayRepository, TripDayRepository>();
        services.AddScoped<IItineraryItemRepository, ItineraryItemRepository>();
        services.AddScoped<IItineraryVoteRepository, ItineraryVoteRepository>();

        services.AddScoped<IItineraryService, ItineraryService>();
        services.AddScoped<IAiTripPlannerService>(sp => new GeminiTripPlannerService(
            sp.GetRequiredService<ITripRepository>(),
            sp.GetRequiredService<ITripDayRepository>(),
            sp.GetRequiredService<IItineraryItemRepository>(),
            sp.GetRequiredService<ITripService>(),
            sp.GetRequiredService<IOptions<GeminiOptions>>()));

        return services;
    }
}
