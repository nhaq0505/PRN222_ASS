using System.Security.Claims;
using CourtBook.BusinessObjects.Enums;

namespace CourtBook.Web.Extensions;

/// <summary>
/// Convenience helpers for extracting CourtBook identity claims from <see cref="ClaimsPrincipal"/>.
/// </summary>
public static class ClaimsPrincipalExtensions
{
    public static int? GetUserId(this ClaimsPrincipal? user)
    {
        if (user?.Identity?.IsAuthenticated != true)
        {
            return null;
        }

        string? raw = user.FindFirstValue(ClaimTypes.NameIdentifier);
        return int.TryParse(raw, out int userId) ? userId : null;
    }

    public static UserRole? GetUserRole(this ClaimsPrincipal? user)
    {
        if (user?.Identity?.IsAuthenticated != true)
        {
            return null;
        }

        string? raw = user.FindFirstValue(ClaimTypes.Role);
        return Enum.TryParse<UserRole>(raw, ignoreCase: true, out UserRole role) ? role : null;
    }

    public static bool IsAdmin(this ClaimsPrincipal? user)
        => user.GetUserRole() == UserRole.Admin;

    public static bool IsCourtOwner(this ClaimsPrincipal? user)
        => user.GetUserRole() == UserRole.CourtOwner;

    public static bool IsOwnerOrAdmin(this ClaimsPrincipal? user)
        => user.GetUserRole() is UserRole.CourtOwner or UserRole.Admin;

    [Obsolete("Use IsOwnerOrAdmin instead.")]
    public static bool IsAdminOrStaff(this ClaimsPrincipal? user)
        => user.IsOwnerOrAdmin();

    public static string GetRoleDisplayName(this ClaimsPrincipal? user) => user.GetUserRole() switch
    {
        UserRole.Admin => "Quản trị sàn",
        UserRole.CourtOwner => "Chủ cụm sân",
        UserRole.Member => "Hội viên",
        _ => string.Empty
    };
}
