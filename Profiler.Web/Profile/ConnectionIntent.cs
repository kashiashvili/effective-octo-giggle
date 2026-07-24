namespace Profiler.Web.Profile;

/// <summary>
/// The first non-interest compatibility signal: an optional, user-controlled statement of what kind
/// of connection someone is here for. Sharing interests is not the same as being worth reaching out
/// to, and this closes that gap without a licensed instrument or any sensitive inference.
///
/// A small closed set stored as a short stable key, never free text, so it carries no more
/// sensitivity than a checkbox. Kept separate and explainable — it is displayed, never folded into a
/// blended "compatibility score".
/// </summary>
public static class ConnectionIntent
{
    /// <summary>Stable storage key → human label. Order is the display order in the selector.</summary>
    private static readonly (string Key, string Label)[] Options =
    {
        ("friends", "Friends who share my interests"),
        ("collaborators", "Collaborators on projects"),
        ("discussion", "People to talk things over with"),
        ("open", "Open to whatever fits"),
    };

    public static IReadOnlyList<(string Key, string Label)> All => Options;

    /// <summary>True for null/blank (unspecified) or a recognised key. Rejects anything invented.</summary>
    public static bool IsValid(string? key) =>
        string.IsNullOrWhiteSpace(key) || Options.Any(o => o.Key == key);

    /// <summary>Normalises to a stored value: a recognised key, or null for unspecified/unknown.</summary>
    public static string? Normalize(string? key) =>
        !string.IsNullOrWhiteSpace(key) && Options.Any(o => o.Key == key) ? key : null;

    /// <summary>Human label for a stored key, or null if unspecified/unknown.</summary>
    public static string? LabelFor(string? key) =>
        Options.FirstOrDefault(o => o.Key == key).Label is { Length: > 0 } label ? label : null;
}
