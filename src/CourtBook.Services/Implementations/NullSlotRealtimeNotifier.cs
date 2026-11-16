using CourtBook.Services.Interfaces;
using CourtBook.Services.Models;

namespace CourtBook.Services.Implementations;

public class NullSlotRealtimeNotifier : ISlotRealtimeNotifier
{
    public static readonly NullSlotRealtimeNotifier Instance = new();

    public Task NotifySlotUpdatedAsync(SlotUpdateBroadcastDto update) => Task.CompletedTask;

    public Task NotifySlotsUpdatedAsync(IEnumerable<SlotUpdateBroadcastDto> updates) => Task.CompletedTask;

    public Task NotifyCourtStatusChangedAsync(int venueId, int courtId, bool isActive) => Task.CompletedTask;
}
