using System.Globalization;
using CourtBook.BusinessObjects.Entities;
using CourtBook.BusinessObjects.Enums;
using CourtBook.Repositories;
using CourtBook.Services.Interfaces;
using CourtBook.Services.Models;

namespace CourtBook.Services.Helpers;

/// <summary>
/// Expires active temporary holds and emits batched SignalR slot reset broadcasts.
/// </summary>
internal static class HoldExpirationHelper
{
    public static async Task<int> ExpireHoldsAndBroadcastAsync(
        IReadOnlyList<Booking> holds,
        IBookingRepository bookingRepository,
        ISlotRealtimeNotifier notifier,
        int? fallbackVenueId = null,
        bool? overrideCourtActive = null,
        CancellationToken ct = default)
    {
        if (holds.Count == 0)
        {
            return 0;
        }

        var broadcasts = new List<SlotUpdateBroadcastDto>(holds.Count);
        foreach (Booking hold in holds)
        {
            hold.Status = BookingStatus.Expired;
            hold.HoldExpiresAtUtc = null;

            broadcasts.Add(new SlotUpdateBroadcastDto
            {
                VenueId = hold.Court?.VenueId ?? fallbackVenueId ?? 0,
                PlayDate = hold.PlayDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                CourtId = hold.CourtId,
                StartHour = hold.StartHour,
                Status = BookingStatus.Expired,
                BookingId = hold.Id,
                UserId = hold.UserId,
                PriceVnd = hold.Court?.GetPriceForHour(hold.StartHour) ?? hold.PriceVnd,
                HoldExpiresAtUtc = null,
                IsCourtActive = overrideCourtActive ?? hold.Court?.IsActive ?? true
            });
        }

        await bookingRepository.UpdateRangeAsync(holds, ct);
        await notifier.NotifySlotsUpdatedAsync(broadcasts);
        return holds.Count;
    }
}
