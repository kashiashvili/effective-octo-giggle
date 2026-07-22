using System.Security.Claims;
using Profiler.Web.Security;
using Xunit;

namespace Profiler.Web.Tests;

/// <summary>
/// The cutoff comparison runs on every authenticated request and decides whether a session is still
/// good. It has to fail closed: a ticket whose issue time is missing or unreadable — a cookie from
/// before this check existed, or a tampered one — must be treated as too old, never as fine.
/// </summary>
public class ClaimsPrincipalExtensionsTests
{
    private static ClaimsPrincipal PrincipalWith(params Claim[] claims) =>
        new(new ClaimsIdentity(claims, "test"));

    private static ClaimsPrincipal IssuedAt(DateTime when) =>
        PrincipalWith(new Claim(ClaimsPrincipalExtensions.IssuedAtClaim, when.ToString("O")));

    [Fact]
    public void ATicketIssuedBeforeTheCutoff_IsRejected()
    {
        var cutoff = DateTime.UtcNow;
        Assert.True(IssuedAt(cutoff.AddMinutes(-1)).WasIssuedBefore(cutoff));
    }

    [Fact]
    public void ATicketIssuedAfterTheCutoff_Survives()
    {
        var cutoff = DateTime.UtcNow;
        Assert.False(IssuedAt(cutoff.AddMinutes(1)).WasIssuedBefore(cutoff));
    }

    [Fact]
    public void ATicketWithNoIssueTimeClaim_IsRejected()
    {
        // A cookie from before the cutoff mechanism existed. It carries no issue time, so it cannot
        // be shown to be recent — treat it as expired.
        var principal = PrincipalWith(new Claim(ClaimTypes.NameIdentifier, "7"));
        Assert.True(principal.WasIssuedBefore(DateTime.UtcNow));
    }

    [Theory]
    [InlineData("not-a-date")]
    [InlineData("")]
    [InlineData("99999999")]
    public void ATicketWithAnUnreadableIssueTime_IsRejected(string garbage)
    {
        var principal = PrincipalWith(new Claim(ClaimsPrincipalExtensions.IssuedAtClaim, garbage));
        Assert.True(principal.WasIssuedBefore(DateTime.UtcNow));
    }

    [Fact]
    public void ANullPrincipal_IsRejected()
    {
        Assert.True(((ClaimsPrincipal?)null).WasIssuedBefore(DateTime.UtcNow));
    }
}
