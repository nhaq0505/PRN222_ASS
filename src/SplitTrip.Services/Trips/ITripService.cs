using SplitTrip.BusinessObjects.Enums;
using SplitTrip.Services.Trips.Dtos;

namespace SplitTrip.Services.Trips;

public interface ITripService
{
    Task<TripDetailsDto> CreateTripAsync(Guid ownerUserId, CreateTripRequest request, CancellationToken ct = default);
    Task<JoinTripResultDto> JoinByInviteCodeAsync(Guid userId, string inviteCode, CancellationToken ct = default);
    Task<IReadOnlyList<TripSummaryDto>> GetUserTripsAsync(Guid userId, CancellationToken ct = default);
    Task<TripDetailsDto> GetTripDetailsAsync(Guid tripId, Guid viewerUserId, bool isAdmin = false, CancellationToken ct = default);
    Task<IReadOnlyList<MemberNetBalanceDto>> GetTripMemberBalancesAsync(Guid tripId, CancellationToken ct = default);
    Task<TripStatus> TransitionStatusAsync(Guid tripId, Guid actorUserId, TripStatus targetStatus, CancellationToken ct = default);
    Task RemoveOrLeaveMemberAsync(Guid tripId, Guid actorUserId, Guid targetUserId, CancellationToken ct = default);
    Task RemoveMemberAsync(Guid tripId, Guid actorUserId, Guid targetUserId, CancellationToken ct = default);
    Task<bool> IsActiveMemberAsync(Guid tripId, Guid userId, CancellationToken ct = default);
}
