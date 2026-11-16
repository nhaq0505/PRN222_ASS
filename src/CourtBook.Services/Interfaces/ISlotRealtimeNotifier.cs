using CourtBook.Services.Models;

namespace CourtBook.Services.Interfaces;

public interface ISlotRealtimeNotifier
{
    Task NotifySlotUpdatedAsync(SlotUpdateBroadcastDto update);
    Task NotifySlotsUpdatedAsync(IEnumerable<SlotUpdateBroadcastDto> updates);
    Task NotifyCourtStatusChangedAsync(int venueId, int courtId, bool isActive);
}
