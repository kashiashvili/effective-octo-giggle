namespace Profiler.Web.Data.Models;

public class AppUser
{
    public int Id { get; set; }
    public string Username { get; set; } = "";

    /// <summary>
    /// The username reduced to a comparison form (compatibility-folded and lower-cased), so two names
    /// that read as the same person cannot both register. SQLite's NOCASE index only folds ASCII, so
    /// on its own it would let "André" and "ANDRÉ" coexist. The display casing lives in
    /// <see cref="Username"/>; this is only ever compared, never shown.
    /// </summary>
    public string NormalizedUsername { get; set; } = "";

    public string PasswordHash { get; set; } = "";
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>Optional, user-authored public bio shown to people they match with. Plain text.</summary>
    public string? Bio { get; set; }

    /// <summary>Optional, user-chosen way to be reached (e.g. "@me on Mastodon"). Plain text, never a live link.</summary>
    public string? Contact { get; set; }

    /// <summary>When false the user keeps their fingerprint but does not appear in anyone else's matches.</summary>
    public bool IsDiscoverable { get; set; } = true;

    /// <summary>
    /// BCrypt hash of the single-use recovery code. Null for accounts created before recovery
    /// existed, and for anyone who has used their code without taking the replacement — such an
    /// account simply has no recovery path until one is generated.
    /// </summary>
    public string? RecoveryCodeHash { get; set; }

    /// <summary>
    /// Sign-ins issued before this moment are no longer honoured. Cookies are persistent for 30
    /// days, so without this a changed password does nothing about the session that prompted the
    /// change — the one remediation the product offers would be theatre.
    /// </summary>
    public DateTime SessionsValidFrom { get; set; } = DateTime.UtcNow;

    public FingerprintRecord? Fingerprint { get; set; }
}
