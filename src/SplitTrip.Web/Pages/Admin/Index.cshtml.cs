using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using SplitTrip.BusinessObjects.Enums;
using SplitTrip.Services.Auth;
using SplitTrip.Services.Settlements;
using SplitTrip.Services.Settlements.Dtos;
using SplitTrip.Web.Pages.Trips;

namespace SplitTrip.Web.Pages.Admin;

[Authorize(Roles = AuthService.AdminRole)]
public sealed class IndexModel(IAdminAnalyticsService analyticsService) : PageModel
{
    [BindProperty(SupportsGet = true)]
    public DateOnly? StartDate { get; set; }

    [BindProperty(SupportsGet = true)]
    public DateOnly? EndDate { get; set; }

    public string? FilterError { get; private set; }

    public AdminDashboardDto Dashboard { get; private set; } = default!;

    public async Task OnGetAsync(CancellationToken ct)
    {
        try
        {
            Dashboard = await analyticsService.GetDashboardAsync(StartDate, EndDate, ct);
        }
        catch (ArgumentException ex)
        {
            FilterError = ex.Message;
            Dashboard = await analyticsService.GetDashboardAsync(null, null, ct);
        }
    }

    public static (string Label, string Css) DescribeTripStatus(TripStatus status)
        => DetailsModel.DescribeStatus(status);
}
