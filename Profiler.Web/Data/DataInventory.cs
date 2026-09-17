using Profiler.Web.Data.Models;

namespace Profiler.Web.Data;

/// <summary>One kind of stored data, described for the person it is about.</summary>
public record StoredKind(Type Entity, string Name, string What, string Control);

/// <summary>
/// The complete list of what the database holds, in plain words, keyed by entity type so a test can
/// hold it to the EF model: a table nobody described here fails the build. Rendered on the privacy
/// page — the promise is only worth something if it stays literally true as the product grows.
/// </summary>
public static class DataInventory
{
    public static readonly IReadOnlyList<StoredKind> Kinds = new List<StoredKind>
    {
        new(typeof(AppUser), "Your account",
            "Username, a BCrypt hash of your password, a hash of your recovery code, when you registered, " +
            "when you last opened your matches, whether you are discoverable, and — only if you chose to add " +
            "them — a bio, a contact line, a connection intent, a values bucket (−2..+2, the answers are " +
            "discarded) and the interests you chose to show. An operator suspension flag, if one was applied.",
            "Edit or clear each optional field on your dashboard and profile; delete the account to remove everything."),
        new(typeof(FingerprintRecord), "Your fingerprint",
            "The combined MinHash signature used for matching and the names of the sources it came from. " +
            "Not the interests themselves.",
            "Rebuilt when you change sources; gone with your last source or your account."),
        new(typeof(SourceFingerprintRecord), "Per-source signatures",
            "One signature per connected source, how many signals it contained (a number), and when it was " +
            "refreshed. Nothing about which signals.",
            "Disconnect any source on your dashboard."),
        new(typeof(UserBlock), "People you hid",
            "Which accounts you hid, and which hid you, with the date. Visible to nobody else.",
            "Unhide from your hidden list; removed with either account."),
        new(typeof(UserReport), "Reports",
            "If you report someone: who reported whom, a reason from a fixed list (never free text) and the " +
            "date, for the operator's review.",
            "Removed with either account."),
        new(typeof(Circle), "Circles",
            "The name of each circle, given by whoever started it.",
            "A circle is deleted when its last member leaves."),
        new(typeof(CircleMembership), "Circle membership",
            "Which circles you are in and when you joined. Not who invited you, not the links you shared.",
            "Leave a circle from your dashboard; removed with your account."),
        new(typeof(FingerprintScheme), "Hashing scheme",
            "One row for the whole server: a check value of the hashing setup, so signatures built under " +
            "a different secret are detected. Nothing about any person.",
            "Not personal data."),
    };
}
