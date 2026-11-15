using SplitTrip.BusinessObjects.Enums;
using SplitTrip.Services.Itinerary.Dtos;

namespace SplitTrip.Services.Itinerary;

public interface IItineraryService
{
    Task<TripScheduleDto> GetScheduleAsync(
        Guid tripId,
        Guid viewerUserId,
        bool isAdmin = false,
        CancellationToken ct = default);

    Task<TripDayDto> UpdateDayAsync(
        Guid tripId,
        Guid tripDayId,
        Guid actorUserId,
        UpdateTripDayRequest request,
        CancellationToken ct = default);

    Task<ItineraryItemDto> AddItemAsync(
        Guid tripId,
        Guid actorUserId,
        UpsertItineraryItemRequest request,
        CancellationToken ct = default);

    Task<ItineraryItemDto> UpdateItemAsync(
        Guid tripId,
        Guid itemId,
        Guid actorUserId,
        UpsertItineraryItemRequest request,
        CancellationToken ct = default);

    Task DeleteItemAsync(
        Guid tripId,
        Guid itemId,
        Guid actorUserId,
        CancellationToken ct = default);

    Task<TripScheduleDto> ReorderItemsAsync(
        Guid tripId,
        Guid targetTripDayId,
        IReadOnlyList<Guid> orderedItemIds,
        CancellationToken ct = default);

    Task<TripScheduleDto> ReorderItemsAsync(
        Guid tripId,
        Guid targetTripDayId,
        Guid actorUserId,
        IReadOnlyList<Guid> orderedItemIds,
        CancellationToken ct = default);

    Task<VoteResultDto> ToggleVoteAsync(
        Guid tripId,
        Guid itemId,
        Guid userId,
        VoteType voteType,
        CancellationToken ct = default);

    Task<IReadOnlyList<DepartureReminderDispatchDto>> ProcessUpcomingDepartureRemindersAsync(
        DateTime? utcNowOverride = null,
        CancellationToken ct = default);
}
