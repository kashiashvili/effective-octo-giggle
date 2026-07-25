using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Caching.Memory;

namespace Profiler.Web.Security;

/// <summary>
/// Privacy-preserving anti-sybil for registration — no third-party CAPTCHA (which would embed a
/// tracker into a product whose whole promise is not tracking you), no external service, no PII.
///
/// Two cheap, self-hosted layers:
///  - a <b>honeypot</b> field hidden from humans (always checked; it never rejects a real person, so
///    it costs nothing to leave on), and
///  - a <b>signed, single-use, time-limited form ticket</b> that proves the registration form was
///    actually loaded and is being submitted once, within a sane window — blocking scripts that POST
///    straight at the endpoint in a loop or stockpile/replay one form load.
///
/// The ticket check is off by default (<c>AntiAbuse:GuardRegistration</c>) so local dev and the test
/// suite are undisturbed; flip it on for a public launch. The per-IP registration rate limit remains
/// the always-on volume cap. Single-use is tracked in-process, so a multi-instance deployment wanting
/// strict single-use needs a shared cache — noted for when scale makes it matter.
/// </summary>
public sealed class RegistrationGuard
{
    private const string Purpose = "registration-form-ticket.v1";
    private static readonly TimeSpan Lifetime = TimeSpan.FromMinutes(30);

    private readonly ITimeLimitedDataProtector _protector;
    private readonly IMemoryCache _usedTickets;
    private readonly IConfiguration _config;

    public RegistrationGuard(IDataProtectionProvider dataProtection, IMemoryCache usedTickets, IConfiguration config)
    {
        _protector = dataProtection.CreateProtector(Purpose).ToTimeLimitedDataProtector();
        _usedTickets = usedTickets;
        _config = config;
    }

    /// <summary>Whether the signed-ticket / timing / single-use checks are enforced.</summary>
    public bool Enabled => _config.GetValue("AntiAbuse:GuardRegistration", false);

    /// <summary>A form submitted faster than this after loading is treated as automated. Used only when enabled.</summary>
    private int MinFormSeconds => _config.GetValue("AntiAbuse:MinFormSeconds", 3);

    /// <summary>Mint a ticket to embed in the registration form. Only meaningful when <see cref="Enabled"/>.</summary>
    public string IssueTicket()
    {
        var id = Guid.NewGuid().ToString("N");
        var issued = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        return _protector.Protect($"{id}|{issued}", Lifetime);
    }

    /// <summary>Convenience: a ticket for the form when enabled, otherwise null (nothing to carry).</summary>
    public string? IssueTicketIfEnabled() => Enabled ? IssueTicket() : null;

    /// <summary>
    /// Returns null when the submission looks human, or a user-facing reason to reject it. Does NOT
    /// consume the ticket: a rejected retry (e.g. "username taken") must be able to resubmit the same
    /// form. Call <see cref="MarkUsed"/> only once a real account has been created.
    /// </summary>
    public string? Validate(string? ticket, string? honeypot)
    {
        // Always on: a real person (and the test suite) never fills the decoy field. Deliberately vague
        // to the client so a bot learns nothing about why it failed.
        if (!string.IsNullOrEmpty(honeypot))
            return "Something went wrong. Please try again.";

        if (!Enabled)
            return null;

        if (TryReadTicketId(ticket, out var id, out var issuedUnix) is { } parseError)
            return parseError;

        if (DateTimeOffset.UtcNow.ToUnixTimeSeconds() - issuedUnix < MinFormSeconds)
            return "That was a little too quick — please take a moment and try again.";

        if (_usedTickets.TryGetValue(id!, out _))
            return "This form was already submitted. Please reload the page and try again.";

        return null;
    }

    /// <summary>Consume the ticket so it cannot mint a second account. Call after a successful sign-up.</summary>
    public void MarkUsed(string? ticket)
    {
        if (!Enabled) return;
        if (TryReadTicketId(ticket, out var id, out _) == null)
            _usedTickets.Set(id!, true, Lifetime);
    }

    /// <summary>Unprotect + parse a ticket. Returns null on success (with id/issued out), else the reason.</summary>
    private string? TryReadTicketId(string? ticket, out string? id, out long issuedUnix)
    {
        id = null;
        issuedUnix = 0;
        if (string.IsNullOrEmpty(ticket))
            return "Please reload the page and try again.";

        string payload;
        try
        {
            payload = _protector.Unprotect(ticket); // fails if tampered or older than Lifetime
        }
        catch
        {
            return "This form has expired. Please reload the page and try again.";
        }

        var parts = payload.Split('|');
        if (parts.Length != 2 || !long.TryParse(parts[1], out issuedUnix))
            return "Please reload the page and try again.";

        id = parts[0];
        return null;
    }
}
