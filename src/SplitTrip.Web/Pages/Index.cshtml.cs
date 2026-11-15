using Microsoft.AspNetCore.Mvc.RazorPages;
using SplitTrip.Services.Auth;
using SplitTrip.Services.Trips;
using SplitTrip.Services.Trips.Dtos;
using SplitTrip.Web.Infrastructure;

namespace SplitTrip.Web.Pages;

public sealed class IndexModel(
    ITripService tripService,
    IAuthService authService) : PageModel
{
    public bool IsAuthenticated { get; private set; }
    public bool IsSystemAdmin { get; private set; }
    public bool HasBankConfigured { get; private set; }
    public IReadOnlyList<TripSummaryDto> Trips { get; private set; } = [];

    public async Task OnGetAsync(CancellationToken ct)
    {
        IsAuthenticated = User.Identity?.IsAuthenticated == true;
        if (!IsAuthenticated)
        {
            return;
        }

        IsSystemAdmin = User.IsSystemAdmin();
        if (IsSystemAdmin)
        {
            return;
        }

        if (User.TryGetUserId(out var userId))
        {
            Trips = await tripService.GetUserTripsAsync(userId, ct);
            var profile = await authService.GetProfileAsync(userId, ct);
            HasBankConfigured = !string.IsNullOrWhiteSpace(profile?.BankBin ?? profile?.BankCode)
                                && !string.IsNullOrWhiteSpace(profile?.BankAccountNumber);
        }
    }
}
