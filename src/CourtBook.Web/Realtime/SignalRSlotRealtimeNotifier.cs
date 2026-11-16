using CourtBook.Services.Interfaces;
using CourtBook.Services.Models;
using CourtBook.Web.Hubs;
using Microsoft.AspNetCore.SignalR;

namespace CourtBook.Web.Realtime;

/// <summary>
/// Pushes slot and court state updates to SignalR clients scoped by venue group.
/// </summary>
public class SignalRSlotRealtimeNotifier : ISlotRealtimeNotifier
{
    private readonly IHubContext<CourtHub> _hubContext;
    private readonly ILogger<SignalRSlotRealtimeNotifier> _logger;

    public SignalRSlotRealtimeNotifier(
        IHubContext<CourtHub> hubContext,
        ILogger<SignalRSlotRealtimeNotifier> logger)
    {
        _hubContext = hubContext;
        _logger = logger;
    }

    public async Task NotifySlotUpdatedAsync(SlotUpdateBroadcastDto update)
    {
        if (update is null)
        {
            return;
        }

        try
        {
            await _hubContext.Clients.Group($"venue-{update.VenueId}").SendAsync("SlotUpdated", update);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to broadcast SlotUpdated for venue {VenueId} court {CourtId} hour {StartHour}.", update.VenueId, update.CourtId, update.StartHour);
        }
    }

    public async Task NotifySlotsUpdatedAsync(IEnumerable<SlotUpdateBroadcastDto> updates)
    {
        IReadOnlyCollection<SlotUpdateBroadcastDto> batch = updates as IReadOnlyCollection<SlotUpdateBroadcastDto>
            ?? updates.ToList();

        if (batch.Count == 0)
        {
            return;
        }

        try
        {
            foreach (IGrouping<int, SlotUpdateBroadcastDto> venueGroup in batch.GroupBy(u => u.VenueId))
            {
                await _hubContext.Clients.Group($"venue-{venueGroup.Key}")
                    .SendAsync("SlotsUpdated", venueGroup.ToList());
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to broadcast SlotsUpdated batch ({Count} items).", batch.Count);
        }
    }

    public async Task NotifyCourtStatusChangedAsync(int venueId, int courtId, bool isActive)
    {
        try
        {
            await _hubContext.Clients.Group($"venue-{venueId}")
                .SendAsync("CourtStatusChanged", new { venueId, courtId, isActive });
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to broadcast CourtStatusChanged for venue {VenueId} court {CourtId}.", venueId, courtId);
        }
    }
}
