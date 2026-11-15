using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;
using SplitTrip.BusinessObjects.Enums;
using SplitTrip.Services.Auth;
using SplitTrip.Services.Itinerary;
using SplitTrip.Services.Itinerary.Dtos;
using SplitTrip.Services.Trips;

namespace SplitTrip.Web.Hubs;

public partial class TripHub
{
    public async Task NotifyItineraryChanged(
        Guid tripId,
        string changeType,
        [FromServices] ITripService tripService)
    {
        var userId = GetAuthenticatedUserId();
        var isAdmin = Context.User?.IsInRole(AuthService.AdminRole) == true;
        if (!isAdmin && !await tripService.IsActiveMemberAsync(tripId, userId, Context.ConnectionAborted))
        {
            throw new HubException("Bạn không có quyền đồng bộ lịch trình của chuyến đi này.");
        }

        await Clients.OthersInGroup(TripGroupName(tripId))
            .SendAsync(
                "ItineraryChanged",
                new
                {
                    tripId,
                    changeType = string.IsNullOrWhiteSpace(changeType) ? "updated" : changeType.Trim(),
                    actorUserId = userId,
                    timestampUtc = DateTime.UtcNow
                },
                Context.ConnectionAborted);
    }

    public async Task<VoteResultDto> ToggleItineraryVote(
        Guid tripId,
        Guid itemId,
        VoteType voteType,
        [FromServices] IItineraryService itineraryService)
    {
        var userId = GetAuthenticatedUserId();

        try
        {
            var result = await itineraryService.ToggleVoteAsync(
                tripId,
                itemId,
                userId,
                voteType,
                Context.ConnectionAborted);

            await Clients.Group(TripGroupName(tripId))
                .SendAsync("ItineraryVoteUpdated", result, Context.ConnectionAborted);

            return result;
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or UnauthorizedAccessException or KeyNotFoundException)
        {
            throw new HubException(ex.Message);
        }
    }

    public async Task ReorderItineraryItems(
        Guid tripId,
        Guid targetTripDayId,
        List<Guid> orderedItemIds,
        [FromServices] IItineraryService itineraryService)
    {
        var userId = GetAuthenticatedUserId();

        try
        {
            await itineraryService.ReorderItemsAsync(
                tripId,
                targetTripDayId,
                userId,
                orderedItemIds ?? [],
                Context.ConnectionAborted);

            await Clients.Group(TripGroupName(tripId))
                .SendAsync(
                    "ItineraryReordered",
                    new
                    {
                        tripId,
                        targetTripDayId,
                        orderedItemIds = orderedItemIds ?? [],
                        actorUserId = userId,
                        timestampUtc = DateTime.UtcNow
                    },
                    Context.ConnectionAborted);
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or UnauthorizedAccessException or KeyNotFoundException)
        {
            throw new HubException(ex.Message);
        }
    }
}
