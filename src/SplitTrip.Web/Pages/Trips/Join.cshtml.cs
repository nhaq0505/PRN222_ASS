using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.SignalR;
using SplitTrip.Services.Trips;
using SplitTrip.Web.Hubs;
using SplitTrip.Web.Infrastructure;

namespace SplitTrip.Web.Pages.Trips;

[Authorize]
public sealed class JoinModel(
    ITripService tripService,
    IHubContext<TripHub> hubContext) : PageModel
{
    [BindProperty(SupportsGet = true)]
    [Required(ErrorMessage = "Vui lòng nhập mã mời 8 ký tự.")]
    [StringLength(16, MinimumLength = 4, ErrorMessage = "Mã mời không hợp lệ.")]
    public string Code { get; set; } = string.Empty;

    public string? ErrorMessage { get; private set; }

    public IActionResult OnGet()
    {
        if (User.IsSystemAdmin())
        {
            return RedirectToPage("/Admin/Index");
        }

        return Page();
    }

    public async Task<IActionResult> OnPostAsync(CancellationToken ct)
    {
        if (User.IsSystemAdmin())
        {
            return RedirectToPage("/Admin/Index");
        }

        if (!ModelState.IsValid)
        {
            return Page();
        }

        var userId = User.GetRequiredUserId();
        var userName = User.FindFirstValue(ClaimTypes.Name) ?? "Thành viên mới";

        try
        {
            var result = await tripService.JoinByInviteCodeAsync(userId, Code, ct);
            var details = await tripService.GetTripDetailsAsync(result.TripId, userId, isAdmin: false, ct);

            await hubContext.Clients.Group(TripHub.TripGroupName(result.TripId))
                .SendAsync(
                    "TripMembersChanged",
                    new
                    {
                        tripId = result.TripId,
                        message = $"{userName} vừa tham gia chuyến đi!"
                    },
                    ct);

            if (details.OwnerUserId != userId)
            {
                await hubContext.Clients.Group(TripHub.UserGroupName(details.OwnerUserId))
                    .SendAsync(
                        "ReceiveNotification",
                        new
                        {
                            title = "Thành viên mới tham gia chuyến đi",
                            message = $"{userName} đã tham gia chuyến đi \"{details.Name}\".",
                            actionUrl = $"/Trips/Details?id={details.Id}"
                        },
                        ct);
            }

            TempData["SuccessMessage"] = result.WasReactivated
                ? $"Chào mừng bạn quay lại chuyến đi \"{result.TripName}\"!"
                : $"Bạn đã tham gia chuyến đi \"{result.TripName}\" thành công!";

            return RedirectToPage("/Trips/Details", new { id = result.TripId });
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or KeyNotFoundException)
        {
            ErrorMessage = ex.Message;
            return Page();
        }
    }
}
