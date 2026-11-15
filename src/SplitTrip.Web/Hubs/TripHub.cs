using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;
using SplitTrip.Services.Auth;
using SplitTrip.Services.Notifications;
using SplitTrip.Services.Trips;

namespace SplitTrip.Web.Hubs;

[Authorize]
public partial class TripHub : Hub
{
    public static string TripGroupName(Guid tripId) => $"trip:{tripId:D}";
    public static string UserGroupName(Guid userId) => $"user:{userId:D}";

    protected Guid GetAuthenticatedUserId()
    {
        var claim = Context.User?.FindFirstValue(ClaimTypes.NameIdentifier);
        if (Guid.TryParse(claim, out var userId))
        {
            return userId;
        }

        throw new HubException("Phiên kết nối không được xác thực hợp lệ.");
    }

    public override async Task OnConnectedAsync()
    {
        var claim = Context.User?.FindFirstValue(ClaimTypes.NameIdentifier);
        if (Guid.TryParse(claim, out var userId))
        {
            await Groups.AddToGroupAsync(Context.ConnectionId, UserGroupName(userId), Context.ConnectionAborted);
        }

        await base.OnConnectedAsync();
    }

    public async Task JoinTripRoom(
        string tripId,
        [FromServices] ITripService tripService)
    {
        if (!Guid.TryParse(tripId, out var parsedTripId))
        {
            throw new HubException("Mã chuyến đi không hợp lệ.");
        }

        var userId = GetAuthenticatedUserId();
        var isAdmin = Context.User?.IsInRole(AuthService.AdminRole) == true;
        if (!isAdmin && !await tripService.IsActiveMemberAsync(parsedTripId, userId, Context.ConnectionAborted))
        {
            throw new HubException("Bạn không có quyền truy cập phòng trực tuyến của chuyến đi này.");
        }

        await Groups.AddToGroupAsync(Context.ConnectionId, TripGroupName(parsedTripId), Context.ConnectionAborted);
    }

    public async Task LeaveTripRoom(string tripId)
    {
        if (Guid.TryParse(tripId, out var parsedTripId))
        {
            await Groups.RemoveFromGroupAsync(Context.ConnectionId, TripGroupName(parsedTripId), Context.ConnectionAborted);
        }
    }

    public async Task MarkNotificationRead(
        Guid notificationId,
        [FromServices] INotificationService notificationService)
    {
        var userId = GetAuthenticatedUserId();
        if (userId == AuthService.SystemAdminUserId)
        {
            return;
        }

        await notificationService.MarkAsReadAsync(notificationId, userId, Context.ConnectionAborted);
        var unreadCount = await notificationService.GetUnreadCountAsync(userId, Context.ConnectionAborted);
        await Clients.Group(UserGroupName(userId))
            .SendAsync("NotificationBadgeUpdated", unreadCount, Context.ConnectionAborted);
    }

    public async Task MarkAllNotificationsRead(
        [FromServices] INotificationService notificationService)
    {
        var userId = GetAuthenticatedUserId();
        if (userId == AuthService.SystemAdminUserId)
        {
            return;
        }

        await notificationService.MarkAllAsReadAsync(userId, Context.ConnectionAborted);
        await Clients.Group(UserGroupName(userId))
            .SendAsync("NotificationBadgeUpdated", 0, Context.ConnectionAborted);
    }
}
