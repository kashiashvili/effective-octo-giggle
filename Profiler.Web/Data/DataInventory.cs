using Profiler.Web.Data.Models;

namespace Profiler.Web.Data;

/// <summary>One kind of stored data, described for the person it is about, naming every column it covers.</summary>
public record StoredKind(Type Entity, string Name, string What, string Control, string[] Columns);

/// <summary>
/// The complete list of what the database holds, in plain words. A test holds it to the EF model
/// column by column: a table or column nobody described here fails the build, and so does a
/// description of something that no longer exists. Rendered on the privacy page — the promise is
/// only worth something if it stays literally true as the product grows.
/// </summary>
public static class DataInventory
{
    public static readonly IReadOnlyList<StoredKind> Kinds = new List<StoredKind>
    {
        new(typeof(AppUser), "Your account",
            "Username (and a lower-cased comparison form of it, so lookalike names cannot register), a " +
            "BCrypt hash of your password, a hash of your recovery code, when you registered, when you last " +
            "opened your matches, whether you are discoverable, the moment after which your sign-ins count " +
            "(moved when you change your password or sign out everywhere), and — only if you chose to add " +
            "them — a bio, a contact line, a connection intent, a values and worldview profile (six small " +
            "numbers: four value priorities and two world beliefs, each −2..+2, tagged with the questionnaire " +
            "version; the answers are discarded) and the interests you chose to show. If an operator " +
            "suspended the account, when.",
            "Edit or clear each optional field on your dashboard and profile; delete the account to remove everything.",
            new[] { "Id", "Username", "NormalizedUsername", "PasswordHash", "RecoveryCodeHash", "CreatedAt", "LastMatchesViewedAt",
                    "IsDiscoverable", "SessionsValidFrom", "Bio", "Contact", "ConnectionIntent", "ValuesProfileJson", "ValuesScheme",
                    "ShowableInterestsJson", "SuspendedAt" }),
        new(typeof(FingerprintRecord), "Your fingerprint",
            "The combined MinHash signature used for matching, the names of the sources it came from, and " +
            "when it was last rebuilt. Not the interests themselves.",
            "Rebuilt when you change sources; gone with your last source or your account.",
            new[] { "UserId", "FingerprintJson", "SourcesJson", "UpdatedAt" }),
        new(typeof(SourceFingerprintRecord), "Per-source signatures",
            "One signature per connected source, how many signals it contained (a number), and when it was " +
            "refreshed. Nothing about which signals.",
            "Disconnect any source on your dashboard.",
            new[] { "Id", "UserId", "Source", "RawSignatureJson", "FeatureCount", "UpdatedAt" }),
        new(typeof(UserBlock), "People you hid",
            "Which accounts you hid, and which hid you, with the date. Visible to nobody else.",
            "Unhide from your hidden list; removed with either account.",
            new[] { "Id", "BlockerId", "BlockedId", "CreatedAt" }),
        new(typeof(UserReport), "Reports",
            "If you report someone: who reported whom, a reason from a fixed list (never free text) and the " +
            "date, for the operator's review.",
            "Removed with either account.",
            new[] { "Id", "ReporterId", "ReportedId", "Reason", "CreatedAt" }),
        new(typeof(Circle), "Circles",
            "The name of each circle, given by whoever started it, and when it was started.",
            "A circle is deleted when its last member leaves.",
            new[] { "Id", "Name", "CreatedAt" }),
        new(typeof(CircleMembership), "Circle membership",
            "Which circles you are in and when you joined. Not who invited you, not the links you shared.",
            "Leave a circle from your dashboard; removed with your account.",
            new[] { "Id", "CircleId", "UserId", "JoinedAt" }),
        new(typeof(FingerprintScheme), "Hashing scheme",
            "One row for the whole server: a check value of the hashing setup and when it was set, so " +
            "signatures built under a different secret are detected. Nothing about any person.",
            "Not personal data.",
            new[] { "Id", "Verifier", "UpdatedAt" }),
    };
}
