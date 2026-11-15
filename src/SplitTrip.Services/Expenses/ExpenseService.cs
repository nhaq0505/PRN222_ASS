using SplitTrip.BusinessObjects.Entities;
using SplitTrip.BusinessObjects.Enums;
using SplitTrip.Repositories.Expenses;
using SplitTrip.Repositories.Notifications;
using SplitTrip.Repositories.Trips;
using SplitTrip.Services.Expenses.Dtos;

namespace SplitTrip.Services.Expenses;

public sealed class ExpenseService(
    IExpenseRepository expenseRepository,
    ITripRepository tripRepository,
    ITripMemberRepository memberRepository,
    INotificationRepository notificationRepository) : IExpenseService
{
    public async Task<TripExpensesOverviewDto> GetTripExpensesOverviewAsync(
        Guid tripId,
        Guid viewerUserId,
        ExpenseCategory? categoryFilter = null,
        bool isAdmin = false,
        CancellationToken ct = default)
    {
        var trip = await tripRepository.GetByIdAsync(tripId, ct)
            ?? throw new KeyNotFoundException("Không tìm thấy chuyến đi.");

        if (!isAdmin)
        {
            await EnsureActiveMemberAsync(tripId, viewerUserId, ct);
        }

        var allExpenses = await expenseRepository.GetByTripAsync(tripId, category: null, includeDrafts: true, ct);

        var confirmedExpenses = allExpenses
            .Where(e => e.ScanStatus != ReceiptScanStatus.DraftReview)
            .ToList();

        var totalConfirmedSpend = confirmedExpenses.Sum(e => e.TotalAmount);
        var draftCount = allExpenses.Count(e => e.ScanStatus == ReceiptScanStatus.DraftReview);
        var openClaimingCount = allExpenses.Count(e => e.ScanStatus == ReceiptScanStatus.ClaimingOpen);

        var spendByCategory = Enum.GetValues<ExpenseCategory>()
            .ToDictionary(
                cat => cat,
                cat => confirmedExpenses.Where(e => e.Category == cat).Sum(e => e.TotalAmount));

        var filtered = categoryFilter.HasValue
            ? allExpenses.Where(e => e.Category == categoryFilter.Value).ToList()
            : allExpenses;

        return new TripExpensesOverviewDto(
            trip.Id,
            totalConfirmedSpend,
            confirmedExpenses.Count,
            draftCount,
            openClaimingCount,
            spendByCategory,
            filtered.Select(MapToSummaryDto).ToList());
    }

    public async Task<ExpenseSummaryDto> GetExpenseByIdAsync(
        Guid expenseId,
        Guid viewerUserId,
        bool isAdmin = false,
        CancellationToken ct = default)
    {
        var expense = await expenseRepository.GetByIdAsync(expenseId, ct)
            ?? throw new KeyNotFoundException("Không tìm thấy khoản chi tiêu.");

        if (!isAdmin)
        {
            await EnsureActiveMemberAsync(expense.TripId, viewerUserId, ct);
        }

        return MapToSummaryDto(expense);
    }

    public async Task<ExpenseSummaryDto> CreateExpenseAsync(
        Guid actorUserId,
        UpsertExpenseRequest request,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var trip = await EnsureTripEditableAsync(request.TripId, ct);
        var activeMembers = await memberRepository.GetActiveMembersByTripAsync(trip.Id, ct);
        var activeUserMap = activeMembers.ToDictionary(m => m.UserId, m => m);

        if (!activeUserMap.ContainsKey(actorUserId))
        {
            throw new UnauthorizedAccessException("Bạn không phải là thành viên đang hoạt động của chuyến đi này.");
        }

        var (title, notes, subtotal, taxOrTip, discount, total, spentAtUtc) = ValidateAndNormalizeScalarInputs(request);
        ValidateParticipatingMembers(request.PaidByUserId, request.Participants, activeUserMap);

        var computedSplits = ExpenseSplitCalculator.CalculateSplits(
            request.SplitMode,
            total,
            request.PaidByUserId,
            request.Participants);

        var nowUtc = DateTime.UtcNow;
        var expenseId = Guid.NewGuid();
        var expense = new Expense
        {
            Id = expenseId,
            TripId = trip.Id,
            PaidByUserId = request.PaidByUserId,
            Title = title,
            Category = request.Category,
            SubtotalAmount = subtotal,
            TaxOrTipAmount = taxOrTip,
            DiscountAmount = discount,
            TotalAmount = total,
            SplitMode = request.SplitMode,
            ScanStatus = ReceiptScanStatus.None,
            SpentAtUtc = spentAtUtc,
            Notes = notes,
            CreatedAtUtc = nowUtc
        };

        var splitEntities = computedSplits.Select(s => new ExpenseSplit
        {
            Id = Guid.NewGuid(),
            ExpenseId = expenseId,
            UserId = s.UserId,
            ShareRatioOrWeight = s.ShareRatioOrWeight,
            OwedAmount = s.OwedAmount
        }).ToList();

        await expenseRepository.AddExpenseWithSplitsAsync(expense, splitEntities, ct);

        var payerName = activeUserMap[request.PaidByUserId].User?.FullName ?? "Thành viên";
        var notifications = computedSplits
            .Where(s => s.UserId != actorUserId && s.OwedAmount > 0m)
            .Select(s => new Notification
            {
                Id = Guid.NewGuid(),
                UserId = s.UserId,
                TripId = trip.Id,
                Type = NotificationType.ExpenseAdded,
                Title = "Khoản chi tiêu mới trong chuyến đi",
                Message = $"{payerName} đã chi \"{title}\" ({total:N0}đ) — phần của bạn: {s.OwedAmount:N0}đ.",
                ActionUrl = $"/Trips/Expenses?tripId={trip.Id}",
                IsRead = false,
                CreatedAtUtc = nowUtc
            })
            .ToList();

        if (notifications.Count > 0)
        {
            await notificationRepository.AddRangeAsync(notifications, ct);
        }

        var persisted = await expenseRepository.GetByIdAsync(expenseId, ct)
            ?? throw new InvalidOperationException("Không thể tải lại khoản chi tiêu vừa tạo.");

        return MapToSummaryDto(persisted);
    }

    public async Task<ExpenseSummaryDto> UpdateExpenseAsync(
        Guid expenseId,
        Guid actorUserId,
        UpsertExpenseRequest request,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var existing = await expenseRepository.GetByIdAsync(expenseId, ct)
            ?? throw new KeyNotFoundException("Không tìm thấy khoản chi tiêu cần chỉnh sửa.");

        if (existing.TripId != request.TripId)
        {
            throw new InvalidOperationException("Không thể chuyển khoản chi tiêu sang chuyến đi khác.");
        }

        var trip = await EnsureTripEditableAsync(existing.TripId, ct);
        var activeMembers = await memberRepository.GetActiveMembersByTripAsync(trip.Id, ct);
        var activeUserMap = activeMembers.ToDictionary(m => m.UserId, m => m);

        if (!activeUserMap.ContainsKey(actorUserId))
        {
            throw new UnauthorizedAccessException("Bạn không phải là thành viên đang hoạt động của chuyến đi này.");
        }

        if (actorUserId != existing.PaidByUserId && actorUserId != trip.OwnerUserId)
        {
            throw new UnauthorizedAccessException("Chỉ người ứng trả khoản chi hoặc trưởng đoàn mới có quyền chỉnh sửa.");
        }

        if (existing.SplitMode == ExpenseSplitMode.Itemized || existing.ScanStatus != ReceiptScanStatus.None)
        {
            throw new InvalidOperationException("Hóa đơn tách món bằng AI được quản lý trực tiếp trên trang Duyệt & Nhận món.");
        }

        var (title, notes, subtotal, taxOrTip, discount, total, spentAtUtc) = ValidateAndNormalizeScalarInputs(request);
        ValidateParticipatingMembers(request.PaidByUserId, request.Participants, activeUserMap);

        var computedSplits = ExpenseSplitCalculator.CalculateSplits(
            request.SplitMode,
            total,
            request.PaidByUserId,
            request.Participants);

        var updatedExpense = new Expense
        {
            Id = existing.Id,
            TripId = existing.TripId,
            PaidByUserId = request.PaidByUserId,
            Title = title,
            Category = request.Category,
            SubtotalAmount = subtotal,
            TaxOrTipAmount = taxOrTip,
            DiscountAmount = discount,
            TotalAmount = total,
            SplitMode = request.SplitMode,
            ReceiptImageUrl = existing.ReceiptImageUrl,
            ScanStatus = ReceiptScanStatus.None,
            SpentAtUtc = spentAtUtc,
            Notes = notes,
            CreatedAtUtc = existing.CreatedAtUtc
        };

        var splitEntities = computedSplits.Select(s => new ExpenseSplit
        {
            Id = Guid.NewGuid(),
            ExpenseId = existing.Id,
            UserId = s.UserId,
            ShareRatioOrWeight = s.ShareRatioOrWeight,
            OwedAmount = s.OwedAmount
        }).ToList();

        await expenseRepository.UpdateExpenseWithSplitsAsync(updatedExpense, splitEntities, ct);

        var persisted = await expenseRepository.GetByIdAsync(existing.Id, ct)
            ?? throw new InvalidOperationException("Không thể tải lại khoản chi tiêu sau khi cập nhật.");

        return MapToSummaryDto(persisted);
    }

    public async Task<Guid> DeleteExpenseAsync(
        Guid expenseId,
        Guid actorUserId,
        CancellationToken ct = default)
    {
        var existing = await expenseRepository.GetByIdAsync(expenseId, ct)
            ?? throw new KeyNotFoundException("Không tìm thấy khoản chi tiêu cần xóa.");

        var trip = await EnsureTripEditableAsync(existing.TripId, ct);
        await EnsureActiveMemberAsync(trip.Id, actorUserId, ct);

        if (actorUserId != existing.PaidByUserId && actorUserId != trip.OwnerUserId)
        {
            throw new UnauthorizedAccessException("Chỉ người ứng trả khoản chi hoặc trưởng đoàn mới có quyền xóa.");
        }

        await expenseRepository.DeleteAsync(expenseId, ct);
        return existing.TripId;
    }

    private async Task<Trip> EnsureTripEditableAsync(Guid tripId, CancellationToken ct)
    {
        var trip = await tripRepository.GetByIdAsync(tripId, ct)
            ?? throw new KeyNotFoundException("Không tìm thấy chuyến đi.");

        if (trip.Status is TripStatus.Settling or TripStatus.Completed)
        {
            throw new InvalidOperationException(
                "Chuyến đi đang trong giai đoạn tất toán hoặc đã hoàn tất, không thể thêm, sửa hoặc xóa chi tiêu.");
        }

        return trip;
    }

    private async Task EnsureActiveMemberAsync(Guid tripId, Guid userId, CancellationToken ct)
    {
        var membership = await memberRepository.GetMembershipAsync(tripId, userId, ct);
        if (membership is null || !membership.IsActive)
        {
            throw new UnauthorizedAccessException("Bạn không phải là thành viên đang hoạt động của chuyến đi này.");
        }
    }

    private static (string Title, string? Notes, decimal Subtotal, decimal TaxOrTip, decimal Discount, decimal Total, DateTime SpentAtUtc)
        ValidateAndNormalizeScalarInputs(UpsertExpenseRequest request)
    {
        var title = (request.Title ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(title) || title.Length > 200)
        {
            throw new ArgumentException("Tên khoản chi tiêu phải từ 1 đến 200 ký tự.", nameof(request));
        }

        var notes = string.IsNullOrWhiteSpace(request.Notes) ? null : request.Notes.Trim();
        if (notes is not null && notes.Length > 2000)
        {
            throw new ArgumentException("Ghi chú khoản chi không được vượt quá 2000 ký tự.", nameof(request));
        }

        ExpenseSplitCalculator.ValidatePositiveWholeVnd(
            request.TotalAmount,
            nameof(request.TotalAmount),
            "Tổng số tiền chi tiêu phải là số nguyên dương (VNĐ).");

        ExpenseSplitCalculator.ValidateNonNegativeWholeVnd(
            request.TaxOrTipAmount,
            nameof(request.TaxOrTipAmount),
            "Thuế / phí phục vụ phải là số nguyên không âm (VNĐ).");

        ExpenseSplitCalculator.ValidateNonNegativeWholeVnd(
            request.DiscountAmount,
            nameof(request.DiscountAmount),
            "Giảm giá phải là số nguyên không âm (VNĐ).");

        var subtotal = request.SubtotalAmount ?? (request.TotalAmount - request.TaxOrTipAmount + request.DiscountAmount);
        ExpenseSplitCalculator.ValidatePositiveWholeVnd(
            subtotal,
            nameof(request.SubtotalAmount),
            "Tạm tính phải là số nguyên dương (VNĐ).");

        if (request.DiscountAmount > subtotal + request.TaxOrTipAmount)
        {
            throw new ArgumentException("Mức giảm giá không được vượt quá tổng tạm tính và thuế/phí.", nameof(request));
        }

        if (subtotal + request.TaxOrTipAmount - request.DiscountAmount != request.TotalAmount)
        {
            throw new ArgumentException(
                $"Tạm tính ({subtotal:N0}đ) + Thuế/Phí ({request.TaxOrTipAmount:N0}đ) - Giảm giá ({request.DiscountAmount:N0}đ) phải bằng Tổng cộng ({request.TotalAmount:N0}đ).",
                nameof(request));
        }

        var spentAtUtc = request.SpentAtUtc.HasValue
            ? (request.SpentAtUtc.Value.Kind == DateTimeKind.Utc
                ? request.SpentAtUtc.Value
                : request.SpentAtUtc.Value.ToUniversalTime())
            : DateTime.UtcNow;

        return (title, notes, subtotal, request.TaxOrTipAmount, request.DiscountAmount, request.TotalAmount, spentAtUtc);
    }

    private static void ValidateParticipatingMembers(
        Guid paidByUserId,
        IReadOnlyList<ParticipantShareInput> participants,
        IReadOnlyDictionary<Guid, TripMember> activeUserMap)
    {
        if (!activeUserMap.ContainsKey(paidByUserId))
        {
            throw new InvalidOperationException("Người ứng trả phải là thành viên đang hoạt động của chuyến đi.");
        }

        foreach (var participant in participants)
        {
            if (!activeUserMap.ContainsKey(participant.UserId))
            {
                throw new InvalidOperationException("Tất cả thành viên tham gia chia tiền phải đang hoạt động trong chuyến đi.");
            }
        }
    }

    internal static ExpenseSummaryDto MapToSummaryDto(Expense expense)
    {
        var splits = expense.Splits
            .OrderByDescending(s => s.OwedAmount)
            .ThenBy(s => s.User?.FullName ?? string.Empty)
            .Select(s => new ExpenseSplitDto(
                s.UserId,
                s.User?.FullName ?? "Thành viên",
                s.ShareRatioOrWeight,
                s.OwedAmount))
            .ToList();

        var itemCount = expense.ReceiptItems.Count;
        var unclaimedCount = expense.ReceiptItems.Count(i => i.Claims.Count == 0);

        return new ExpenseSummaryDto(
            expense.Id,
            expense.TripId,
            expense.PaidByUserId,
            expense.PaidByUser?.FullName ?? "Thành viên",
            expense.Title,
            expense.Category,
            expense.SubtotalAmount,
            expense.TaxOrTipAmount,
            expense.DiscountAmount,
            expense.TotalAmount,
            expense.SplitMode,
            expense.ScanStatus,
            expense.ReceiptImageUrl,
            expense.SpentAtUtc,
            expense.Notes,
            expense.CreatedAtUtc,
            itemCount,
            unclaimedCount,
            splits);
    }
}
