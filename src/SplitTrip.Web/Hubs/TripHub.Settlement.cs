using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;
using SplitTrip.BusinessObjects.Enums;
using SplitTrip.Services.Auth;
using SplitTrip.Services.Notifications;
using SplitTrip.Services.Notifications.Dtos;
using SplitTrip.Services.Settlements;
using SplitTrip.Services.Settlements.Dtos;
using SplitTrip.Services.Trips;

namespace SplitTrip.Web.Hubs;

public partial class TripHub
{
    public async Task<SettlementCardDto> MarkSettlementPaid(
        Guid settlementId,
        string? proofNote,
        [FromServices] ISettlementService settlementService,
        [FromServices] INotificationService notificationService)
    {
        var actorId = GetAuthenticatedUserId();
        try
        {
            var result = await settlementService.MarkAsPaidAsync(
                settlementId,
                actorId,
                proofNote,
                Context.ConnectionAborted);

            await BroadcastActionOutcomeAsync(
                result,
                $"{result.Settlement.FromUserName} đã đánh dấu chuyển khoản {result.Settlement.Amount:N0}đ cho {result.Settlement.ToUserName}.",
                notificationService,
                Context.ConnectionAborted);

            return result.Settlement;
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or UnauthorizedAccessException or KeyNotFoundException)
        {
            throw new HubException(ex.Message);
        }
    }

    public async Task<SettlementCardDto> ConfirmSettlementReceived(
        Guid settlementId,
        [FromServices] ISettlementService settlementService,
        [FromServices] INotificationService notificationService)
    {
        var actorId = GetAuthenticatedUserId();
        try
        {
            var result = await settlementService.ConfirmReceivedAsync(
                settlementId,
                actorId,
                Context.ConnectionAborted);

            await BroadcastActionOutcomeAsync(
                result,
                $"{result.Settlement.ToUserName} đã xác nhận nhận đủ {result.Settlement.Amount:N0}đ từ {result.Settlement.FromUserName}.",
                notificationService,
                Context.ConnectionAborted);

            return result.Settlement;
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or UnauthorizedAccessException or KeyNotFoundException)
        {
            throw new HubException(ex.Message);
        }
    }

    public async Task<SettlementCardDto> DisputeSettlement(
        Guid settlementId,
        string disputeReason,
        [FromServices] ISettlementService settlementService,
        [FromServices] INotificationService notificationService)
    {
        var actorId = GetAuthenticatedUserId();
        try
        {
            var result = await settlementService.DisputeAsync(
                settlementId,
                actorId,
                disputeReason,
                Context.ConnectionAborted);

            await BroadcastActionOutcomeAsync(
                result,
                $"{result.Settlement.ToUserName} báo chưa nhận được khoản {result.Settlement.Amount:N0}đ từ {result.Settlement.FromUserName}.",
                notificationService,
                Context.ConnectionAborted);

            return result.Settlement;
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or UnauthorizedAccessException or KeyNotFoundException)
        {
            throw new HubException(ex.Message);
        }
    }

    public async Task NudgeSettlementDebtor(
        Guid settlementId,
        [FromServices] ISettlementService settlementService,
        [FromServices] INotificationService notificationService)
    {
        var actorId = GetAuthenticatedUserId();
        try
        {
            var nudge = await settlementService.NudgeDebtorAsync(
                settlementId,
                actorId,
                Context.ConnectionAborted);

            await DispatchUserNotificationAsync(
                Clients,
                nudge.TargetUserId,
                nudge.Notification,
                notificationService,
                Context.ConnectionAborted);

            await Clients.Group(TripGroupName(nudge.TripId))
                .SendAsync(
                    "SettlementUpdated",
                    new
                    {
                        tripId = nudge.TripId,
                        settlementId = nudge.SettlementId,
                        status = "Nudged",
                        message = "Đã gửi lời nhắc tất toán VietQR."
                    },
                    Context.ConnectionAborted);
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or UnauthorizedAccessException or KeyNotFoundException)
        {
            throw new HubException(ex.Message);
        }
    }

