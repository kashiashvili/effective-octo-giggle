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

    /// <summary>
    /// When the user last opened their matches. Null until the first visit. Used only to tell them
    /// how many matches have refreshed since — a reason to come back, without collecting an email to
    /// notify them out of band.
    /// </summary>
    public DateTime? LastMatchesViewedAt { get; set; }

    /// <summary>
    /// Optional connection intent — the first non-interest compatibility signal. A short stable key
    /// from a small closed set (see <see cref="Profile.ConnectionIntent"/>), or null for unspecified.
    /// Shown to matches like bio/contact; never blended into a score.
    /// </summary>
    public string? ConnectionIntent { get; set; }

    /// <summary>
    /// Optional values and worldview signal: the derived profile — four value priorities and two world
    /// beliefs, each −2..+2 — as compact JSON, or null if not taken. Only this is stored; the
    /// questionnaire answers are discarded after derivation, exactly like interest features. See
    /// <see cref="Profile.ValuesQuestionnaire"/> and docs/DESIGN_VALUES.md.
    /// </summary>
    public string? ValuesProfileJson { get; set; }

    /// <summary>Scheme version the bucket was derived under, so a future item change is detectable.</summary>
    public string? ValuesScheme { get; set; }

    /// <summary>
    /// Optional, user-chosen interests to show publicly on match cards — "talk to me about…". A small
    /// capped JSON list of plain-text labels. Deliberately SEPARATE from the matching fingerprint: the
    /// interests used for matching are still derived and discarded, so the "raw interests are never
    /// stored" guarantee is untouched. This is voluntary public disclosure, the same model as
    /// <see cref="Bio"/> — it exists so a match has a concrete reason (and icebreaker) to reach out,
    /// instead of only a similarity percentage. Null when the user has shared none.
    /// </summary>
    public string? ShowableInterestsJson { get; set; }

    /// <summary>
    /// When set, the account is suspended by the operator (a moderation action on reports): it is
    /// excluded from everyone's matches and its sessions and logins are refused. Deliberately a
    /// reversible flag rather than a delete — a token-gated hard-delete would let a leaked operator
    /// token wipe accounts, whereas a suspension can be lifted. Null for an account in good standing.
    /// </summary>
    public DateTime? SuspendedAt { get; set; }

    public FingerprintRecord? Fingerprint { get; set; }
}
