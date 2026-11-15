using System.Security.Claims;
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
public sealed class ExpensesModel(
    ITripService tripService,
    IExpenseService expenseService,
    IAiReceiptScannerService receiptScannerService,
    INotificationService notificationService,
    IHubContext<TripHub> hubContext,
    IWebHostEnvironment environment) : PageModel
{
    public TripDetailsDto Trip { get; private set; } = default!;
    public TripExpensesOverviewDto Overview { get; private set; } = default!;
    public ExpenseCategory? SelectedCategory { get; private set; }
    public Guid CurrentUserId { get; private set; }
    public bool IsOwner { get; private set; }
    public bool IsAdmin { get; private set; }
    public bool IsTripEditable => Trip.Status is TripStatus.Planning or TripStatus.Ongoing;
    public IReadOnlyList<MemberNetBalanceDto> ActiveMembers { get; private set; } = [];

    public async Task<IActionResult> OnGetAsync(
        Guid? tripId,
        Guid? id,
        ExpenseCategory? category,
        CancellationToken ct)
    {
        var resolvedId = tripId ?? id;
        if (resolvedId is null || resolvedId == Guid.Empty)
        {
            return RedirectToPage("/Index");
        }

        try
        {
            await LoadWorkspaceAsync(resolvedId.Value, category, ct);
            return Page();
        }
        catch (Exception ex) when (ex is KeyNotFoundException or UnauthorizedAccessException)
        {
            TempData["ErrorMessage"] = ex.Message;
            return RedirectToPage("/Index");
        }
    }

    public async Task<IActionResult> OnGetLiveFeedAsync(
        Guid tripId,
        ExpenseCategory? category,
        CancellationToken ct)
    {
        try
        {
            await LoadWorkspaceAsync(tripId, category, ct);
            var activeCount = Math.Max(1, ActiveMembers.Count);
            var averagePerMember = decimal.Floor(Overview.TotalConfirmedSpend / activeCount);

            return new JsonResult(new
            {
                tripId = Trip.Id,
                totalConfirmedSpend = Overview.TotalConfirmedSpend,
                confirmedExpenseCount = Overview.ConfirmedExpenseCount,
                draftReceiptCount = Overview.DraftReceiptCount,
                openClaimingCount = Overview.OpenClaimingCount,
                averagePerActiveMember = averagePerMember
            });
        }
        catch (Exception ex) when (ex is KeyNotFoundException or UnauthorizedAccessException)
        {
            return new JsonResult(new { error = ex.Message }) { StatusCode = StatusCodes.Status403Forbidden };
        }
    }

    public async Task<IActionResult> OnPostSaveExpenseAsync(
        SaveExpenseFormInput input,
        CancellationToken ct)
    {
        if (input.TripId == Guid.Empty)
        {
            return RedirectToPage("/Index");
        }

        var actorId = User.GetRequiredUserId();
        var actorName = User.FindFirstValue(ClaimTypes.Name) ?? "Thành viên";

        try
        {
            var participants = (input.Participants ?? [])
                .Where(p => p.Selected && p.UserId != Guid.Empty)
                .Select(p =>
                {
                    var normalized = (p.Value ?? "1").Trim().Replace(',', '.');
                    if (!decimal.TryParse(
                            normalized,
                            System.Globalization.NumberStyles.Number,
                            System.Globalization.CultureInfo.InvariantCulture,
                            out var parsedVal))
                    {
                        throw new ArgumentException("Giá trị phần chia của thành viên không hợp lệ.");
                    }

                    return new ParticipantShareInput(p.UserId, parsedVal);
                })
                .ToList();

            var request = new UpsertExpenseRequest(
                input.TripId,
                input.PaidByUserId,
                input.Title ?? string.Empty,
                input.Category,
                input.TotalAmount,
                input.SplitMode,
                participants,
                SpentAtUtc: null,
                Notes: input.Notes);

            if (input.ExpenseId.HasValue && input.ExpenseId.Value != Guid.Empty)
            {
                var updated = await expenseService.UpdateExpenseAsync(input.ExpenseId.Value, actorId, request, ct);

                await hubContext.Clients.Group(TripHub.TripGroupName(input.TripId))
                    .SendAsync(
                        "ExpenseUpdated",
                        new
                        {
                            tripId = input.TripId,
                            expenseId = updated.Id,
                            title = updated.Title,
                            totalAmount = updated.TotalAmount,
                            message = $"{actorName} đã cập nhật khoản chi \"{updated.Title}\" ({updated.TotalAmount:N0}đ)."
                        },
                        ct);

                TempData["SuccessMessage"] = $"Đã cập nhật khoản chi \"{updated.Title}\" ({updated.TotalAmount:N0}đ).";
            }
            else
            {
                var created = await expenseService.CreateExpenseAsync(actorId, request, ct);

                await hubContext.Clients.Group(TripHub.TripGroupName(input.TripId))
                    .SendAsync(
                        "ExpenseAdded",
                        new
                        {
                            tripId = input.TripId,
                            expenseId = created.Id,
                            title = created.Title,
                            totalAmount = created.TotalAmount,
                            message = $"{actorName} đã thêm khoản chi \"{created.Title}\" ({created.TotalAmount:N0}đ)."
                        },
                        ct);

                foreach (var split in created.Splits.Where(s => s.UserId != actorId && s.OwedAmount > 0m))
                {
                    var recent = await notificationService.GetRecentAsync(split.UserId, 1, ct);
                    var latest = recent.FirstOrDefault();
                    if (latest is not null)
                    {
                        await hubContext.Clients.Group(TripHub.UserGroupName(split.UserId))
                            .SendAsync("ReceiveNotification", latest, ct);
                    }
                }

                TempData["SuccessMessage"] = $"Đã thêm khoản chi \"{created.Title}\" ({created.TotalAmount:N0}đ) — sổ cái đã cân bằng chính xác!";
            }
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or UnauthorizedAccessException or KeyNotFoundException)
        {
            TempData["ErrorMessage"] = ex.Message;
        }

        return RedirectToPage("/Trips/Expenses", new { tripId = input.TripId, category = input.ReturnCategory });
    }

    public async Task<IActionResult> OnPostDeleteExpenseAsync(
        Guid tripId,
        Guid expenseId,
        ExpenseCategory? returnCategory,
        CancellationToken ct)
    {
        var actorId = User.GetRequiredUserId();
        var actorName = User.FindFirstValue(ClaimTypes.Name) ?? "Thành viên";

        try
        {
            await expenseService.DeleteExpenseAsync(expenseId, actorId, ct);

            await hubContext.Clients.Group(TripHub.TripGroupName(tripId))
                .SendAsync(
                    "ExpenseDeleted",
                    new
                    {
                        tripId,
                        expenseId,
                        message = $"{actorName} vừa xóa một khoản chi tiêu khỏi sổ cái."
                    },
                    ct);

            TempData["SuccessMessage"] = "Đã xóa khoản chi tiêu và cập nhật lại số dư ròng.";
        }
        catch (Exception ex) when (ex is InvalidOperationException or UnauthorizedAccessException or KeyNotFoundException)
        {
            TempData["ErrorMessage"] = ex.Message;
        }

        return RedirectToPage("/Trips/Expenses", new { tripId, category = returnCategory });
    }

    public async Task<IActionResult> OnPostScanReceiptAsync(
        Guid tripId,
        Guid? paidByUserId,
        IFormFile? receiptImage,
        string? notes,
        bool useDemoSample,
        CancellationToken ct)
    {
        if (tripId == Guid.Empty)
        {
            return RedirectToPage("/Index");
        }

        var actorId = User.GetRequiredUserId();

        try
        {
            byte[]? imageBytes = null;
            string? savedRelativeUrl = null;
            string? fileName = null;
            string? contentType = null;

            if (receiptImage is { Length: > 0 })
            {
                if (receiptImage.Length > GeminiReceiptScannerService.MaxReceiptImageBytes)
                {
                    throw new ArgumentException("Kích thước ảnh hóa đơn không được vượt quá 10 MB.");
                }

                await using var ms = new MemoryStream((int)receiptImage.Length);
                await receiptImage.CopyToAsync(ms, ct);
                imageBytes = ms.ToArray();
                fileName = receiptImage.FileName;
                contentType = receiptImage.ContentType;

                receiptScannerService.ValidateReceiptImage(fileName, contentType, imageBytes);

                var ext = Path.GetExtension(fileName).ToLowerInvariant();
                var safeFileName = $"{Guid.NewGuid():N}{ext}";
                var webRoot = string.IsNullOrWhiteSpace(environment.WebRootPath)
                    ? Path.Combine(environment.ContentRootPath, "wwwroot")
                    : environment.WebRootPath;

                var uploadDir = Path.Combine(webRoot, "uploads", "receipts", tripId.ToString("D"));
                Directory.CreateDirectory(uploadDir);

                var fullPath = Path.Combine(uploadDir, safeFileName);
                await System.IO.File.WriteAllBytesAsync(fullPath, imageBytes, ct);
                savedRelativeUrl = $"/uploads/receipts/{tripId:D}/{safeFileName}";
            }
            else if (!useDemoSample)
            {
                throw new ArgumentException("Vui lòng tải lên ảnh hóa đơn (JPG, PNG, WEBP ≤ 10 MB) hoặc bật chế độ Hóa đơn mẫu Demo AI.");
            }

            var draft = await receiptScannerService.ScanAndCreateDraftAsync(
                new ScanReceiptRequest(
                    TripId: tripId,
                    ActorUserId: actorId,
                    PaidByUserId: paidByUserId ?? actorId,
                    ImageBytes: imageBytes,
                    FileName: fileName,
                    ContentType: contentType,
                    ReceiptImageUrl: savedRelativeUrl,
                    Notes: notes,
                    UseDemoSample: useDemoSample),
                ct);

            TempData["SuccessMessage"] = draft.IsDemoFallback
                ? "Đã phân tích hóa đơn (Chế độ Demo AI thông minh)! Hãy kiểm tra các dòng món bên dưới trước khi mở chọn món Live."
                : "Gemini Vision đã trích xuất hóa đơn thành công! Hãy rà soát danh sách món trước khi mở chọn món.";

            return RedirectToPage("/Trips/ReceiptClaim", new { tripId, expenseId = draft.ExpenseId });
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or UnauthorizedAccessException or KeyNotFoundException)
        {
            TempData["ErrorMessage"] = ex.Message;
            return RedirectToPage("/Trips/Expenses", new { tripId });
        }
    }

    private async Task LoadWorkspaceAsync(Guid tripId, ExpenseCategory? category, CancellationToken ct)
    {
        CurrentUserId = User.GetRequiredUserId();
        IsAdmin = User.IsSystemAdmin();
        SelectedCategory = category;

        Trip = await tripService.GetTripDetailsAsync(tripId, CurrentUserId, IsAdmin, ct);
        Overview = await expenseService.GetTripExpensesOverviewAsync(tripId, CurrentUserId, category, IsAdmin, ct);
        IsOwner = Trip.OwnerUserId == CurrentUserId;
        ActiveMembers = Trip.MemberBalances.Where(m => m.IsActive).ToList();
    }

    public static (string Label, string Icon) DescribeCategory(ExpenseCategory category) => category switch
    {
        ExpenseCategory.Food => ("Ăn uống", "🍜"),
        ExpenseCategory.Transport => ("Di chuyển", "🚕"),
        ExpenseCategory.Accommodation => ("Lưu trú", "🏨"),
        ExpenseCategory.Ticket => ("Vé tham quan", "🎫"),
        ExpenseCategory.Shopping => ("Mua sắm", "🛍️"),
        ExpenseCategory.Entertainment => ("Giải trí", "🎉"),
        _ => ("Khác", "📦")
    };

    public static string DescribeSplitMode(ExpenseSplitMode mode) => mode switch
    {
        ExpenseSplitMode.Equal => "Chia đều",
        ExpenseSplitMode.ExactAmount => "Số tiền cụ thể",
        ExpenseSplitMode.Percentage => "Theo tỷ lệ %",
        ExpenseSplitMode.Shares => "Theo phần (Shares)",
        ExpenseSplitMode.Itemized => "Tách món AI",
        _ => mode.ToString()
    };

    public sealed class SaveExpenseFormInput
    {
        public Guid TripId { get; set; }
        public Guid? ExpenseId { get; set; }
        public Guid PaidByUserId { get; set; }
        public string? Title { get; set; }
        public ExpenseCategory Category { get; set; } = ExpenseCategory.Food;
        public decimal TotalAmount { get; set; }
        public ExpenseSplitMode SplitMode { get; set; } = ExpenseSplitMode.Equal;
        public string? Notes { get; set; }
        public ExpenseCategory? ReturnCategory { get; set; }
        public List<ParticipantRowInput> Participants { get; set; } = [];
    }

    public sealed class ParticipantRowInput
    {
        public Guid UserId { get; set; }
        public bool Selected { get; set; }
        public string? Value { get; set; } = "1";
    }
}
