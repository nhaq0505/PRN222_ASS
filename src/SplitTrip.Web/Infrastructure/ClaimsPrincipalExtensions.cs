using System.Security.Claims;
using SplitTrip.Services.Auth;

namespace SplitTrip.Web.Infrastructure;

public static class ClaimsPrincipalExtensions
{
    public static bool TryGetUserId(this ClaimsPrincipal? principal, out Guid userId)
    {
        var raw = principal?.FindFirstValue(ClaimTypes.NameIdentifier);
        return Guid.TryParse(raw, out userId);
    }

    public static Guid GetRequiredUserId(this ClaimsPrincipal principal)
    {
        if (principal.TryGetUserId(out var userId))
        {
            return userId;
        }

        throw new UnauthorizedAccessException("Phiên đăng nhập không hợp lệ hoặc đã hết hạn.");
    }

    public static bool IsSystemAdmin(this ClaimsPrincipal? principal)
        => principal?.IsInRole(AuthService.AdminRole) == true;
}
