using SplitTrip.BusinessObjects.Entities;
using SplitTrip.BusinessObjects.Enums;
using SplitTrip.Repositories.Itinerary;
using SplitTrip.Repositories.Trips;
using SplitTrip.Services.Itinerary.Dtos;
using SplitTrip.Services.Trips;

namespace SplitTrip.Services.Itinerary;

public sealed class ItineraryService(
    ITripRepository tripRepository,
    ITripDayRepository tripDayRepository,
    IItineraryItemRepository itineraryItemRepository,
    IItineraryVoteRepository itineraryVoteRepository,
    ITripService tripService) : IItineraryService
{
    private static readonly TimeSpan VietnamUtcOffset = TimeSpan.FromHours(7);

    public static DateTime ToVietnamLocalTime(DateTime utcDateTime)
    {
        var normalizedUtc = utcDateTime.Kind == DateTimeKind.Utc
            ? utcDateTime
            : DateTime.SpecifyKind(utcDateTime, DateTimeKind.Utc);

        return normalizedUtc.Add(VietnamUtcOffset);
    }

    public async Task<TripScheduleDto> GetScheduleAsync(
        Guid tripId,
        Guid viewerUserId,
        bool isAdmin = false,
        CancellationToken ct = default)
    {
        var trip = await tripRepository.GetByIdWithDetailsAsync(tripId, ct)
            ?? throw new KeyNotFoundException("Không tìm thấy chuyến đi.");

        if (!isAdmin && !await tripService.IsActiveMemberAsync(tripId, viewerUserId, ct))
        {
            throw new UnauthorizedAccessException("Bạn không phải là thành viên đang hoạt động của chuyến đi này.");
        }

        var days = await EnsureDaysSeededAndLoadedAsync(trip, ct);
        return MapSchedule(trip, days, viewerUserId, isAdmin);
    }

    public async Task<TripDayDto> UpdateDayAsync(
        Guid tripId,
        Guid tripDayId,
        Guid actorUserId,
        UpdateTripDayRequest request,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var trip = await ValidateMutableTripAndMembershipAsync(tripId, actorUserId, ct);
        var day = await tripDayRepository.GetByIdAsync(tripDayId, ct)
            ?? throw new KeyNotFoundException("Không tìm thấy ngày lịch trình.");

        if (day.TripId != trip.Id)
        {
            throw new InvalidOperationException("Ngày lịch trình không thuộc chuyến đi này.");
        }

        var trimmedTitle = string.IsNullOrWhiteSpace(request.Title) ? $"Ngày {day.DayNumber}" : request.Title.Trim();
        if (trimmedTitle.Length > 200)
        {
            throw new ArgumentException("Tiêu đề ngày không được vượt quá 200 ký tự.", nameof(request));
        }

        var trimmedNotes = string.IsNullOrWhiteSpace(request.Notes) ? null : request.Notes.Trim();
        if (trimmedNotes is { Length: > 2000 })
        {
            throw new ArgumentException("Ghi chú ngày không được vượt quá 2000 ký tự.", nameof(request));
        }

        day.Title = trimmedTitle;
        day.Notes = trimmedNotes;
        await tripDayRepository.UpdateAsync(day, ct);

        var reloadedDays = await tripDayRepository.GetByTripIdWithItemsAsync(tripId, ct);
        var updated = reloadedDays.First(d => d.Id == day.Id);
        return MapDay(updated, actorUserId);
    }

    public async Task<ItineraryItemDto> AddItemAsync(
        Guid tripId,
        Guid actorUserId,
        UpsertItineraryItemRequest request,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var trip = await ValidateMutableTripAndMembershipAsync(tripId, actorUserId, ct);
        var normalized = ValidateAndNormalizeItemRequest(request);

        var day = await tripDayRepository.GetByIdAsync(request.TripDayId, ct)
            ?? throw new KeyNotFoundException("Không tìm thấy ngày lịch trình đã chọn.");

        if (day.TripId != trip.Id)
        {
            throw new InvalidOperationException("Ngày lịch trình không thuộc chuyến đi này.");
        }

        var maxSortOrder = await itineraryItemRepository.GetMaxSortOrderAsync(day.Id, ct);
        var entity = new ItineraryItem
        {
            Id = Guid.NewGuid(),
            TripDayId = day.Id,
            Title = normalized.Title,
            LocationName = normalized.LocationName,
            Address = normalized.Address,
            Latitude = request.Latitude,
            Longitude = request.Longitude,
            StartTime = request.StartTime,
            EndTime = request.EndTime,
            EstimatedCost = normalized.EstimatedCost,
            SortOrder = maxSortOrder + 1,
            Notes = normalized.Notes,
            IsAiSuggested = false,
            ReminderSentAtUtc = null,
            CreatedByUserId = actorUserId,
            CreatedAtUtc = DateTime.UtcNow
        };

        await itineraryItemRepository.AddAsync(entity, ct);

        var created = await itineraryItemRepository.GetByIdAsync(entity.Id, ct) ?? entity;
        created.TripDay ??= day;
        return MapItem(created, day, actorUserId);
    }

    public async Task<ItineraryItemDto> UpdateItemAsync(
        Guid tripId,
        Guid itemId,
        Guid actorUserId,
        UpsertItineraryItemRequest request,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var trip = await ValidateMutableTripAndMembershipAsync(tripId, actorUserId, ct);
        var normalized = ValidateAndNormalizeItemRequest(request);

        var existing = await itineraryItemRepository.GetByIdAsync(itemId, ct)
            ?? throw new KeyNotFoundException("Không tìm thấy điểm dừng lịch trình.");

        if (existing.TripDay is null || existing.TripDay.TripId != trip.Id)
        {
            throw new InvalidOperationException("Điểm dừng không thuộc chuyến đi này.");
        }

        var targetDay = await tripDayRepository.GetByIdAsync(request.TripDayId, ct)
            ?? throw new KeyNotFoundException("Không tìm thấy ngày lịch trình đích.");

        if (targetDay.TripId != trip.Id)
        {
            throw new InvalidOperationException("Ngày lịch trình đích không thuộc chuyến đi này.");
        }

        var oldDayId = existing.TripDayId;
        var dayChanged = oldDayId != targetDay.Id;
        var startTimeChanged = existing.StartTime != request.StartTime;

        existing.Title = normalized.Title;
        existing.LocationName = normalized.LocationName;
        existing.Address = normalized.Address;
        existing.Latitude = request.Latitude;
        existing.Longitude = request.Longitude;
        existing.StartTime = request.StartTime;
        existing.EndTime = request.EndTime;
        existing.EstimatedCost = normalized.EstimatedCost;
        existing.Notes = normalized.Notes;

        if (dayChanged || startTimeChanged || request.StartTime is null)
        {
            existing.ReminderSentAtUtc = null;
        }

        if (dayChanged)
        {
            var nextSort = await itineraryItemRepository.GetMaxSortOrderAsync(targetDay.Id, ct) + 1;
            existing.TripDayId = targetDay.Id;
            existing.TripDay = targetDay;
            existing.SortOrder = nextSort;
            await itineraryItemRepository.UpdateAsync(existing, ct);

            var remainingOldDayItems = await itineraryItemRepository.GetByTripDayIdAsync(oldDayId, ct);
            if (remainingOldDayItems.Count > 0)
            {
                for (var idx = 0; idx < remainingOldDayItems.Count; idx++)
                {
                    remainingOldDayItems[idx].SortOrder = idx;
                }

                await itineraryItemRepository.UpdateRangeAsync(remainingOldDayItems, ct);
            }
        }
        else
        {
            await itineraryItemRepository.UpdateAsync(existing, ct);
        }

        var reloaded = await itineraryItemRepository.GetByIdAsync(existing.Id, ct) ?? existing;
        return MapItem(reloaded, targetDay, actorUserId);
    }

    public async Task DeleteItemAsync(
        Guid tripId,
        Guid itemId,
        Guid actorUserId,
        CancellationToken ct = default)
    {
        var trip = await ValidateMutableTripAndMembershipAsync(tripId, actorUserId, ct);

        var existing = await itineraryItemRepository.GetByIdAsync(itemId, ct)
            ?? throw new KeyNotFoundException("Không tìm thấy điểm dừng lịch trình.");

        if (existing.TripDay is null || existing.TripDay.TripId != trip.Id)
        {
            throw new InvalidOperationException("Điểm dừng không thuộc chuyến đi này.");
        }

        await itineraryItemRepository.DeleteAsync(existing, ct);
    }

    public async Task<TripScheduleDto> ReorderItemsAsync(
        Guid tripId,
        Guid targetTripDayId,
        IReadOnlyList<Guid> orderedItemIds,
        CancellationToken ct = default)
    {
        var trip = await tripRepository.GetByIdWithDetailsAsync(tripId, ct)
            ?? throw new KeyNotFoundException("Không tìm thấy chuyến đi.");

        EnsureTripIsMutable(trip);
        return await ReorderItemsInternalAsync(trip, targetTripDayId, trip.OwnerUserId, orderedItemIds, ct);
    }

    public async Task<TripScheduleDto> ReorderItemsAsync(
        Guid tripId,
        Guid targetTripDayId,
        Guid actorUserId,
        IReadOnlyList<Guid> orderedItemIds,
        CancellationToken ct = default)
    {
        var trip = await ValidateMutableTripAndMembershipAsync(tripId, actorUserId, ct);
        return await ReorderItemsInternalAsync(trip, targetTripDayId, actorUserId, orderedItemIds, ct);
    }

    public async Task<VoteResultDto> ToggleVoteAsync(
        Guid tripId,
        Guid itemId,
        Guid userId,
        VoteType voteType,
        CancellationToken ct = default)
    {
        if (voteType is not (VoteType.Upvote or VoteType.Downvote))
        {
            throw new ArgumentException("Loại bình chọn không hợp lệ.", nameof(voteType));
        }

        var trip = await ValidateMutableTripAndMembershipAsync(tripId, userId, ct);

        var item = await itineraryItemRepository.GetByIdAsync(itemId, ct)
            ?? throw new KeyNotFoundException("Không tìm thấy điểm dừng lịch trình.");

        if (item.TripDay is null || item.TripDay.TripId != trip.Id)
        {
            throw new InvalidOperationException("Điểm dừng không thuộc chuyến đi này.");
        }

        var existingVote = await itineraryVoteRepository.GetVoteAsync(itemId, userId, ct);
        VoteType? currentUserVote;

        if (existingVote is null)
        {
            await itineraryVoteRepository.AddAsync(new ItineraryVote
            {
                Id = Guid.NewGuid(),
                ItineraryItemId = itemId,
                UserId = userId,
                Vote = voteType,
                CreatedAtUtc = DateTime.UtcNow
            }, ct);
            currentUserVote = voteType;
        }
        else if (existingVote.Vote == voteType)
        {
            await itineraryVoteRepository.RemoveAsync(existingVote, ct);
            currentUserVote = null;
        }
        else
        {
            existingVote.Vote = voteType;
            existingVote.CreatedAtUtc = DateTime.UtcNow;
            await itineraryVoteRepository.UpdateAsync(existingVote, ct);
            currentUserVote = voteType;
        }

        var allVotes = await itineraryVoteRepository.GetVotesByItemIdAsync(itemId, ct);
        var upvotes = allVotes.Count(v => v.Vote == VoteType.Upvote);
        var downvotes = allVotes.Count(v => v.Vote == VoteType.Downvote);

        return new VoteResultDto(
            trip.Id,
            itemId,
            upvotes,
            downvotes,
            upvotes - downvotes,
            userId,
            currentUserVote);
    }

    public async Task<IReadOnlyList<DepartureReminderDispatchDto>> ProcessUpcomingDepartureRemindersAsync(
        DateTime? utcNowOverride = null,
        CancellationToken ct = default)
    {
        var nowUtc = utcNowOverride?.ToUniversalTime() ?? DateTime.UtcNow;
        var nowLocal = ToVietnamLocalTime(nowUtc);
        var windowEndLocal = nowLocal.AddMinutes(30);

        var minDate = DateOnly.FromDateTime(nowLocal);
        var maxDate = DateOnly.FromDateTime(windowEndLocal);

        var candidates = await itineraryItemRepository.GetCandidatesForDepartureReminderAsync(minDate, maxDate, ct);
        if (candidates.Count == 0)
        {
            return [];
        }

        var dispatches = new List<DepartureReminderDispatchDto>();

        foreach (var item in candidates)
        {
            if (item.StartTime is null
                || item.ReminderSentAtUtc is not null
                || item.TripDay?.Trip is null
                || item.TripDay.Trip.Status != TripStatus.Ongoing)
            {
                continue;
            }

            var scheduledLocal = item.TripDay.Date.ToDateTime(item.StartTime.Value);
            if (scheduledLocal < nowLocal || scheduledLocal > windowEndLocal)
            {
                continue;
            }

            var trip = item.TripDay.Trip;
            var activeMemberIds = trip.Members
                .Where(m => m.IsActive)
                .Select(m => m.UserId)
                .Distinct()
                .ToList();

            var minutesUntilStart = Math.Max(0, (int)Math.Round((scheduledLocal - nowLocal).TotalMinutes));
            var startTimeStr = item.StartTime.Value.ToString("HH:mm");
            var locationSuffix = string.IsNullOrWhiteSpace(item.LocationName)
                ? string.Empty
                : $" tại {item.LocationName}";
            var actionUrl = $"/Trips/Itinerary?tripId={trip.Id}#item-{item.Id}";
            var title = $"Sắp khởi hành: {item.Title}";
            var message = minutesUntilStart <= 1
                ? $"Điểm dừng \"{item.Title}\" ({startTimeStr}{locationSuffix}) trong chuyến \"{trip.Name}\" sắp bắt đầu ngay bây giờ!"
                : $"Điểm dừng \"{item.Title}\" ({startTimeStr}{locationSuffix}) trong chuyến \"{trip.Name}\" sẽ bắt đầu sau {minutesUntilStart} phút.";

            var notifications = activeMemberIds
                .Select(memberUserId => new Notification
                {
                    Id = Guid.NewGuid(),
                    UserId = memberUserId,
                    TripId = trip.Id,
                    Type = NotificationType.DepartureReminder,
                    Title = title,
                    Message = message,
                    ActionUrl = actionUrl,
                    IsRead = false,
                    CreatedAtUtc = nowUtc
                })
                .ToList();

            await itineraryItemRepository.MarkReminderSentWithNotificationsAsync(item, nowUtc, notifications, ct);

            var notificationDtos = notifications
                .Select(n => new DepartureReminderNotificationItemDto(
                    n.Id,
                    n.UserId,
                    trip.Id,
                    n.Type,
                    n.Title,
                    n.Message,
                    actionUrl,
                    n.CreatedAtUtc))
                .ToList();

            dispatches.Add(new DepartureReminderDispatchDto(
                trip.Id,
                trip.Name,
                item.Id,
                item.Title,
                item.LocationName,
                startTimeStr,
                minutesUntilStart,
                nowUtc,
                notificationDtos));
        }

        return dispatches;
    }

    private async Task<TripScheduleDto> ReorderItemsInternalAsync(
        Trip trip,
        Guid targetTripDayId,
        Guid viewerUserId,
        IReadOnlyList<Guid> orderedItemIds,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(orderedItemIds);

        var targetDay = await tripDayRepository.GetByIdAsync(targetTripDayId, ct)
            ?? throw new KeyNotFoundException("Không tìm thấy ngày lịch trình đích.");

        if (targetDay.TripId != trip.Id)
        {
            throw new InvalidOperationException("Ngày lịch trình đích không thuộc chuyến đi này.");
        }

        var allTripItems = await itineraryItemRepository.GetByTripIdAsync(trip.Id, ct);
        var itemsById = allTripItems.ToDictionary(i => i.Id);

        var distinctOrderedIds = new List<Guid>(orderedItemIds.Count);
        var seen = new HashSet<Guid>();
        foreach (var id in orderedItemIds)
        {
            if (!itemsById.ContainsKey(id))
            {
                throw new KeyNotFoundException($"Điểm dừng '{id}' không tồn tại trong chuyến đi này.");
            }

            if (seen.Add(id))
            {
                distinctOrderedIds.Add(id);
            }
        }

        var affectedSourceDayIds = new HashSet<Guid>();
        var finalTargetItems = new List<ItineraryItem>();

        foreach (var id in distinctOrderedIds)
        {
            var item = itemsById[id];
            if (item.TripDayId != targetTripDayId)
            {
                affectedSourceDayIds.Add(item.TripDayId);
                item.TripDayId = targetTripDayId;
                item.TripDay = targetDay;
                item.ReminderSentAtUtc = null;
            }

            finalTargetItems.Add(item);
        }

        // Preserve any concurrent items already on targetTripDayId that were not in orderedItemIds
        var unlistedTargetItems = allTripItems
            .Where(i => i.TripDayId == targetTripDayId && !seen.Contains(i.Id))
            .OrderBy(i => i.SortOrder)
            .ThenBy(i => i.CreatedAtUtc);

        finalTargetItems.AddRange(unlistedTargetItems);

        var modifiedItems = new List<ItineraryItem>();
        for (var idx = 0; idx < finalTargetItems.Count; idx++)
        {
            finalTargetItems[idx].SortOrder = idx;
            modifiedItems.Add(finalTargetItems[idx]);
        }

        foreach (var sourceDayId in affectedSourceDayIds)
        {
            var remainingSourceItems = allTripItems
                .Where(i => i.TripDayId == sourceDayId)
                .OrderBy(i => i.SortOrder)
                .ThenBy(i => i.CreatedAtUtc)
                .ToList();

            for (var idx = 0; idx < remainingSourceItems.Count; idx++)
            {
                remainingSourceItems[idx].SortOrder = idx;
                modifiedItems.Add(remainingSourceItems[idx]);
            }
        }

        if (modifiedItems.Count > 0)
        {
            await itineraryItemRepository.UpdateRangeAsync(modifiedItems, ct);
        }

        var reloadedDays = await tripDayRepository.GetByTripIdWithItemsAsync(trip.Id, ct);
        return MapSchedule(trip, reloadedDays, viewerUserId, isAdmin: false);
    }

    private async Task<Trip> ValidateMutableTripAndMembershipAsync(
        Guid tripId,
        Guid actorUserId,
        CancellationToken ct)
    {
        var trip = await tripRepository.GetByIdWithDetailsAsync(tripId, ct)
            ?? throw new KeyNotFoundException("Không tìm thấy chuyến đi.");

        if (!await tripService.IsActiveMemberAsync(tripId, actorUserId, ct))
        {
            throw new UnauthorizedAccessException("Bạn không phải là thành viên đang hoạt động của chuyến đi này.");
        }

        EnsureTripIsMutable(trip);
        return trip;
    }

    private static void EnsureTripIsMutable(Trip trip)
    {
        if (trip.Status is TripStatus.Settling or TripStatus.Completed)
        {
            throw new InvalidOperationException(
                "Không thể chỉnh sửa lịch trình khi chuyến đi đang tất toán hoặc đã hoàn tất.");
        }
    }

    private static (string Title, string? LocationName, string? Address, string? Notes, decimal EstimatedCost)
        ValidateAndNormalizeItemRequest(UpsertItineraryItemRequest request)
    {
        var title = (request.Title ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(title) || title.Length > 200)
        {
            throw new ArgumentException("Tên điểm dừng phải từ 1 đến 200 ký tự.", nameof(request));
        }

        var locationName = string.IsNullOrWhiteSpace(request.LocationName) ? null : request.LocationName.Trim();
        if (locationName is { Length: > 250 })
        {
            throw new ArgumentException("Tên địa điểm không được vượt quá 250 ký tự.", nameof(request));
        }

        var address = string.IsNullOrWhiteSpace(request.Address) ? null : request.Address.Trim();
        if (address is { Length: > 500 })
        {
            throw new ArgumentException("Địa chỉ không được vượt quá 500 ký tự.", nameof(request));
        }

        var notes = string.IsNullOrWhiteSpace(request.Notes) ? null : request.Notes.Trim();
        if (notes is { Length: > 2000 })
        {
            throw new ArgumentException("Ghi chú không được vượt quá 2000 ký tự.", nameof(request));
        }

        if (request.EstimatedCost < 0m)
        {
            throw new ArgumentException("Chi phí dự kiến không được âm.", nameof(request));
        }

        if (request.StartTime.HasValue && request.EndTime.HasValue && request.EndTime.Value <= request.StartTime.Value)
        {
            throw new ArgumentException("Giờ kết thúc phải lớn hơn giờ bắt đầu.", nameof(request));
        }

        var roundedCost = decimal.Round(request.EstimatedCost, 0, MidpointRounding.AwayFromZero);
        return (title, locationName, address, notes, roundedCost);
    }

    private async Task<IReadOnlyList<TripDay>> EnsureDaysSeededAndLoadedAsync(Trip trip, CancellationToken ct)
    {
        var days = await tripDayRepository.GetByTripIdWithItemsAsync(trip.Id, ct);
        if (days.Count > 0)
        {
            return days;
        }

        var dayCount = Math.Max(1, trip.EndDate.DayNumber - trip.StartDate.DayNumber + 1);
        var seededDays = Enumerable.Range(0, dayCount)
            .Select(offset => new TripDay
            {
                Id = Guid.NewGuid(),
                TripId = trip.Id,
                DayNumber = offset + 1,
                Date = trip.StartDate.AddDays(offset),
                Title = $"Ngày {offset + 1}"
            })
            .ToList();

        await tripDayRepository.AddRangeAsync(seededDays, ct);
        return await tripDayRepository.GetByTripIdWithItemsAsync(trip.Id, ct);
    }

    internal static TripScheduleDto MapSchedule(
        Trip trip,
        IReadOnlyList<TripDay> days,
        Guid viewerUserId,
        bool isAdmin)
    {
        var dayDtos = days
            .OrderBy(d => d.DayNumber)
            .Select(d => MapDay(d, viewerUserId))
            .ToList();

        var totalCost = dayDtos.Sum(d => d.DayEstimatedCost);
        var totalItems = dayDtos.Sum(d => d.Items.Count);
        var aiItems = dayDtos.Sum(d => d.Items.Count(i => i.IsAiSuggested));
        var dayCount = Math.Max(dayDtos.Count, trip.EndDate.DayNumber - trip.StartDate.DayNumber + 1);
        var activeMemberCount = Math.Max(1, trip.Members.Count(m => m.IsActive));
        var canEdit = !isAdmin && trip.Status is TripStatus.Planning or TripStatus.Ongoing;

        return new TripScheduleDto(
            trip.Id,
            trip.Name,
            trip.Destination,
            trip.StartDate,
            trip.EndDate,
            dayCount,
            trip.EstimatedBudgetPerPerson,
            activeMemberCount,
            trip.Status,
            canEdit,
            totalCost,
            totalItems,
            aiItems,
            dayDtos);
    }

    internal static TripDayDto MapDay(TripDay day, Guid viewerUserId)
    {
        var itemDtos = day.Items
            .OrderBy(i => i.SortOrder)
            .ThenBy(i => i.CreatedAtUtc)
            .Select(i => MapItem(i, day, viewerUserId))
            .ToList();

        return new TripDayDto(
            day.Id,
            day.TripId,
            day.DayNumber,
            day.Date,
            day.Title,
            day.Notes,
            itemDtos.Sum(i => i.EstimatedCost),
            itemDtos);
    }

    internal static ItineraryItemDto MapItem(ItineraryItem item, TripDay day, Guid viewerUserId)
    {
        var votes = item.Votes ?? [];
        var upvotes = votes.Count(v => v.Vote == VoteType.Upvote);
        var downvotes = votes.Count(v => v.Vote == VoteType.Downvote);
        var userVote = votes.FirstOrDefault(v => v.UserId == viewerUserId)?.Vote;

        return new ItineraryItemDto(
            item.Id,
            day.Id,
            day.DayNumber,
            day.Date,
            item.Title,
            item.LocationName,
            item.Address,
            item.Latitude,
            item.Longitude,
            item.StartTime,
            item.EndTime,
            item.StartTime?.ToString("HH:mm"),
            item.EndTime?.ToString("HH:mm"),
            item.EstimatedCost,
            item.SortOrder,
            item.Notes,
            item.IsAiSuggested,
            item.ReminderSentAtUtc,
            item.CreatedByUserId,
            item.CreatedByUser?.FullName ?? "Thành viên",
            item.CreatedAtUtc,
            upvotes,
            downvotes,
            upvotes - downvotes,
            userVote);
    }
}
