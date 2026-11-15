using SplitTrip.BusinessObjects.Entities;
using SplitTrip.BusinessObjects.Enums;
using SplitTrip.Repositories.Expenses;
using SplitTrip.Repositories.Notifications;
using SplitTrip.Repositories.Trips;
using SplitTrip.Services.Expenses.Dtos;

namespace SplitTrip.Services.Expenses;

public sealed class ExpenseService(
    IExpenseRepository expenseRepository,
    IReceiptRepository receiptRepository,
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

    public async Task<ReceiptDraftDto> GetReceiptDraftAsync(
        Guid expenseId,
        Guid viewerUserId,
        bool isAdmin = false,
        CancellationToken ct = default)
    {
        var expense = await receiptRepository.GetExpenseWithReceiptDetailsAsync(expenseId, ct)
            ?? throw new KeyNotFoundException("Không tìm thấy hóa đơn quét AI.");

        if (!isAdmin)
        {
            await EnsureActiveMemberAsync(expense.TripId, viewerUserId, ct);
        }

        var orderedItems = expense.ReceiptItems
            .OrderBy(i => i.SortOrder)
            .ThenBy(i => i.Id)
            .ToList();

        return GeminiReceiptScannerService.BuildReceiptDraftDto(
            expense,
            orderedItems,
            expense.PaidByUser?.FullName ?? "Thành viên");
    }

    public async Task<ReceiptDraftDto> UpdateReceiptDraftAsync(
        Guid actorUserId,
        UpdateReceiptDraftRequest request,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var existing = await receiptRepository.GetExpenseWithReceiptDetailsAsync(request.ExpenseId, ct)
            ?? throw new KeyNotFoundException("Không tìm thấy bản nháp hóa đơn cần cập nhật.");

        var trip = await EnsureTripEditableAsync(existing.TripId, ct);
        var activeMembers = await memberRepository.GetActiveMembersByTripAsync(trip.Id, ct);
        var activeMap = activeMembers.ToDictionary(m => m.UserId, m => m);

        if (!activeMap.ContainsKey(actorUserId))
        {
            throw new UnauthorizedAccessException("Bạn không phải là thành viên đang hoạt động của chuyến đi này.");
        }

        if (actorUserId != existing.PaidByUserId && actorUserId != trip.OwnerUserId)
        {
            throw new UnauthorizedAccessException("Chỉ người thanh toán hóa đơn hoặc trưởng đoàn mới có quyền chỉnh sửa bản nháp OCR.");
        }

        if (existing.ScanStatus != ReceiptScanStatus.DraftReview)
        {
            throw new InvalidOperationException("Chỉ có thể chỉnh sửa chi tiết dòng món khi hóa đơn đang ở trạng thái Duyệt nháp (DraftReview).");
        }

        if (!activeMap.ContainsKey(request.PaidByUserId))
        {
            throw new InvalidOperationException("Người thanh toán hóa đơn phải là thành viên đang hoạt động của chuyến đi.");
        }

        var title = (request.Title ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(title) || title.Length > 200)
        {
            throw new ArgumentException("Tên hóa đơn phải từ 1 đến 200 ký tự.", nameof(request));
        }

        var notes = string.IsNullOrWhiteSpace(request.Notes) ? null : request.Notes.Trim();
        if (notes is not null && notes.Length > 2000)
        {
            throw new ArgumentException("Ghi chú không được vượt quá 2000 ký tự.", nameof(request));
        }

        ExpenseSplitCalculator.ValidateNonNegativeWholeVnd(request.SubtotalAmount, nameof(request.SubtotalAmount), "Tạm tính phải là số nguyên không âm (VNĐ).");
        ExpenseSplitCalculator.ValidateNonNegativeWholeVnd(request.TaxOrTipAmount, nameof(request.TaxOrTipAmount), "Thuế/Phí phải là số nguyên không âm (VNĐ).");
        ExpenseSplitCalculator.ValidateNonNegativeWholeVnd(request.DiscountAmount, nameof(request.DiscountAmount), "Giảm giá phải là số nguyên không âm (VNĐ).");
        ExpenseSplitCalculator.ValidateNonNegativeWholeVnd(request.TotalAmount, nameof(request.TotalAmount), "Tổng thanh toán phải là số nguyên không âm (VNĐ).");

        if (request.Items is null || request.Items.Count == 0)
        {
            throw new ArgumentException("Hóa đơn phải có ít nhất một dòng món ăn/mặt hàng.", nameof(request));
        }

        var sanitizedItems = new List<ReceiptItem>(request.Items.Count);
        for (var idx = 0; idx < request.Items.Count; idx++)
        {
            var raw = request.Items[idx];
            var itemName = (raw.ItemName ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(itemName))
            {
                continue;
            }

            if (itemName.Length > 250)
            {
                itemName = itemName[..250];
            }

            if (raw.Quantity < 1)
            {
                throw new ArgumentException($"Số lượng của món \"{itemName}\" phải lớn hơn hoặc bằng 1.", nameof(request));
            }

            ExpenseSplitCalculator.ValidateNonNegativeWholeVnd(raw.UnitPrice, nameof(request), $"Đơn giá món \"{itemName}\" phải là số nguyên không âm (VNĐ).");
            ExpenseSplitCalculator.ValidateNonNegativeWholeVnd(raw.LineTotal, nameof(request), $"Thành tiền món \"{itemName}\" phải là số nguyên không âm (VNĐ).");

            sanitizedItems.Add(new ReceiptItem
            {
                Id = Guid.NewGuid(),
                ExpenseId = existing.Id,
                ItemName = itemName,
                Quantity = raw.Quantity,
                UnitPrice = raw.UnitPrice,
                LineTotal = raw.LineTotal,
                SortOrder = sanitizedItems.Count
            });
        }

        if (sanitizedItems.Count == 0)
        {
            throw new ArgumentException("Hóa đơn phải có ít nhất một dòng món hợp lệ.", nameof(request));
        }

        var spentAtUtc = request.SpentAtUtc.HasValue
            ? (request.SpentAtUtc.Value.Kind == DateTimeKind.Utc
                ? request.SpentAtUtc.Value
                : request.SpentAtUtc.Value.ToUniversalTime())
            : existing.SpentAtUtc;

        await receiptRepository.UpdateReceiptDraftAsync(
            existing.Id,
            title,
            request.Category,
            request.PaidByUserId,
            spentAtUtc,
            notes,
            request.SubtotalAmount,
            request.TaxOrTipAmount,
            request.DiscountAmount,
            request.TotalAmount,
            sanitizedItems,
            ct);

        return await GetReceiptDraftAsync(existing.Id, actorUserId, isAdmin: false, ct);
    }

    public async Task<ClaimingBoardDto> ConfirmDraftAndOpenClaimingAsync(
        Guid expenseId,
        Guid actorUserId,
        UpdateReceiptDraftRequest? finalEdits = null,
        CancellationToken ct = default)
    {
        if (finalEdits is not null)
        {
            await UpdateReceiptDraftAsync(actorUserId, finalEdits, ct);
        }

        var expense = await receiptRepository.GetExpenseWithReceiptDetailsAsync(expenseId, ct)
            ?? throw new KeyNotFoundException("Không tìm thấy bản nháp hóa đơn.");

        var trip = await EnsureTripEditableAsync(expense.TripId, ct);
        var activeMembers = await memberRepository.GetActiveMembersByTripAsync(trip.Id, ct);
        var activeMap = activeMembers.ToDictionary(m => m.UserId, m => m);

        if (!activeMap.ContainsKey(actorUserId))
        {
            throw new UnauthorizedAccessException("Bạn không phải là thành viên đang hoạt động của chuyến đi này.");
        }

        if (actorUserId != expense.PaidByUserId && actorUserId != trip.OwnerUserId)
        {
            throw new UnauthorizedAccessException("Chỉ người thanh toán hóa đơn hoặc trưởng đoàn mới có quyền xác nhận mở chọn món.");
        }

        if (expense.ScanStatus != ReceiptScanStatus.DraftReview)
        {
            throw new InvalidOperationException("Hóa đơn này đã được mở chọn món hoặc đã khóa chốt sổ.");
        }

        var orderedItems = expense.ReceiptItems
            .OrderBy(i => i.SortOrder)
            .ThenBy(i => i.Id)
            .ToList();

        var draftEvaluation = GeminiReceiptScannerService.BuildReceiptDraftDto(
            expense,
            orderedItems,
            expense.PaidByUser?.FullName ?? "Thành viên");

        if (draftEvaluation.HasMismatch)
        {
            throw new InvalidOperationException(
                $"Chưa thể mở chọn món vì số liệu hóa đơn chưa khớp: {draftEvaluation.MismatchWarningMessage}");
        }

        var calcItems = orderedItems
            .Select(i => (
                ItemId: i.Id,
                i.LineTotal,
                Claims: (IReadOnlyList<(Guid UserId, DateTime ClaimedAtUtc)>)[ ]))
            .ToList();

        var allocation = ExpenseSplitCalculator.CalculateItemizedProRataAllocation(
            expense.SubtotalAmount,
            expense.TotalAmount,
            expense.PaidByUserId,
            calcItems,
            allowUnclaimedFallbackToPayer: true);

        var splitEntities = allocation.ReconciledSplits.Select(s => new ExpenseSplit
        {
            Id = Guid.NewGuid(),
            ExpenseId = expense.Id,
            UserId = s.UserId,
            ShareRatioOrWeight = s.ShareRatioOrWeight,
            OwedAmount = s.OwedAmount
        }).ToList();

        await expenseRepository.ReplaceSplitsAsync(expense.Id, splitEntities, ReceiptScanStatus.ClaimingOpen, ct);

        var payerName = activeMap.GetValueOrDefault(expense.PaidByUserId)?.User?.FullName ?? "Thành viên";
        var nowUtc = DateTime.UtcNow;
        var notifications = activeMembers
            .Where(m => m.UserId != actorUserId)
            .Select(m => new Notification
            {
                Id = Guid.NewGuid(),
                UserId = m.UserId,
                TripId = trip.Id,
                Type = NotificationType.ExpenseAdded,
                Title = "Hóa đơn AI mới mở chọn món trực tiếp",
                Message = $"{payerName} vừa mở nhận món cho hóa đơn \"{expense.Title}\" ({expense.TotalAmount:N0}đ). Hãy vào chọn món bạn đã dùng!",
                ActionUrl = $"/Trips/ReceiptClaim?tripId={trip.Id}&expenseId={expense.Id}",
                IsRead = false,
                CreatedAtUtc = nowUtc
            })
            .ToList();

        if (notifications.Count > 0)
        {
            await notificationRepository.AddRangeAsync(notifications, ct);
        }

        return await BuildClaimingBoardInternalAsync(
            expense.Id,
            actorUserId,
            isAdmin: false,
            lastToggledItemId: null,
            lastActorUserId: actorUserId,
            lastActionMessage: $"{payerName} đã xác nhận hóa đơn và mở bảng chọn món trực tiếp.",
            ct);
    }

    public Task<ClaimingBoardDto> GetClaimingBoardAsync(
        Guid expenseId,
        Guid viewerUserId,
        bool isAdmin = false,
        CancellationToken ct = default)
        => BuildClaimingBoardInternalAsync(expenseId, viewerUserId, isAdmin, null, null, null, ct);

    public async Task<ClaimingBoardDto> ToggleItemClaimAsync(
        Guid expenseId,
        Guid receiptItemId,
        Guid userId,
        CancellationToken ct = default)
    {
        var expense = await receiptRepository.GetExpenseWithReceiptDetailsAsync(expenseId, ct)
            ?? throw new KeyNotFoundException("Không tìm thấy hóa đơn.");

        var trip = await EnsureTripEditableAsync(expense.TripId, ct);
        var activeMembers = await memberRepository.GetActiveMembersByTripAsync(trip.Id, ct);
        var activeMap = activeMembers.ToDictionary(m => m.UserId, m => m);

        if (!activeMap.TryGetValue(userId, out var actorMember))
        {
            throw new UnauthorizedAccessException("Bạn không phải là thành viên đang hoạt động của chuyến đi này.");
        }

        if (expense.ScanStatus == ReceiptScanStatus.DraftReview)
        {
            throw new InvalidOperationException("Hóa đơn vẫn đang ở bước duyệt nháp OCR, chưa mở nhận món.");
        }

        if (expense.ScanStatus == ReceiptScanStatus.Locked)
        {
            throw new InvalidOperationException("Hóa đơn đã khóa chốt sổ, không thể thay đổi món đã chọn.");
        }

        if (expense.ScanStatus != ReceiptScanStatus.ClaimingOpen)
        {
            throw new InvalidOperationException("Khoản chi tiêu này không ở chế độ nhận món trực tiếp.");
        }

        var targetItem = expense.ReceiptItems.FirstOrDefault(i => i.Id == receiptItemId)
            ?? throw new KeyNotFoundException("Món ăn không tồn tại trong hóa đơn này.");

        var isNowClaimed = await receiptRepository.ToggleClaimAsync(receiptItemId, userId, ct);

        var refreshedItems = await receiptRepository.GetItemsWithClaimsAsync(expenseId, ct);
        var calcItems = refreshedItems
            .Select(i => (
                ItemId: i.Id,
                i.LineTotal,
                Claims: (IReadOnlyList<(Guid UserId, DateTime ClaimedAtUtc)>)i.Claims
                    .Select(c => (c.UserId, c.ClaimedAtUtc))
                    .ToList()))
            .ToList();

        var allocation = ExpenseSplitCalculator.CalculateItemizedProRataAllocation(
            expense.SubtotalAmount,
            expense.TotalAmount,
            expense.PaidByUserId,
            calcItems,
            allowUnclaimedFallbackToPayer: true);

        var splitEntities = allocation.ReconciledSplits.Select(s => new ExpenseSplit
        {
            Id = Guid.NewGuid(),
            ExpenseId = expense.Id,
            UserId = s.UserId,
            ShareRatioOrWeight = s.ShareRatioOrWeight,
            OwedAmount = s.OwedAmount
        }).ToList();

        await expenseRepository.ReplaceSplitsAsync(expense.Id, splitEntities, updatedScanStatus: null, ct);

        var actorName = actorMember.User?.FullName ?? "Thành viên";
        var actionVerb = isNowClaimed ? "đã nhận" : "đã bỏ chọn";
        var actionMessage = $"{actorName} {actionVerb} món \"{targetItem.ItemName}\".";

        return await BuildClaimingBoardInternalAsync(
            expenseId,
            userId,
            isAdmin: false,
            lastToggledItemId: receiptItemId,
            lastActorUserId: userId,
            lastActionMessage: actionMessage,
            ct);
    }

    public async Task<ClaimingBoardDto> FinalizeReceiptClaimsAsync(
        Guid expenseId,
        Guid actorUserId,
        CancellationToken ct = default)
    {
        var expense = await receiptRepository.GetExpenseWithReceiptDetailsAsync(expenseId, ct)
            ?? throw new KeyNotFoundException("Không tìm thấy hóa đơn cần khóa.");

        var trip = await EnsureTripEditableAsync(expense.TripId, ct);
        var activeMembers = await memberRepository.GetActiveMembersByTripAsync(trip.Id, ct);
        var activeMap = activeMembers.ToDictionary(m => m.UserId, m => m);

        if (!activeMap.TryGetValue(actorUserId, out var actorMember))
        {
            throw new UnauthorizedAccessException("Bạn không phải là thành viên đang hoạt động của chuyến đi này.");
        }

        if (actorUserId != expense.PaidByUserId && actorUserId != trip.OwnerUserId)
        {
            throw new UnauthorizedAccessException("Chỉ người thanh toán hóa đơn hoặc trưởng đoàn mới có quyền khóa và chốt chia tiền.");
        }

        if (expense.ScanStatus != ReceiptScanStatus.ClaimingOpen)
        {
            throw new InvalidOperationException("Chỉ có thể khóa hóa đơn đang ở trạng thái Mở nhận món (ClaimingOpen).");
        }

        var orderedItems = expense.ReceiptItems
            .OrderBy(i => i.SortOrder)
            .ThenBy(i => i.Id)
            .ToList();

        var unclaimedCount = orderedItems.Count(i => i.Claims.Count == 0);
        if (unclaimedCount > 0)
        {
            throw new InvalidOperationException(
                $"Còn {unclaimedCount} món chưa có thành viên nào nhận. Vui lòng phân bổ hết các món trước khi khóa hóa đơn.");
        }

        var calcItems = orderedItems
            .Select(i => (
                ItemId: i.Id,
                i.LineTotal,
                Claims: (IReadOnlyList<(Guid UserId, DateTime ClaimedAtUtc)>)i.Claims
                    .Select(c => (c.UserId, c.ClaimedAtUtc))
                    .ToList()))
            .ToList();

        var allocation = ExpenseSplitCalculator.CalculateItemizedProRataAllocation(
            expense.SubtotalAmount,
            expense.TotalAmount,
            expense.PaidByUserId,
            calcItems,
            allowUnclaimedFallbackToPayer: false);

        var splitEntities = allocation.ReconciledSplits.Select(s => new ExpenseSplit
        {
            Id = Guid.NewGuid(),
            ExpenseId = expense.Id,
            UserId = s.UserId,
            ShareRatioOrWeight = s.ShareRatioOrWeight,
            OwedAmount = s.OwedAmount
        }).ToList();

        await expenseRepository.ReplaceSplitsAsync(expense.Id, splitEntities, ReceiptScanStatus.Locked, ct);

        var actorName = actorMember.User?.FullName ?? "Trưởng đoàn";
        var nowUtc = DateTime.UtcNow;
        var notifications = allocation.ReconciledSplits
            .Where(s => s.UserId != actorUserId && s.OwedAmount > 0m)
            .Select(s => new Notification
            {
                Id = Guid.NewGuid(),
                UserId = s.UserId,
                TripId = trip.Id,
                Type = NotificationType.ExpenseAdded,
                Title = "Hóa đơn tách món đã chốt sổ",
                Message = $"{actorName} đã khóa hóa đơn \"{expense.Title}\" ({expense.TotalAmount:N0}đ) — phần của bạn (gồm VAT/giảm giá): {s.OwedAmount:N0}đ.",
                ActionUrl = $"/Trips/ReceiptClaim?tripId={trip.Id}&expenseId={expense.Id}",
                IsRead = false,
                CreatedAtUtc = nowUtc
            })
            .ToList();

        if (notifications.Count > 0)
        {
            await notificationRepository.AddRangeAsync(notifications, ct);
        }

        return await BuildClaimingBoardInternalAsync(
            expense.Id,
            actorUserId,
            isAdmin: false,
            lastToggledItemId: null,
            lastActorUserId: actorUserId,
            lastActionMessage: $"{actorName} đã khóa nhận món và chốt sổ cái hóa đơn.",
            ct);
    }

    private async Task<ClaimingBoardDto> BuildClaimingBoardInternalAsync(
        Guid expenseId,
        Guid viewerUserId,
        bool isAdmin,
        Guid? lastToggledItemId,
        Guid? lastActorUserId,
        string? lastActionMessage,
        CancellationToken ct)
    {
        var expense = await receiptRepository.GetExpenseWithReceiptDetailsAsync(expenseId, ct)
            ?? throw new KeyNotFoundException("Không tìm thấy hóa đơn.");

        var trip = expense.Trip ?? await tripRepository.GetByIdAsync(expense.TripId, ct)
            ?? throw new KeyNotFoundException("Không tìm thấy chuyến đi.");

        if (!isAdmin)
        {
            await EnsureActiveMemberAsync(trip.Id, viewerUserId, ct);
        }

        if (expense.ScanStatus == ReceiptScanStatus.DraftReview)
        {
            throw new InvalidOperationException("Hóa đơn vẫn đang ở trạng thái Duyệt nháp OCR, chưa mở bảng chọn món.");
        }

        var allMembers = await memberRepository.GetAllMembersByTripAsync(trip.Id, ct);
        var userNames = allMembers.ToDictionary(
            m => m.UserId,
            m => m.User?.FullName ?? "Thành viên");

        var orderedItems = expense.ReceiptItems
            .OrderBy(i => i.SortOrder)
            .ThenBy(i => i.Id)
            .ToList();

        var calcItems = orderedItems
            .Select(i => (
                ItemId: i.Id,
                i.LineTotal,
                Claims: (IReadOnlyList<(Guid UserId, DateTime ClaimedAtUtc)>)i.Claims
                    .Select(c => (c.UserId, c.ClaimedAtUtc))
                    .ToList()))
            .ToList();

        var allocation = ExpenseSplitCalculator.CalculateItemizedProRataAllocation(
            expense.SubtotalAmount,
            expense.TotalAmount,
            expense.PaidByUserId,
            calcItems,
            allowUnclaimedFallbackToPayer: true);

        var itemDtos = orderedItems.Select(item =>
        {
            var shares = allocation.PerItemShares.GetValueOrDefault(item.Id) ?? [];
            var shareMap = shares.ToDictionary(x => x.UserId, x => x.DishShareVnd);

            var claimers = item.Claims
                .OrderBy(c => c.ClaimedAtUtc)
                .ThenBy(c => c.UserId)
                .Select(c =>
                {
                    var name = c.User?.FullName ?? userNames.GetValueOrDefault(c.UserId, "Thành viên");
                    return new ItemClaimerDto(
                        c.UserId,
                        name,
                        BuildInitials(name),
                        shareMap.GetValueOrDefault(c.UserId, 0m),
                        c.ClaimedAtUtc);
                })
                .ToList();

            return new ClaimableReceiptItemDto(
                item.Id,
                item.ItemName,
                item.Quantity,
                item.UnitPrice,
                item.LineTotal,
                item.SortOrder,
                IsUnclaimed: claimers.Count == 0,
                IsClaimedByViewer: claimers.Any(c => c.UserId == viewerUserId),
                claimers);
        }).ToList();

        var claimedItemCountByUser = orderedItems
            .SelectMany(i => i.Claims.Select(c => c.UserId))
            .GroupBy(uid => uid)
            .ToDictionary(g => g.Key, g => g.Count());

        var ledgerMap = allocation.ReconciledSplits.ToDictionary(s => s.UserId, s => s.OwedAmount);

        var displayMembers = allMembers
            .Where(m => m.IsActive || ledgerMap.ContainsKey(m.UserId) || m.UserId == expense.PaidByUserId)
            .ToList();

        var memberPreviews = displayMembers
            .Select(m =>
            {
                var name = m.User?.FullName ?? "Thành viên";
                var claimedSub = allocation.ClaimedSubtotalByUser.GetValueOrDefault(m.UserId, 0m);
                var fallbackSub = allocation.UnclaimedFallbackByUser.GetValueOrDefault(m.UserId, 0m);
                var effectiveSub = claimedSub + fallbackSub;
                var ledgerOwed = ledgerMap.GetValueOrDefault(m.UserId, 0m);
                var proRataAdj = ledgerOwed - effectiveSub;

                return new MemberClaimPreviewDto(
                    m.UserId,
                    name,
                    BuildInitials(name),
                    IsPayer: m.UserId == expense.PaidByUserId,
                    ClaimedItemCount: claimedItemCountByUser.GetValueOrDefault(m.UserId, 0),
                    ClaimedDishSubtotal: claimedSub,
                    UnclaimedFallbackSubtotal: fallbackSub,
                    EffectiveDishSubtotal: effectiveSub,
                    ProRataAdjustmentVnd: proRataAdj,
                    LedgerOwedAmount: ledgerOwed);
            })
            .OrderByDescending(m => m.LedgerOwedAmount)
            .ThenByDescending(m => m.IsPayer)
            .ThenBy(m => m.FullName)
            .ToList();

        var unclaimedItems = itemDtos.Where(i => i.IsUnclaimed).ToList();
        var isTripEditable = trip.Status is TripStatus.Planning or TripStatus.Ongoing;
        var canManageLock = isTripEditable
                            && (viewerUserId == expense.PaidByUserId || viewerUserId == trip.OwnerUserId);

        return new ClaimingBoardDto(
            expense.Id,
            trip.Id,
            expense.Title,
            expense.Category,
            expense.PaidByUserId,
            expense.PaidByUser?.FullName ?? userNames.GetValueOrDefault(expense.PaidByUserId, "Thành viên"),
            expense.ReceiptImageUrl,
            expense.ScanStatus,
            expense.SpentAtUtc,
            expense.Notes,
            expense.SubtotalAmount,
            expense.TaxOrTipAmount,
            expense.DiscountAmount,
            expense.TotalAmount,
            itemDtos.Count,
            unclaimedItems.Count,
            unclaimedItems.Sum(i => i.LineTotal),
            AllItemsClaimed: unclaimedItems.Count == 0,
            canManageLock,
            isTripEditable,
            lastToggledItemId,
            lastActorUserId,
            lastActionMessage,
            itemDtos,
            memberPreviews);
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

    private static string BuildInitials(string fullName)
    {
        if (string.IsNullOrWhiteSpace(fullName))
        {
            return "TV";
        }

        var chars = fullName
            .Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .TakeLast(2)
            .Select(part => char.ToUpperInvariant(part[0]))
            .ToArray();

        return chars.Length > 0 ? new string(chars) : "TV";
    }
}
