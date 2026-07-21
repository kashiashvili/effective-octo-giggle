using System.Security.Claims;

namespace Profiler.Web.Security;

public static class ClaimsPrincipalExtensions
{
    /// <summary>
    /// The signed-in user's id. Only valid on a request that passed authorization — the claim is
    /// written at sign-in and every cookie is checked against an existing user, so an authorized
    /// request always carries a parseable id.
    /// </summary>
    public static int GetUserId(this ClaimsPrincipal principal) =>
        int.Parse(principal.FindFirstValue(ClaimTypes.NameIdentifier)!);

    /// <summary>For code that runs before authorization, where the claim may be absent or malformed.</summary>
    public static bool TryGetUserId(this ClaimsPrincipal? principal, out int userId)
    {
        userId = 0;
        return principal is not null
            && int.TryParse(principal.FindFirstValue(ClaimTypes.NameIdentifier), out userId);
    }
}
