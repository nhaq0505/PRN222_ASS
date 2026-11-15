using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.SignalR;
using SplitTrip.BusinessObjects.Enums;
using SplitTrip.Services.Trips;
using SplitTrip.Services.Trips.Dtos;
using SplitTrip.Web.Hubs;
using SplitTrip.Web.Infrastructure;

namespace SplitTrip.Web.Pages.Trips;

[Authorize]
public sealed class DetailsModel(
    ITripService tripService,
    IHubContext<TripHub> hubContext) : PageModel
{
    public TripDetailsDto Trip { get; private set; } = default!;
    public Guid CurrentUserId { get; private set; }
    public bool IsOwner { get; private set; }
    public bool IsAdmin { get; private set; }

    public async Task<IActionResult> OnGetAsync(Guid? id, Guid? tripId, CancellationToken ct)
    {
        var resolvedId = id ?? tripId;
        if (resolvedId is null || resolvedId == Guid.Empty)
        {
            return RedirectToPage("/Index");
        }

        try
        {
            await LoadWorkspaceAsync(resolvedId.Value, ct);
            return Page();
        }
        catch (KeyNotFoundException ex)
        {
            TempData["ErrorMessage"] = ex.Message;
            return RedirectToPage("/Index");
        }
        catch (UnauthorizedAccessException ex)
        {
            TempData["ErrorMessage"] = ex.Message;
            return RedirectToPage("/Index");
        }
    }

    public async Task<IActionResult> OnGetLiveStateAsync(Guid id, CancellationToken ct)
    {
        try
        {
            await LoadWorkspaceAsync(id, ct);
            var (statusLabel, statusCss) = DescribeStatus(Trip.Status);

            return new JsonResult(new
            {
                tripId = Trip.Id,
                status = Trip.Status.ToString(),
                statusLabel,
                statusCss,
                activeMemberCount = Trip.MemberBalances.Count(m => m.IsActive),
                totalFinalizedSpend = Trip.MemberBalances.Sum(m => m.TotalPaid),
                ledgerNetSum = Trip.MemberBalances.Sum(m => m.NetBalance),
                members = Trip.MemberBalances.Select(m => new
                {
                    userId = m.UserId,
                    fullName = m.FullName,
                    email = m.Email,
                    role = m.Role.ToString(),
                    isOwner = m.Role == TripMemberRole.Owner,
                    hasBank = !string.IsNullOrWhiteSpace(m.BankBin ?? m.BankCode) && !string.IsNullOrWhiteSpace(m.BankAccountNumber),
                    bankCode = m.BankCode,
                    totalPaid = m.TotalPaid,
                    totalOwed = m.TotalOwed,
                    netSettled = m.SettlementsPaid - m.SettlementsReceived,
                    netBalance = m.NetBalance,
                    isSelf = m.UserId == CurrentUserId,
                    canAct = Trip.Status != TripStatus.Completed
                             && m.Role != TripMemberRole.Owner
                             && (IsOwner || m.UserId == CurrentUserId)
                })
            });
        }
        catch (Exception ex) when (ex is KeyNotFoundException or UnauthorizedAccessException)
        {
            return new JsonResult(new { error = ex.Message }) { StatusCode = StatusCodes.Status403Forbidden };
        }
    }

    public async Task<IActionResult> OnPostTransitionStatusAsync(
        Guid id,
        TripStatus targetStatus,
        CancellationToken ct)
    {
        var actorId = User.GetRequiredUserId();
        var actorName = User.FindFirstValue(ClaimTypes.Name) ?? "Trưởng đoàn";

        try
        {
            var newStatus = await tripService.TransitionStatusAsync(id, actorId, targetStatus, ct);
            var (statusLabel, _) = DescribeStatus(newStatus);

            await hubContext.Clients.Group(TripHub.TripGroupName(id))
                .SendAsync(
                    "TripStatusChanged",
                    new
                    {
                        tripId = id,
                        status = newStatus.ToString(),
                        statusLabel,
                        message = $"{actorName} đã chuyển trạng thái chuyến đi sang \"{statusLabel}\"."
                    },
                    ct);

            TempData["SuccessMessage"] = $"Đã chuyển trạng thái chuyến đi sang \"{statusLabel}\".";
        }
        catch (Exception ex) when (ex is InvalidOperationException or UnauthorizedAccessException or KeyNotFoundException)
        {
            TempData["ErrorMessage"] = ex.Message;
        }

        return RedirectToPage("/Trips/Details", new { id });
    }

    public async Task<IActionResult> OnPostRemoveMemberAsync(
        Guid id,
        Guid targetUserId,
        CancellationToken ct)
    {
        var actorId = User.GetRequiredUserId();
        var isSelfLeave = actorId == targetUserId;

        try
        {
            await tripService.RemoveOrLeaveMemberAsync(id, actorId, targetUserId, ct);

            await hubContext.Clients.Group(TripHub.TripGroupName(id))
                .SendAsync(
                    "TripMembersChanged",
                    new
                    {
                        tripId = id,
                        removedUserId = targetUserId,
                        message = isSelfLeave
                            ? "Một thành viên vừa rời chuyến đi."
                            : "Trưởng đoàn vừa cập nhật danh sách thành viên."
                    },
                    ct);

            if (isSelfLeave)
            {
                TempData["SuccessMessage"] = "Bạn đã rời khỏi chuyến đi thành công.";
                return RedirectToPage("/Index");
            }

            TempData["SuccessMessage"] = "Đã xóa thành viên khỏi chuyến đi.";
        }
        catch (Exception ex) when (ex is InvalidOperationException or UnauthorizedAccessException or KeyNotFoundException)
        {
            TempData["ErrorMessage"] = ex.Message;
        }

        return RedirectToPage("/Trips/Details", new { id });
    }

    private async Task LoadWorkspaceAsync(Guid tripId, CancellationToken ct)
    {
        CurrentUserId = User.GetRequiredUserId();
        IsAdmin = User.IsSystemAdmin();
        Trip = await tripService.GetTripDetailsAsync(tripId, CurrentUserId, IsAdmin, ct);
        IsOwner = Trip.OwnerUserId == CurrentUserId;
    }

    public static (string Label, string Css) DescribeStatus(TripStatus status) => status switch
    {
        TripStatus.Planning => ("Đang lên kế hoạch", "st-status-planning"),
        TripStatus.Ongoing => ("Đang diễn ra", "st-status-ongoing"),
        TripStatus.Settling => ("Đang tất toán", "st-status-settling"),
        TripStatus.Completed => ("Đã hoàn tất", "st-status-completed"),
        _ => (status.ToString(), "st-status-completed")
    };
}
