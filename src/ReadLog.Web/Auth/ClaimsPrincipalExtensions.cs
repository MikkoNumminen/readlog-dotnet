using System.Security.Claims;

namespace ReadLog.Web.Auth;

public static class ClaimsPrincipalExtensions
{
    /// <summary>
    /// The signed-in user's id. Pages that call this are <c>[Authorize]</c>d, so the
    /// claim is always present; a missing claim is a programming error.
    /// </summary>
    public static string GetUserId(this ClaimsPrincipal user) =>
        user.FindFirstValue(ClaimTypes.NameIdentifier)
        ?? throw new InvalidOperationException("The authenticated user has no id claim.");
}
