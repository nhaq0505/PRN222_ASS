using CourtBook.BusinessObjects.Entities;
using CourtBook.Repositories;

namespace CourtBook.Services.Helpers;

/// <summary>
/// Ensures mutating operations are performed only by existing, active user accounts.
/// </summary>
internal static class ActorGuard
{
    public const string InactiveOrMissingMessage = "Tài khoản của bạn đã bị tạm khóa hoặc không tồn tại.";

    public static async Task<AppUser?> GetActiveActorAsync(
        IUserRepository userRepository,
        int actorUserId,
        CancellationToken ct = default)
    {
        AppUser? actor = await userRepository.GetByIdAsync(actorUserId, ct);
        return actor is not null && actor.IsActive ? actor : null;
    }

    public static async Task<bool> IsActiveAsync(
        IUserRepository userRepository,
        int actorUserId,
        CancellationToken ct = default)
        => await GetActiveActorAsync(userRepository, actorUserId, ct) is not null;
}
