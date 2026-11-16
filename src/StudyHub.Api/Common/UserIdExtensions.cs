using System.Security.Claims;

namespace StudyHub.Api.Common;

public static class UserIdExtensions
{
    /// <summary>Reads the signed-in user's id from the JWT "sub" claim.</summary>
    public static Guid GetUserId(this ClaimsPrincipal principal)
    {
        return principal.GetUserIdOrNull()
               ?? throw new UnauthorizedException("Invalid or missing user id in token.");
    }

    /// <summary>Same, but returns null for visitors who are not logged in.</summary>
    public static Guid? GetUserIdOrNull(this ClaimsPrincipal principal)
    {
        var value = principal.FindFirst("sub")?.Value
                    ?? principal.FindFirst(ClaimTypes.NameIdentifier)?.Value;

        if (Guid.TryParse(value, out var id))
        {
            return id;
        }

        return null;
    }
}
