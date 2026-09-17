using Microsoft.AspNetCore.DataProtection;

namespace Profiler.Web.Security;

/// <summary>
/// Signed, time-limited invite links for circles. The token carries only the circle id; nothing
/// about it is stored, so there is no record of who invited whom. Any member can show the current
/// link; each render mints a fresh token and older ones stay valid until they expire.
/// </summary>
public sealed class CircleInvites
{
    private const string Purpose = "circle-invite.v1";
    public static readonly TimeSpan Lifetime = TimeSpan.FromDays(30);
    private const int MaxTokenLength = 512;

    private readonly ITimeLimitedDataProtector _protector;

    public CircleInvites(IDataProtectionProvider dataProtection) =>
        _protector = dataProtection.CreateProtector(Purpose).ToTimeLimitedDataProtector();

    public string Issue(int circleId) =>
        _protector.Protect($"{circleId}|{DateTimeOffset.UtcNow.ToUnixTimeSeconds()}", Lifetime);

    /// <summary>The circle id a token names, or null when the token is missing, tampered or expired.</summary>
    public int? TryRead(string? token)
    {
        if (string.IsNullOrEmpty(token) || token.Length > MaxTokenLength) return null;
        try
        {
            var parts = _protector.Unprotect(token).Split('|');
            return parts.Length == 2 && int.TryParse(parts[0], out var id) && id > 0 ? id : null;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>Whether a query-string value is shaped like one of our tokens (so it can be carried through a form).</summary>
    public static bool LooksLikeToken(string? value) =>
        !string.IsNullOrEmpty(value) && value.Length <= MaxTokenLength
        && value.All(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_');
}
