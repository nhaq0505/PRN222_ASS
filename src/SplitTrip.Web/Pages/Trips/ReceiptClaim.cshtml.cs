using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.SignalR;
using SplitTrip.BusinessObjects.Enums;
using SplitTrip.Services.Expenses;
using SplitTrip.Services.Expenses.Dtos;
using SplitTrip.Services.Notifications;
using SplitTrip.Services.Trips;
using SplitTrip.Services.Trips.Dtos;
using SplitTrip.Web.Hubs;
using SplitTrip.Web.Infrastructure;

namespace SplitTrip.Web.Pages.Trips;

[Authorize]
public sealed class ReceiptClaimModel(
    ITripService tripService,
    IExpenseService expenseService,
    INotificationService notificationService,
    IHubContext<TripHub> hubContext) : PageModel
{
    public TripDetailsDto Trip { get; private set; } = default!;
    public ExpenseSummaryDto ExpenseSummary { get; private set; } = default!;
    public ReceiptDraftDto? Draft { get; private set; }
    public ClaimingBoardDto? Board { get; private set; }
    public Guid CurrentUserId { get; private set; }
    public bool IsOwner { get; private set; }
    public bool IsAdmin { get; private set; }
    public bool IsTripEditable => Trip.Status is TripStatus.Planning or TripStatus.Ongoing;
    public IReadOnlyList<MemberNetBalanceDto> ActiveMembers { get; private set; } = [];

    [BindProperty]
    public DraftReviewFormInput Input { get; set; } = new();

    public async Task<IActionResult> OnGetAsync(
        Guid? tripId,
        Guid? expenseId,
        CancellationToken ct)
    {
        if (expenseId is null || expenseId == Guid.Empty)
        {
            return tripId.HasValue && tripId != Guid.Empty
                ? RedirectToPage("/Trips/Expenses", new { tripId })
                : RedirectToPage("/Index");
        }

        try
        {
            await LoadPageAsync(tripId, expenseId.Value, ct);
            return Page();
        }
        catch (Exception ex) when (ex is KeyNotFoundException or UnauthorizedAccessException)
        {
            TempData["ErrorMessage"] = ex.Message;
            return tripId.HasValue && tripId != Guid.Empty
                ? RedirectToPage("/Trips/Expenses", new { tripId })
                : RedirectToPage("/Index");
        }
    }

    public async Task<IActionResult> OnPostSaveDraftAsync(CancellationToken ct)
    {
        var actorId = User.GetRequiredUserId();

        try
        {
            var request = MapDraftInput(Input);
            var updated = await expenseService.UpdateReceiptDraftAsync(actorId, request, ct);

            TempData["SuccessMessage"] = updated.HasMismatch
                ? "Đã lưu thay đổi bản nháp OCR. Lưu ý: tổng tiền các món vẫn còn chênh lệch, vui lòng đối chiếu trước khi mở chọn món."
                : "Đã lưu bản nháp OCR — tổng tiền các món, thuế/phí và giảm giá đã khớp 100%!";
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or UnauthorizedAccessException or KeyNotFoundException)
        {
            TempData["ErrorMessage"] = ex.Message;
        }

        return RedirectToPage("/Trips/ReceiptClaim", new { tripId = Input.TripId, expenseId = Input.ExpenseId });
    }

    public async Task<IActionResult> OnPostPublishClaimingAsync(CancellationToken ct)
    {
        var actorId = User.GetRequiredUserId();

        try
        {
            var finalEdits = MapDraftInput(Input);
            var board = await expenseService.ConfirmDraftAndOpenClaimingAsync(
                Input.ExpenseId,
                actorId,
                finalEdits,
                ct);

            var groupName = TripHub.TripGroupName(board.TripId);

            await hubContext.Clients.Group(groupName)
                .SendAsync(
                    "ReceiptDraftPublished",
                    new
                    {
                        tripId = board.TripId,
                        expenseId = board.ExpenseId,
                        title = board.Title,
                        totalAmount = board.TotalAmount,
                        message = board.LastActionMessage
                    },
                    ct);

            await hubContext.Clients.Group(groupName)
                .SendAsync(
                    "ExpenseAdded",
                    new
                    {
                        tripId = board.TripId,
                        expenseId = board.ExpenseId,
                        title = board.Title,
                        totalAmount = board.TotalAmount,
                        message = board.LastActionMessage
                    },
                    ct);

            foreach (var member in board.MemberPreviews.Where(m => m.UserId != actorId))
            {
                var recent = await notificationService.GetRecentAsync(member.UserId, 1, ct);
                var latest = recent.FirstOrDefault();
                if (latest is not null)
                {
                    await hubContext.Clients.Group(TripHub.UserGroupName(member.UserId))
                        .SendAsync("ReceiveNotification", latest, ct);
                }
            }

            TempData["SuccessMessage"] = "Đã mở chọn món trực tiếp! Mời các thành viên bấm chọn món mình đã dùng hoặc dùng chung.";
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or UnauthorizedAccessException or KeyNotFoundException)
        {
            TempData["ErrorMessage"] = ex.Message;
        }

        return RedirectToPage("/Trips/ReceiptClaim", new { tripId = Input.TripId, expenseId = Input.ExpenseId });
    }

    public async Task<IActionResult> OnPostToggleClaimAsync(
        Guid tripId,
        Guid expenseId,
        Guid receiptItemId,
        CancellationToken ct)
    {
        var actorId = User.GetRequiredUserId();

        try
        {
            var board = await expenseService.ToggleItemClaimAsync(expenseId, receiptItemId, actorId, ct);
            var groupName = TripHub.TripGroupName(board.TripId);

            await hubContext.Clients.Group(groupName)
                .SendAsync("ReceiptClaimUpdated", board, ct);

            await hubContext.Clients.Group(groupName)
                .SendAsync(
                    "ExpenseUpdated",
                    new
                    {
                        tripId = board.TripId,
                        expenseId = board.ExpenseId,
                        title = board.Title,
                        totalAmount = board.TotalAmount,
                        scanStatus = board.ScanStatus.ToString(),
                        message = board.LastActionMessage
                    },
                    ct);

            if (Request.Headers.Accept.ToString().Contains("application/json", StringComparison.OrdinalIgnoreCase))
            {
                return new JsonResult(board);
            }
        }
        catch (Exception ex) when (ex is InvalidOperationException or UnauthorizedAccessException or KeyNotFoundException)
        {
            if (Request.Headers.Accept.ToString().Contains("application/json", StringComparison.OrdinalIgnoreCase))
            {
                return new JsonResult(new { error = ex.Message }) { StatusCode = StatusCodes.Status400BadRequest };
            }

            TempData["ErrorMessage"] = ex.Message;
        }

        return RedirectToPage("/Trips/ReceiptClaim", new { tripId, expenseId });
    }

    public async Task<IActionResult> OnPostLockClaimsAsync(
        Guid tripId,
        Guid expenseId,
        CancellationToken ct)
    {
        var actorId = User.GetRequiredUserId();

        try
        {
            var board = await expenseService.FinalizeReceiptClaimsAsync(expenseId, actorId, ct);
            var groupName = TripHub.TripGroupName(board.TripId);

            await hubContext.Clients.Group(groupName)
                .SendAsync("ReceiptClaimLocked", board, ct);

            await hubContext.Clients.Group(groupName)
                .SendAsync(
                    "ExpenseUpdated",
                    new
                    {
                        tripId = board.TripId,
                        expenseId = board.ExpenseId,
                        title = board.Title,
                        totalAmount = board.TotalAmount,
                        scanStatus = board.ScanStatus.ToString(),
                        message = board.LastActionMessage
                    },
                    ct);

            foreach (var member in board.MemberPreviews.Where(m => m.UserId != actorId && m.LedgerOwedAmount > 0m))
            {
                var recent = await notificationService.GetRecentAsync(member.UserId, 1, ct);
                var latest = recent.FirstOrDefault();
                if (latest is not null)
                {
                    await hubContext.Clients.Group(TripHub.UserGroupName(member.UserId))
                        .SendAsync("ReceiveNotification", latest, ct);
                }
            }

            TempData["SuccessMessage"] = "Đã khóa nhận món và chốt sổ cái thành công — tổng phần chia khớp chính xác 100% hóa đơn!";
        }
        catch (Exception ex) when (ex is InvalidOperationException or UnauthorizedAccessException or KeyNotFoundException)
        {
            TempData["ErrorMessage"] = ex.Message;
        }

        return RedirectToPage("/Trips/ReceiptClaim", new { tripId, expenseId });
    }

    private async Task LoadPageAsync(Guid? requestedTripId, Guid expenseId, CancellationToken ct)
    {
        CurrentUserId = User.GetRequiredUserId();
        IsAdmin = User.IsSystemAdmin();

        ExpenseSummary = await expenseService.GetExpenseByIdAsync(expenseId, CurrentUserId, IsAdmin, ct);
        var resolvedTripId = requestedTripId.HasValue && requestedTripId.Value != Guid.Empty
            ? requestedTripId.Value
            : ExpenseSummary.TripId;

        Trip = await tripService.GetTripDetailsAsync(resolvedTripId, CurrentUserId, IsAdmin, ct);
        IsOwner = Trip.OwnerUserId == CurrentUserId;
        ActiveMembers = Trip.MemberBalances.Where(m => m.IsActive).ToList();

        if (ExpenseSummary.ScanStatus == ReceiptScanStatus.DraftReview)
        {
            Draft = await expenseService.GetReceiptDraftAsync(expenseId, CurrentUserId, IsAdmin, ct);
        }
        else
        {
            Board = await expenseService.GetClaimingBoardAsync(expenseId, CurrentUserId, IsAdmin, ct);
        }
    }

    private static UpdateReceiptDraftRequest MapDraftInput(DraftReviewFormInput input)
    {
        var items = (input.Items ?? [])
            .Select(i => new DraftReceiptItemInput(
                i.ItemName ?? string.Empty,
                i.Quantity,
                i.UnitPrice,
                i.LineTotal))
            .ToList();

        return new UpdateReceiptDraftRequest(
            input.ExpenseId,
            input.Title ?? string.Empty,
            input.Category,
            input.PaidByUserId,
            input.SubtotalAmount,
            input.TaxOrTipAmount,
            input.DiscountAmount,
            input.TotalAmount,
            items,
            SpentAtUtc: null,
            Notes: input.Notes);
    }

    public sealed class DraftReviewFormInput
    {
        public Guid TripId { get; set; }
        public Guid ExpenseId { get; set; }
        public string? Title { get; set; }
        public ExpenseCategory Category { get; set; } = ExpenseCategory.Food;
        public Guid PaidByUserId { get; set; }
        public decimal SubtotalAmount { get; set; }
        public decimal TaxOrTipAmount { get; set; }
        public decimal DiscountAmount { get; set; }
        public decimal TotalAmount { get; set; }
        public string? Notes { get; set; }
        public List<DraftItemRowInput> Items { get; set; } = [];
    }

    public sealed class DraftItemRowInput
    {
        public string? ItemName { get; set; }
        public int Quantity { get; set; } = 1;
        public decimal UnitPrice { get; set; }
        public decimal LineTotal { get; set; }
    }
}