    public async Task<IReadOnlyList<SettlementCardDto>> SyncSettlementPlan(
        Guid tripId,
        [FromServices] ISettlementService settlementService,
        [FromServices] ITripService tripService)
    {
        var actorId = GetAuthenticatedUserId();
        var isAdmin = Context.User?.IsInRole(AuthService.AdminRole) == true;

        if (!isAdmin && !await tripService.IsActiveMemberAsync(tripId, actorId, Context.ConnectionAborted))
        {
            throw new HubException("Bạn không có quyền đồng bộ kế hoạch tất toán của chuyến đi này.");
        }

        try
        {
            var cards = await settlementService.SyncSettlementPlanAsync(tripId, Context.ConnectionAborted);
            await Clients.Group(TripGroupName(tripId))
                .SendAsync(
                    "SettlementUpdated",
                    new
                    {
                        tripId,
                        status = "Synced",
                        message = "Kế hoạch tất toán tối ưu Min-Cash-Flow vừa được làm mới."
                    },
                    Context.ConnectionAborted);

            return cards;
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or UnauthorizedAccessException or KeyNotFoundException)
        {
            throw new HubException(ex.Message);
        }
    }

    public static async Task BroadcastSettlementOutcomeAsync(
        IHubContext<TripHub> hubContext,
        SettlementActionResultDto result,
        string message,
        INotificationService notificationService,
        CancellationToken ct = default)
    {
        await DispatchUserNotificationAsync(
            hubContext.Clients,
            result.NotificationTargetUserId,
            result.Notification,
            notificationService,
            ct);

        await hubContext.Clients.Group(TripGroupName(result.Settlement.TripId))
            .SendAsync(
                "SettlementUpdated",
                new
                {
                    tripId = result.Settlement.TripId,
                    settlementId = result.Settlement.Id,
                    status = result.Settlement.Status.ToString(),
                    tripStatus = result.NewTripStatus.ToString(),
                    tripCompleted = result.TripCompleted,
                    message
                },
                ct);

        if (result.TripCompleted)
        {
            await hubContext.Clients.Group(TripGroupName(result.Settlement.TripId))
                .SendAsync(
                    "TripStatusChanged",
                    new
                    {
                        tripId = result.Settlement.TripId,
                        status = TripStatus.Completed.ToString(),
                        statusLabel = "Đã hoàn tất",
                        message = "Tất cả thành viên đã tất toán về 0đ — chuyến đi tự động chuyển sang Hoàn tất!"
                    },
                    ct);

            await hubContext.Clients.Group(TripGroupName(result.Settlement.TripId))
                .SendAsync(
                    "TripCompleted",
                    new
                    {
                        tripId = result.Settlement.TripId,
                        message = "Tất cả công nợ đã tất toán 0đ!"
                    },
                    ct);
        }
    }

    public static async Task DispatchUserNotificationAsync(
        IHubClients<IClientProxy> clients,
        Guid targetUserId,
        NotificationDto notification,
        INotificationService notificationService,
        CancellationToken ct = default)
    {
        if (targetUserId == AuthService.SystemAdminUserId)
        {
            return;
        }

        var unreadCount = await notificationService.GetUnreadCountAsync(targetUserId, ct);
        var userGroup = UserGroupName(targetUserId);

        await clients.Group(userGroup).SendAsync(
            "ReceiveNotification",
            new
            {
                id = notification.Id,
                title = notification.Title,
                message = notification.Message,
                actionUrl = notification.ActionUrl,
                createdAtUtc = notification.CreatedAtUtc
            },
            ct);

        await clients.Group(userGroup).SendAsync("NotificationBadgeUpdated", unreadCount, ct);
    }

    private async Task BroadcastActionOutcomeAsync(
        SettlementActionResultDto result,
        string message,
        INotificationService notificationService,
        CancellationToken ct)
    {
        await DispatchUserNotificationAsync(
            Clients,
            result.NotificationTargetUserId,
            result.Notification,
            notificationService,
            ct);

        await Clients.Group(TripGroupName(result.Settlement.TripId))
            .SendAsync(
                "SettlementUpdated",
                new
                {
                    tripId = result.Settlement.TripId,
                    settlementId = result.Settlement.Id,
                    status = result.Settlement.Status.ToString(),
                    tripStatus = result.NewTripStatus.ToString(),
                    tripCompleted = result.TripCompleted,
                    message
                },
                ct);

        if (result.TripCompleted)
        {
            await Clients.Group(TripGroupName(result.Settlement.TripId))
                .SendAsync(
                    "TripStatusChanged",
                    new
                    {
                        tripId = result.Settlement.TripId,
                        status = TripStatus.Completed.ToString(),
                        statusLabel = "Đã hoàn tất",
                        message = "Tất cả thành viên đã tất toán về 0đ — chuyến đi tự động chuyển sang Hoàn tất!"
                    },
                    ct);

            await Clients.Group(TripGroupName(result.Settlement.TripId))
                .SendAsync(
                    "TripCompleted",
                    new
                    {
                        tripId = result.Settlement.TripId,
                        message = "Tất cả công nợ đã tất toán 0đ!"
                    },
                    ct);
        }
    }
}
