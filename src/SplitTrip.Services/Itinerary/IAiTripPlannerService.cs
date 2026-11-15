using SplitTrip.Services.Itinerary.Dtos;

namespace SplitTrip.Services.Itinerary;

public interface IAiTripPlannerService
{
    Task<AiTripPlanPreviewDto> PreviewPlanAsync(
        Guid tripId,
        Guid actorUserId,
        AiTripPlanRequestDto request,
        CancellationToken ct = default);

    Task<TripScheduleDto> ApplyPlanAsync(
        Guid tripId,
        Guid actorUserId,
        ApplyAiTripPlanRequestDto request,
        CancellationToken ct = default);

    Task<TripScheduleDto> GenerateAndApplyPlanAsync(
        Guid tripId,
        Guid actorUserId,
        AiTripPlanRequestDto request,
        bool clearExistingAiItems = false,
        CancellationToken ct = default);
}
