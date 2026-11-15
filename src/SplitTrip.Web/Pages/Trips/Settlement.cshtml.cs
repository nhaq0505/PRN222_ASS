using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.SignalR;
using SplitTrip.BusinessObjects.Enums;
using SplitTrip.Services.Notifications;
using SplitTrip.Services.Settlements;
using SplitTrip.Services.Settlements.Dtos;
using SplitTrip.Services.Trips;
using SplitTrip.Web.Hubs;
using SplitTrip.Web.Infrastructure;

namespace SplitTrip.Web.Pages.Trips;

[Authorize]
public sealed class SettlementModel(
    ISettlementService settlementService,
    ITripService tripService,
    INotificationService notificationService,
    IHubContext<TripHub> hubContext) : PageModel
{
    public TripSettlementWorkspaceDto Workspace { get; private set; } = default!;
    public Guid CurrentUserId { get; private set; }
    public bool IsOwner { get; private set; }
    public bool IsAdmin { get; private set; }

    public async Task<IActionResult> OnGetAsync(Guid? tripId, Guid? id, CancellationToken ct)
    {
        var resolvedId = tripId ?? id;
        if (resolvedId is null || resolvedId == Guid.Empty)
        {
            return RedirectToPage("/Index");
        }

        try
        {
            await LoadWorkspaceAsync(resolvedId.Value, syncPendingPlan: true, ct);
            return Page();
        }
        catch (Exception ex) when (ex is KeyNotFoundException or UnauthorizedAccessException)
        {
            TempData["ErrorMessage"] = ex.Message;
            return RedirectToPage("/Index");
        }
    }

    public async Task<IActionResult> OnPostSyncPlanAsync(Guid tripId, CancellationToken ct)
    {
        var actorId = User.GetRequiredUserId();
        var isAdmin = User.IsSystemAdmin();

        try
        {
            if (!isAdmin && !await tripService.IsActiveMemberAsync(tripId, actorId, ct))
            {
                throw new UnauthorizedAccessException("Bạn không phải là thành viên đang hoạt động của chuyến đi này.");
            }

            var active = await settlementService.SyncSettlementPlanAsync(tripId, ct);

            await hubContext.Clients.Group(TripHub.TripGroupName(tripId))
                .SendAsync(
                    "SettlementUpdated",
                    new
                    {
                        tripId,
                        status = "Synced",
                        message = $"Đã tối ưu lại kế hoạch tất toán Min-Cash-Flow ({active.Count} giao dịch)."
                    },
                    ct);

            TempData["SuccessMessage"] = $"Đã cập nhật kế hoạch tất toán tối ưu ({active.Count} giao dịch cần thực hiện).";
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or UnauthorizedAccessException or KeyNotFoundException)
        {
            TempData["ErrorMessage"] = ex.Message;
        }

        return RedirectToPage("/Trips/Settlement", new { tripId });
    }

    public async Task<IActionResult> OnPostMarkPaidAsync(
        Guid tripId,
        Guid settlementId,
        string? proofNote,
        CancellationToken ct)
    {
        var actorId = User.GetRequiredUserId();

        try
        {
            var result = await settlementService.MarkAsPaidAsync(settlementId, actorId, proofNote, ct);

            await TripHub.BroadcastSettlementOutcomeAsync(
                hubContext,
                result,
                $"{result.Settlement.FromUserName} đã đánh dấu chuyển khoản {result.Settlement.Amount:N0}đ cho {result.Settlement.ToUserName}.",
                notificationService,
                ct);

            TempData["SuccessMessage"] =
                $"Đã báo chuyển khoản {result.Settlement.Amount:N0}đ cho {result.Settlement.ToUserName}. Đang chờ chủ nợ xác nhận!";
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or UnauthorizedAccessException or KeyNotFoundException)
        {
            TempData["ErrorMessage"] = ex.Message;
        }

        return RedirectToPage("/Trips/Settlement", new { tripId });
    }

    public async Task<IActionResult> OnPostConfirmReceivedAsync(
        Guid tripId,
        Guid settlementId,
        CancellationToken ct)
    {
        var actorId = User.GetRequiredUserId();

        try
        {
            var result = await settlementService.ConfirmReceivedAsync(settlementId, actorId, ct);

            await TripHub.BroadcastSettlementOutcomeAsync(
                hubContext,
                result,
                $"{result.Settlement.ToUserName} đã xác nhận nhận đủ {result.Settlement.Amount:N0}đ từ {result.Settlement.FromUserName}.",
                notificationService,
                ct);

            TempData["SuccessMessage"] = result.TripCompleted
                ? $"Đã xác nhận nhận {result.Settlement.Amount:N0}đ! Tất cả công nợ đã cân bằng 0đ — chuyến đi hoàn tất!"
                : $"Đã xác nhận nhận đủ {result.Settlement.Amount:N0}đ từ {result.Settlement.FromUserName}.";
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or UnauthorizedAccessException or KeyNotFoundException)
        {
            TempData["ErrorMessage"] = ex.Message;
        }

        return RedirectToPage("/Trips/Settlement", new { tripId });
    }

    public async Task<IActionResult> OnPostDisputeAsync(
        Guid tripId,
        Guid settlementId,
        string disputeReason,
        CancellationToken ct)
    {
        var actorId = User.GetRequiredUserId();

        try
        {
            var result = await settlementService.DisputeAsync(settlementId, actorId, disputeReason, ct);

            await TripHub.BroadcastSettlementOutcomeAsync(
                hubContext,
                result,
                $"{result.Settlement.ToUserName} báo chưa nhận được khoản {result.Settlement.Amount:N0}đ từ {result.Settlement.FromUserName}.",
                notificationService,
                ct);

            TempData["SuccessMessage"] =
                $"Đã gửi phản hồi chưa nhận được tiền tới {result.Settlement.FromUserName} để kiểm tra lại.";
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or UnauthorizedAccessException or KeyNotFoundException)
        {
            TempData["ErrorMessage"] = ex.Message;
        }

        return RedirectToPage("/Trips/Settlement", new { tripId });
    }

    public async Task<IActionResult> OnPostNudgeAsync(
        Guid tripId,
        Guid settlementId,
        CancellationToken ct)
    {
        var actorId = User.GetRequiredUserId();

        try
        {
            var nudge = await settlementService.NudgeDebtorAsync(settlementId, actorId, ct);

            await TripHub.DispatchUserNotificationAsync(
                hubContext.Clients,
                nudge.TargetUserId,
                nudge.Notification,
                notificationService,
                ct);

            await hubContext.Clients.Group(TripHub.TripGroupName(tripId))
                .SendAsync(
                    "SettlementUpdated",
                    new
                    {
                        tripId,
                        settlementId,
                        status = "Nudged",
                        message = "Đã gửi thông báo nhắc thanh toán VietQR."
                    },
                    ct);

            TempData["SuccessMessage"] = "Đã gửi lời nhắc thanh toán kèm mã VietQR tới thành viên.";
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or UnauthorizedAccessException or KeyNotFoundException)
        {
            TempData["ErrorMessage"] = ex.Message;
        }

        return RedirectToPage("/Trips/Settlement", new { tripId });
    }

    public async Task<IActionResult> OnPostTransitionStatusAsync(
        Guid tripId,
        TripStatus targetStatus,
        CancellationToken ct)
    {
        var actorId = User.GetRequiredUserId();
        var actorName = User.FindFirstValue(ClaimTypes.Name) ?? "Trưởng đoàn";

        try
        {
            var newStatus = await tripService.TransitionStatusAsync(tripId, actorId, targetStatus, ct);
            if (newStatus == TripStatus.Settling)
            {
                await settlementService.SyncSettlementPlanAsync(tripId, ct);
            }

            var (statusLabel, _) = DetailsModel.DescribeStatus(newStatus);

            await hubContext.Clients.Group(TripHub.TripGroupName(tripId))
                .SendAsync(
                    "TripStatusChanged",
                    new
                    {
                        tripId,
                        status = newStatus.ToString(),
                        statusLabel,
                        message = $"{actorName} đã chuyển trạng thái chuyến đi sang \"{statusLabel}\"."
                    },
                    ct);

            TempData["SuccessMessage"] = $"Đã chuyển trạng thái chuyến đi sang \"{statusLabel}\".";
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or UnauthorizedAccessException or KeyNotFoundException)
        {
            TempData["ErrorMessage"] = ex.Message;
        }

        return RedirectToPage("/Trips/Settlement", new { tripId });
    }

    public static (string Label, string BadgeCss) DescribeSettlementStatus(SettlementStatus status) => status switch
    {
        SettlementStatus.Pending => ("Chờ chuyển khoản", "text-bg-secondary"),
        SettlementStatus.MarkedPaid => ("Đã báo chuyển · Chờ xác nhận", "text-bg-warning"),
        SettlementStatus.Confirmed => ("Đã xác nhận nhận tiền", "text-bg-success"),
        SettlementStatus.Disputed => ("Báo chưa nhận · Cần kiểm tra", "text-bg-danger"),
        _ => (status.ToString(), "text-bg-secondary")
    };

    public static string ExtractInitials(string? fullName)
    {
        if (string.IsNullOrWhiteSpace(fullName))
        {
            return "TV";
        }

        var initials = string.Join(
            "",
            fullName.Split(' ', StringSplitOptions.RemoveEmptyEntries)
                .TakeLast(2)
                .Select(s => char.ToUpperInvariant(s[0])));

        return string.IsNullOrWhiteSpace(initials) ? "TV" : initials;
    }

    private async Task LoadWorkspaceAsync(Guid tripId, bool syncPendingPlan, CancellationToken ct)
    {
        CurrentUserId = User.GetRequiredUserId();
        IsAdmin = User.IsSystemAdmin();
        Workspace = await settlementService.GetWorkspaceAsync(tripId, CurrentUserId, IsAdmin, syncPendingPlan, ct);
        IsOwner = Workspace.Trip.OwnerUserId == CurrentUserId;
    }
}
