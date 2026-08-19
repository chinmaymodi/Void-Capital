using System.Security.Claims;

namespace VoidCapital.Api.Shared;

/// <summary>
/// Authorization helpers over the ClaimsPrincipal produced by
/// <see cref="Modules.Auth.Services.ApiKeyAuthenticationHandler"/>.
/// </summary>
public static class ClaimsPrincipalExtensions
{
    public static bool IsAdmin(this ClaimsPrincipal user) => user.IsInRole("Admin");

    /// <summary>
    /// Admin can access any user's data; a regular user can only access their
    /// own (NameIdentifier claim holds the user id).
    /// </summary>
    public static bool CanAccess(this ClaimsPrincipal user, int userId) =>
        user.IsAdmin() || user.FindFirstValue(ClaimTypes.NameIdentifier) == userId.ToString();
}