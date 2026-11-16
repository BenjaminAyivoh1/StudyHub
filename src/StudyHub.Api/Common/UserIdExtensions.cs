using System.Security.Claims;

namespace StudyHub.Api.Common;

public static class UserIdExtensions
{
    /// <summary>Reads the signed-in user's id from the JWT "sub" claim.</summary>
    public static Guid GetUserId(this ClaimsPrincipal principal)
    {
        var value = principal.FindFirst("sub")?.Value
                    ?? principal.FindFirst(ClaimTypes.NameIdentifier)?.Value;

        return Guid.TryParse(value, out var id)
            ? id
            : throw new UnauthorizedException("Invalid or missing user id in token.");
    }
}
