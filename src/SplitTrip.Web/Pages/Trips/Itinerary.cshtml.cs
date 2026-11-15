using System.Globalization;
using System.Security.Claims;
using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.SignalR;
using SplitTrip.BusinessObjects.Enums;
using SplitTrip.Services.Itinerary;
using SplitTrip.Services.Itinerary.Dtos;
using SplitTrip.Services.Trips;
using SplitTrip.Services.Trips.Dtos;
using SplitTrip.Web.Hubs;
using SplitTrip.Web.Infrastructure;

namespace SplitTrip.Web.Pages.Trips;

[Authorize]
public sealed class ItineraryModel(
    ITripService tripService,
    IItineraryService itineraryService,
    IAiTripPlannerService aiPlannerService,
    IHubContext<TripHub> hubContext) : PageModel
{
    private static readonly JsonSerializerOptions CamelCaseJson = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public TripDetailsDto Trip { get; private set; } = default!;
    public TripScheduleDto Schedule { get; private set; } = default!;
    public Guid CurrentUserId { get; private set; }
    public bool IsOwner { get; private set; }
    public bool IsAdmin { get; private set; }
    public string InitialScheduleJson { get; private set; } = "{}";

    public async Task<IActionResult> OnGetAsync(Guid? tripId, Guid? id, CancellationToken ct)
    {
        var resolvedTripId = tripId ?? id;
        if (resolvedTripId is null || resolvedTripId == Guid.Empty)
        {
            return RedirectToPage("/Index");
        }

        try
        {
            await LoadWorkspaceAsync(resolvedTripId.Value, ct);
            InitialScheduleJson = JsonSerializer.Serialize(Schedule, CamelCaseJson);
            return Page();
        }
        catch (Exception ex) when (ex is KeyNotFoundException or UnauthorizedAccessException)
        {
            TempData["ErrorMessage"] = ex.Message;
            return RedirectToPage("/Index");
        }
    }

    public async Task<IActionResult> OnGetScheduleJsonAsync(Guid tripId, CancellationToken ct)
    {
        try
        {
            var userId = User.GetRequiredUserId();
            var isAdmin = User.IsSystemAdmin();
            var schedule = await itineraryService.GetScheduleAsync(tripId, userId, isAdmin, ct);
            return new JsonResult(schedule, CamelCaseJson);
        }
        catch (Exception ex) when (ex is KeyNotFoundException or UnauthorizedAccessException)
        {
            return new JsonResult(new { error = ex.Message }, CamelCaseJson)
            {
                StatusCode = StatusCodes.Status403Forbidden
            };
        }
    }

    public async Task<IActionResult> OnPostSaveItemAsync(
        [FromQuery] Guid tripId,
        [FromBody] SaveStopPayload? payload,
        CancellationToken ct)
    {
        if (payload is null)
        {
            return BadRequestJson("Dữ liệu điểm dừng không hợp lệ.");
        }

        var actorId = User.GetRequiredUserId();
        var actorName = User.FindFirstValue(ClaimTypes.Name) ?? "Thành viên";

        if (!TryParseOptionalTime(payload.StartTime, out var startTime))
        {
            return BadRequestJson("Giờ bắt đầu không đúng định dạng HH:mm.");
        }

        if (!TryParseOptionalTime(payload.EndTime, out var endTime))
        {
            return BadRequestJson("Giờ kết thúc không đúng định dạng HH:mm.");
        }

        try
        {
            var request = new UpsertItineraryItemRequest(
                payload.TripDayId,
                payload.Title ?? string.Empty,
                payload.LocationName,
                payload.Address,
                startTime,
                endTime,
                payload.EstimatedCost,
                payload.Notes);

            ItineraryItemDto savedItem;
            string eventName;
            string toastMsg;

            if (payload.ItemId is { } existingId && existingId != Guid.Empty)
            {
                savedItem = await itineraryService.UpdateItemAsync(tripId, existingId, actorId, request, ct);
                eventName = "ItineraryItemUpdated";
                toastMsg = $"{actorName} vừa cập nhật điểm dừng \"{savedItem.Title}\".";
            }
            else
            {
                savedItem = await itineraryService.AddItemAsync(tripId, actorId, request, ct);
                eventName = "ItineraryItemAdded";
                toastMsg = $"{actorName} vừa thêm điểm dừng \"{savedItem.Title}\".";
            }

            await hubContext.Clients.Group(TripHub.TripGroupName(tripId))
                .SendAsync(
                    eventName,
                    new
                    {
                        tripId,
                        item = savedItem,
                        actorUserId = actorId,
                        message = toastMsg
                    },
                    ct);

            var schedule = await itineraryService.GetScheduleAsync(tripId, actorId, isAdmin: false, ct);
            return new JsonResult(new { ok = true, item = savedItem, schedule }, CamelCaseJson);
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or UnauthorizedAccessException or KeyNotFoundException)
        {
            return BadRequestJson(ex.Message);
        }
    }

    public async Task<IActionResult> OnPostDeleteItemAsync(
        [FromQuery] Guid tripId,
        [FromBody] DeleteStopPayload? payload,
        CancellationToken ct)
    {
        if (payload is null || payload.ItemId == Guid.Empty)
        {
            return BadRequestJson("Mã điểm dừng cần xóa không hợp lệ.");
        }

        var actorId = User.GetRequiredUserId();
        var actorName = User.FindFirstValue(ClaimTypes.Name) ?? "Thành viên";

        try
        {
            await itineraryService.DeleteItemAsync(tripId, payload.ItemId, actorId, ct);

            await hubContext.Clients.Group(TripHub.TripGroupName(tripId))
                .SendAsync(
                    "ItineraryItemDeleted",
                    new
                    {
                        tripId,
                        itemId = payload.ItemId,
                        actorUserId = actorId,
                        message = $"{actorName} vừa xóa một điểm dừng khỏi lịch trình."
                    },
                    ct);

            var schedule = await itineraryService.GetScheduleAsync(tripId, actorId, isAdmin: false, ct);
            return new JsonResult(new { ok = true, schedule }, CamelCaseJson);
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or UnauthorizedAccessException or KeyNotFoundException)
        {
            return BadRequestJson(ex.Message);
        }
    }

    public async Task<IActionResult> OnPostUpdateDayAsync(
        [FromQuery] Guid tripId,
        [FromBody] SaveDayPayload? payload,
        CancellationToken ct)
    {
        if (payload is null || payload.TripDayId == Guid.Empty)
        {
            return BadRequestJson("Thông tin ngày lịch trình không hợp lệ.");
        }

        var actorId = User.GetRequiredUserId();
        var actorName = User.FindFirstValue(ClaimTypes.Name) ?? "Thành viên";

        try
        {
            var day = await itineraryService.UpdateDayAsync(
                tripId,
                payload.TripDayId,
                actorId,
                new UpdateTripDayRequest(payload.Title, payload.Notes),
                ct);

            await hubContext.Clients.Group(TripHub.TripGroupName(tripId))
                .SendAsync(
                    "ItineraryDayUpdated",
                    new
                    {
                        tripId,
                        dayId = day.Id,
                        actorUserId = actorId,
                        message = $"{actorName} vừa cập nhật tiêu đề/ghi chú Ngày {day.DayNumber}."
                    },
                    ct);

            var schedule = await itineraryService.GetScheduleAsync(tripId, actorId, isAdmin: false, ct);
            return new JsonResult(new { ok = true, day, schedule }, CamelCaseJson);
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or UnauthorizedAccessException or KeyNotFoundException)
        {
            return BadRequestJson(ex.Message);
        }
    }

    public async Task<IActionResult> OnPostReorderAsync(
        [FromQuery] Guid tripId,
        [FromBody] ReorderPayload? payload,
        CancellationToken ct)
    {
        if (payload is null || payload.TargetTripDayId == Guid.Empty)
        {
            return BadRequestJson("Dữ liệu sắp xếp lịch trình không hợp lệ.");
        }

        var actorId = User.GetRequiredUserId();
        var actorName = User.FindFirstValue(ClaimTypes.Name) ?? "Thành viên";

        try
        {
            var schedule = await itineraryService.ReorderItemsAsync(
                tripId,
                payload.TargetTripDayId,
                actorId,
                payload.OrderedItemIds ?? [],
                ct);

            await hubContext.Clients.Group(TripHub.TripGroupName(tripId))
                .SendAsync(
                    "ItineraryReordered",
                    new
                    {
                        tripId,
                        targetTripDayId = payload.TargetTripDayId,
                        orderedItemIds = payload.OrderedItemIds ?? [],
                        actorUserId = actorId,
                        message = $"{actorName} vừa sắp xếp lại thứ tự lịch trình."
                    },
                    ct);

            return new JsonResult(new { ok = true, schedule }, CamelCaseJson);
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or UnauthorizedAccessException or KeyNotFoundException)
        {
            return BadRequestJson(ex.Message);
        }
    }

    public async Task<IActionResult> OnPostVoteAsync(
        [FromQuery] Guid tripId,
        [FromBody] VotePayload? payload,
        CancellationToken ct)
    {
        if (payload is null || payload.ItemId == Guid.Empty)
        {
            return BadRequestJson("Dữ liệu bình chọn không hợp lệ.");
        }

        var actorId = User.GetRequiredUserId();

        try
        {
            var result = await itineraryService.ToggleVoteAsync(
                tripId,
                payload.ItemId,
                actorId,
                payload.VoteType,
                ct);

            await hubContext.Clients.Group(TripHub.TripGroupName(tripId))
                .SendAsync("ItineraryVoteUpdated", result, ct);

            return new JsonResult(new { ok = true, vote = result }, CamelCaseJson);
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or UnauthorizedAccessException or KeyNotFoundException)
        {
            return BadRequestJson(ex.Message);
        }
    }

    public async Task<IActionResult> OnPostAiPreviewAsync(
        [FromQuery] Guid tripId,
        [FromBody] AiPreviewPayload? payload,
        CancellationToken ct)
    {
        var actorId = User.GetRequiredUserId();

        try
        {
            var preview = await aiPlannerService.PreviewPlanAsync(
                tripId,
                actorId,
                new AiTripPlanRequestDto(
                    payload?.TravelStyle ?? "Cân bằng",
                    payload?.BudgetPerPerson,
                    payload?.PreferencesPrompt,
                    AllowFallbackOnApiError: true),
                ct);

            return new JsonResult(new { ok = true, preview }, CamelCaseJson);
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or UnauthorizedAccessException or KeyNotFoundException)
        {
            return BadRequestJson(ex.Message);
        }
    }

    public async Task<IActionResult> OnPostAiApplyAsync(
        [FromQuery] Guid tripId,
        [FromBody] AiApplyPayload? payload,
        CancellationToken ct)
    {
        if (payload?.Days is null || payload.Days.Count == 0)
        {
            return BadRequestJson("Chưa có dữ liệu lịch trình gợi ý để áp dụng.");
        }

        var actorId = User.GetRequiredUserId();
        var actorName = User.FindFirstValue(ClaimTypes.Name) ?? "Thành viên";

        try
        {
            var schedule = await aiPlannerService.ApplyPlanAsync(
                tripId,
                actorId,
                new ApplyAiTripPlanRequestDto(payload.Days, payload.ClearExistingAiItems),
                ct);

            await hubContext.Clients.Group(TripHub.TripGroupName(tripId))
                .SendAsync(
                    "AiItineraryGenerated",
                    new
                    {
                        tripId,
                        actorUserId = actorId,
                        message = $"{actorName} vừa áp dụng lịch trình gợi ý từ AI Trip Planner!"
                    },
                    ct);

            return new JsonResult(new { ok = true, schedule }, CamelCaseJson);
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or UnauthorizedAccessException or KeyNotFoundException)
        {
            return BadRequestJson(ex.Message);
        }
    }

    private async Task LoadWorkspaceAsync(Guid tripId, CancellationToken ct)
    {
        CurrentUserId = User.GetRequiredUserId();
        IsAdmin = User.IsSystemAdmin();
        Trip = await tripService.GetTripDetailsAsync(tripId, CurrentUserId, IsAdmin, ct);
        Schedule = await itineraryService.GetScheduleAsync(tripId, CurrentUserId, IsAdmin, ct);
        IsOwner = Trip.OwnerUserId == CurrentUserId;
    }

    private static bool TryParseOptionalTime(string? raw, out TimeOnly? parsed)
    {
        parsed = null;
        if (string.IsNullOrWhiteSpace(raw))
        {
            return true;
        }

        string[] formats = ["HH:mm", "H:mm", "HH:mm:ss"];
        if (TimeOnly.TryParseExact(raw.Trim(), formats, CultureInfo.InvariantCulture, DateTimeStyles.None, out var value))
        {
            parsed = value;
            return true;
        }

        return false;
    }

    private static JsonResult BadRequestJson(string error)
        => new(new { ok = false, error }, CamelCaseJson)
        {
            StatusCode = StatusCodes.Status400BadRequest
        };

    public sealed record SaveStopPayload(
        Guid? ItemId,
        Guid TripDayId,
        string? Title,
        string? LocationName,
        string? Address,
        string? StartTime,
        string? EndTime,
        decimal EstimatedCost,
        string? Notes);

    public sealed record DeleteStopPayload(Guid ItemId);

    public sealed record SaveDayPayload(
        Guid TripDayId,
        string? Title,
        string? Notes);

    public sealed record ReorderPayload(
        Guid TargetTripDayId,
        List<Guid>? OrderedItemIds);

    public sealed record VotePayload(
        Guid ItemId,
        VoteType VoteType);

    public sealed record AiPreviewPayload(
        string? TravelStyle,
        decimal? BudgetPerPerson,
        string? PreferencesPrompt);

    public sealed record AiApplyPayload(
        List<AiSuggestedDayDto>? Days,
        bool ClearExistingAiItems = false);
}
