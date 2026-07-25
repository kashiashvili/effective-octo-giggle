namespace Profiler.Web.Profile;

/// <summary>
/// The closed set of reasons a user can report another. A fixed list rather than free text: a report
/// is operator-facing moderation data, and free text about another person invites abuse, defamation,
/// and personal data we would then have to store. Keys are stable; labels are shown in the UI.
/// </summary>
public static class ReportReason
{
    public static readonly IReadOnlyList<(string Key, string Label)> All = new List<(string, string)>
    {
        ("harassment", "Harassment or abuse"),
        ("spam", "Spam or phishing"),
        ("impersonation", "Impersonation or a fake profile"),
        ("inappropriate", "Inappropriate bio or contact details"),
        ("other", "Something else"),
    };

    private static readonly HashSet<string> Keys = All.Select(x => x.Key).ToHashSet();

    public static bool IsValid(string? key) => key != null && Keys.Contains(key);

    public static string? LabelFor(string? key) =>
        key != null && All.FirstOrDefault(x => x.Key == key) is { Key: not null } hit ? hit.Label : null;
}
