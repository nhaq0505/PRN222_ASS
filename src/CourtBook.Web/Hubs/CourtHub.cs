using Microsoft.AspNetCore.SignalR;

namespace CourtBook.Web.Hubs;

/// <summary>
/// SignalR hub broadcasting venue-isolated live slot hold, booking, expiry, and court maintenance changes.
/// </summary>
public class CourtHub : Hub
{
    public Task JoinVenueGroup(int venueId)
        => venueId > 0
            ? Groups.AddToGroupAsync(Context.ConnectionId, $"venue-{venueId}")
            : Task.CompletedTask;

    public Task LeaveVenueGroup(int venueId)
        => venueId > 0
            ? Groups.RemoveFromGroupAsync(Context.ConnectionId, $"venue-{venueId}")
            : Task.CompletedTask;
}
