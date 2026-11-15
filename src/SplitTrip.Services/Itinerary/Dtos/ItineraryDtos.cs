using SplitTrip.BusinessObjects.Enums;

namespace SplitTrip.Services.Itinerary.Dtos;

public sealed record TripScheduleDto(
    Guid TripId,
    string TripName,
    string Destination,
    DateOnly StartDate,
    DateOnly EndDate,
    int DayCount,
    decimal EstimatedBudgetPerPerson,
    int ActiveMemberCount,
    TripStatus Status,
    bool CanEdit,
    decimal TotalEstimatedCost,
    int TotalItemsCount,
    int AiSuggestedItemsCount,
    IReadOnlyList<TripDayDto> Days);

public sealed record TripDayDto(
    Guid Id,
    Guid TripId,
    int DayNumber,
    DateOnly Date,
    string? Title,
    string? Notes,
    decimal DayEstimatedCost,
    IReadOnlyList<ItineraryItemDto> Items);

public sealed record ItineraryItemDto(
    Guid Id,
    Guid TripDayId,
    int DayNumber,
    DateOnly DayDate,
    string Title,
    string? LocationName,
    string? Address,
    double? Latitude,
    double? Longitude,
    TimeOnly? StartTime,
    TimeOnly? EndTime,
    string? StartTimeFormatted,
    string? EndTimeFormatted,
    decimal EstimatedCost,
    int SortOrder,
    string? Notes,
    bool IsAiSuggested,
    DateTime? ReminderSentAtUtc,
    Guid CreatedByUserId,
    string CreatedByName,
    DateTime CreatedAtUtc,
    int Upvotes,
    int Downvotes,
    int NetScore,
    VoteType? CurrentUserVote);

public sealed record UpdateTripDayRequest(
    string? Title,
    string? Notes);

public sealed record UpsertItineraryItemRequest(
    Guid TripDayId,
    string Title,
    string? LocationName,
    string? Address,
    TimeOnly? StartTime,
    TimeOnly? EndTime,
    decimal EstimatedCost,
    string? Notes,
    double? Latitude = null,
    double? Longitude = null);

public sealed record VoteResultDto(
    Guid TripId,
    Guid ItineraryItemId,
    int Upvotes,
    int Downvotes,
    int NetScore,
    Guid UserId,
    VoteType? CurrentUserVote);

public sealed record DepartureReminderNotificationItemDto(
    Guid NotificationId,
    Guid UserId,
    Guid TripId,
    NotificationType Type,
    string Title,
    string Message,
    string ActionUrl,
    DateTime CreatedAtUtc);

public sealed record DepartureReminderDispatchDto(
    Guid TripId,
    string TripName,
    Guid ItineraryItemId,
    string ItemTitle,
    string? LocationName,
    string StartTimeFormatted,
    int MinutesUntilStart,
    DateTime SentAtUtc,
    IReadOnlyList<DepartureReminderNotificationItemDto> UserNotifications);

public sealed record AiTripPlanRequestDto(
    string TravelStyle = "Cân bằng",
    decimal? BudgetPerPersonOverride = null,
    string? PreferencesPrompt = null,
    bool AllowFallbackOnApiError = true);

public sealed record AiSuggestedItemDto(
    string Title,
    string? LocationName,
    string? Address,
    string? StartTime,
    string? EndTime,
    decimal EstimatedCost,
    string? Notes);

public sealed record AiSuggestedDayDto(
    int DayNumber,
    DateOnly Date,
    string Title,
    string? Notes,
    IReadOnlyList<AiSuggestedItemDto> Items);

public sealed record AiTripPlanPreviewDto(
    Guid TripId,
    string Destination,
    int DayCount,
    decimal BudgetPerPerson,
    string TravelStyle,
    bool UsedFallback,
    string SourceLabel,
    string? FallbackReason,
    decimal TotalEstimatedCost,
    IReadOnlyList<AiSuggestedDayDto> Days);

public sealed record ApplyAiTripPlanRequestDto(
    IReadOnlyList<AiSuggestedDayDto> Days,
    bool ClearExistingAiItems = false);
