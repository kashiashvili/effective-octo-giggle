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

    /// <summary>
    /// Claim recording when this sign-in was issued, so a ticket from before a password change can
    /// be told apart from one issued after it.
    /// </summary>
    public const string IssuedAtClaim = "profiler:issued_at";

    /// <summary>
    /// True when the ticket was issued before the account's cutoff — or carries no issue time at
    /// all, which is what a cookie predating this check looks like. Either way it is no longer good.
    /// </summary>
    public static bool WasIssuedBefore(this ClaimsPrincipal? principal, DateTime cutoff)
    {
        var issued = principal?.FindFirstValue(IssuedAtClaim);
        return !DateTime.TryParse(issued, null, System.Globalization.DateTimeStyles.RoundtripKind, out var issuedAt)
            || issuedAt < cutoff;
    }

    /// <summary>For code that runs before authorization, where the claim may be absent or malformed.</summary>
    public static bool TryGetUserId(this ClaimsPrincipal? principal, out int userId)
    {
        userId = 0;
        return principal is not null
            && int.TryParse(principal.FindFirstValue(ClaimTypes.NameIdentifier), out userId);
    }
}
