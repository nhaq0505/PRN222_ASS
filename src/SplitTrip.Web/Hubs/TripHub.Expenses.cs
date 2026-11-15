using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;
using SplitTrip.Services.Expenses;
using SplitTrip.Services.Expenses.Dtos;
using SplitTrip.Services.Notifications;

namespace SplitTrip.Web.Hubs;

public partial class TripHub
{
    public async Task<ClaimingBoardDto> ToggleReceiptItemClaim(
        string tripId,
        string expenseId,
        string receiptItemId,
        [FromServices] IExpenseService expenseService)
    {
        if (!Guid.TryParse(tripId, out var parsedTripId)
            || !Guid.TryParse(expenseId, out var parsedExpenseId)
            || !Guid.TryParse(receiptItemId, out var parsedItemId))
        {
            throw new HubException("Tham số nhận món không hợp lệ.");
        }

        var userId = GetAuthenticatedUserId();

        try
        {
            var board = await expenseService.ToggleItemClaimAsync(
                parsedExpenseId,
                parsedItemId,
                userId,
                Context.ConnectionAborted);

            if (board.TripId != parsedTripId)
            {
                throw new HubException("Hóa đơn không thuộc chuyến đi hiện tại.");
            }

            var groupName = TripGroupName(board.TripId);

            await Clients.Group(groupName)
                .SendAsync("ReceiptClaimUpdated", board, Context.ConnectionAborted);

            await Clients.Group(groupName)
                .SendAsync(
                    "ExpenseUpdated",
                    new
                    {
                        tripId = board.TripId,
                        expenseId = board.ExpenseId,
                        title = board.Title,
                        totalAmount = board.TotalAmount,
                        scanStatus = board.ScanStatus.ToString(),
                        unclaimedItemCount = board.UnclaimedItemCount,
                        message = board.LastActionMessage
                    },
                    Context.ConnectionAborted);

            return board;
        }
        catch (Exception ex) when (ex is InvalidOperationException or UnauthorizedAccessException or KeyNotFoundException or ArgumentException)
        {
            throw new HubException(ex.Message);
        }
    }

    public async Task<ClaimingBoardDto> FinalizeReceiptClaims(
        string tripId,
        string expenseId,
        [FromServices] IExpenseService expenseService,
        [FromServices] INotificationService notificationService)
    {
        if (!Guid.TryParse(tripId, out var parsedTripId)
            || !Guid.TryParse(expenseId, out var parsedExpenseId))
        {
            throw new HubException("Tham số khóa hóa đơn không hợp lệ.");
        }

        var userId = GetAuthenticatedUserId();

        try
        {
            var board = await expenseService.FinalizeReceiptClaimsAsync(
                parsedExpenseId,
                userId,
                Context.ConnectionAborted);

            if (board.TripId != parsedTripId)
            {
                throw new HubException("Hóa đơn không thuộc chuyến đi hiện tại.");
            }

            var groupName = TripGroupName(board.TripId);

            await Clients.Group(groupName)
                .SendAsync("ReceiptClaimLocked", board, Context.ConnectionAborted);

            await Clients.Group(groupName)
                .SendAsync(
                    "ExpenseUpdated",
                    new
                    {
                        tripId = board.TripId,
                        expenseId = board.ExpenseId,
                        title = board.Title,
                        totalAmount = board.TotalAmount,
                        scanStatus = board.ScanStatus.ToString(),
                        unclaimedItemCount = 0,
                        message = board.LastActionMessage
                    },
                    Context.ConnectionAborted);

            foreach (var member in board.MemberPreviews.Where(m => m.UserId != userId && m.LedgerOwedAmount > 0m))
            {
                var recent = await notificationService.GetRecentAsync(member.UserId, 1, Context.ConnectionAborted);
                var latest = recent.FirstOrDefault();
                if (latest is not null)
                {
                    await Clients.Group(UserGroupName(member.UserId))
                        .SendAsync("ReceiveNotification", latest, Context.ConnectionAborted);
                }
            }

            return board;
        }
        catch (Exception ex) when (ex is InvalidOperationException or UnauthorizedAccessException or KeyNotFoundException)
        {
            throw new HubException(ex.Message);
        }
    }
}
