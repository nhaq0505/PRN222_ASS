using SplitTrip.BusinessObjects.Enums;

namespace SplitTrip.Services.Trips.Dtos;

public sealed record CreateTripRequest(
    string Name,
    string Destination,
    string? Description,
    DateOnly StartDate,
    DateOnly EndDate,
    decimal EstimatedBudgetPerPerson);

public sealed record MemberNetBalanceDto(
    Guid UserId,
    string FullName,
    string Email,
    string? BankCode,
    string? BankBin,
    string? BankAccountNumber,
    string? BankAccountHolder,
    decimal TotalPaid,
    decimal TotalOwed,
    decimal SettlementsPaid,
    decimal SettlementsReceived,
    decimal NetBalance,
    TripMemberRole Role = TripMemberRole.Member,
    bool IsActive = true);

public sealed record TripSummaryDto(
    Guid Id,
    string Name,
    string Destination,
    string InviteCode,
    DateOnly StartDate,
    DateOnly EndDate,
    decimal EstimatedBudgetPerPerson,
    TripStatus Status,
    Guid OwnerUserId,
    string OwnerName,
    int ActiveMemberCount,
    DateTime CreatedAtUtc);

public sealed record TripDayOverviewDto(
    Guid Id,
    int DayNumber,
    DateOnly Date,
    string? Title);

public sealed record TripDetailsDto(
    Guid Id,
    string Name,
    string Destination,
    string? Description,
    string InviteCode,
    DateOnly StartDate,
    DateOnly EndDate,
    int DayCount,
    decimal EstimatedBudgetPerPerson,
    TripStatus Status,
    Guid OwnerUserId,
    string OwnerName,
    DateTime CreatedAtUtc,
    DateTime? ClosedAtUtc,
    IReadOnlyList<TripDayOverviewDto> Days,
    IReadOnlyList<MemberNetBalanceDto> MemberBalances);

public sealed record JoinTripResultDto(
    Guid TripId,
    string TripName,
    string InviteCode,
    bool WasReactivated);
