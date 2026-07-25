using System.Text.Json;
using Profiler.Web.Security;

namespace Profiler.Web.Profile;

/// <summary>
/// The small set of interests a user chooses to show publicly on their match cards — "talk to me
/// about…". This is deliberately separate from the matching fingerprint: interests used for matching
/// are still derived and discarded, so the "raw interests are never stored" guarantee is untouched.
/// These are voluntary, opt-in public labels, the same trust model as the bio — they exist so a match
/// has a concrete reason (and an icebreaker) to reach out instead of only a similarity percentage.
/// Stored as a small JSON list of plain-text labels.
/// </summary>
public static class ShowableInterests
{
    public const int MaxCount = 8;
    public const int MaxLabelLength = 40;

    /// <summary>
    /// Parse free-text input (comma- or newline-separated) into a clean, capped, de-duplicated label
    /// list. Duplicates that only differ by case/width collapse (uniqueness key), the first spelling
    /// wins, and the count is capped. Display casing is preserved — this is what a stranger reads.
    /// </summary>
    public static List<string> Parse(string? raw)
    {
        var result = new List<string>();
        if (string.IsNullOrWhiteSpace(raw)) return result;

        var seen = new HashSet<string>();
        foreach (var part in raw.Split(new[] { '\n', '\r', ',' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            // Collapse internal whitespace so "sea  kayaking" and "sea kayaking" are one interest — both
            // for de-duplication here and so two people who type it with different spacing still match.
            var collapsed = string.Join(' ', part.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
            var label = collapsed.Length > MaxLabelLength ? collapsed[..MaxLabelLength].Trim() : collapsed;
            if (label.Length == 0) continue;
            var key = TextPolicy.NormalizeForUniqueness(label);
            if (key.Length == 0 || !seen.Add(key)) continue;
            result.Add(label);
            if (result.Count >= MaxCount) break;
        }
        return result;
    }

    public static string? Serialize(List<string> labels) =>
        labels.Count == 0 ? null : JsonSerializer.Serialize(labels);

    public static List<string> Deserialize(string? json) =>
        string.IsNullOrEmpty(json) ? new List<string>() : (JsonSerializer.Deserialize<List<string>>(json) ?? new List<string>());

    /// <summary>The labels two people both chose to show, matched by uniqueness key, keeping the other
    /// person's spelling. This is the shared hook a match card leads with.</summary>
    public static List<string> Common(IEnumerable<string> viewer, IEnumerable<string> match)
    {
        var viewerKeys = viewer.Select(TextPolicy.NormalizeForUniqueness).ToHashSet();
        return match.Where(m => viewerKeys.Contains(TextPolicy.NormalizeForUniqueness(m))).ToList();
    }
}
