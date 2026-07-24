namespace Profiler.Web.ViewModels;

public class MatchViewModel
{
    public string Username { get; set; } = "";
    public double Similarity { get; set; }
    public int SimilarityPercent => (int)Math.Round(Similarity * 100);
    public List<string> SharedSources { get; set; } = new();

    /// <summary>
    /// Per-source-type overlap, strongest first. A single number says how much two people overlap but
    /// never what kind, which is the main reason a match never turns into a message. The per-source
    /// signatures needed for this were already being stored and never compared.
    /// </summary>
    public List<SharedSourceOverlap> SharedSourceOverlaps { get; set; } = new();

    /// <summary>The source you overlap on most, when it stands out enough to be worth saying.</summary>
    public SharedSourceOverlap? StrongestOverlap =>
        SharedSourceOverlaps.FirstOrDefault(o => o.SimilarityPercent >= 10);

    /// <summary>Optional public bio the matched user chose to share.</summary>
    public string? Bio { get; set; }

    /// <summary>Optional contact detail the matched user chose to share. Rendered as plain text.</summary>
    public string? Contact { get; set; }

    /// <summary>Human label for the matched user's connection intent, or null if unspecified/hidden.</summary>
    public string? ConnectionIntentLabel { get; set; }

    /// <summary>True when the match is here for the same thing as the viewer — a mutual signal, not just a display.</summary>
    public bool SharesViewerIntent { get; set; }

    /// <summary>Coarse values alignment with the viewer ("Similar outlook"), or null if either side has none/hidden.</summary>
    public string? ValuesAlignmentLabel { get; set; }

    /// <summary>Numeric closeness for the "similar outlook first" sort — smaller is closer; large when absent.</summary>
    public int ValuesAlignmentRank { get; set; } = int.MaxValue;

    /// <summary>
    /// When the matched user's fingerprint was last rebuilt. Tokens are never stored, so refreshing
    /// is manual and an abandoned profile never decays: without this, a year-old snapshot is
    /// presented as a "Strong match" with nothing to say so.
    /// </summary>
    public DateTime UpdatedAt { get; set; }

    public bool IsStale => (DateTime.UtcNow - UpdatedAt).TotalDays > SourceStatusViewModel.StaleAfterDays;

    /// <summary>Coarse age wording — a match's exact activity time is not anyone else's business.</summary>
    public string FreshnessLabel
    {
        get
        {
            var days = (DateTime.UtcNow - UpdatedAt).TotalDays;
            if (days < 1) return "updated today";
            if (days < 30) return "updated this month";
            var months = (int)Math.Round(days / 30);
            return months < 12
                ? $"updated about {months} month{(months == 1 ? "" : "s")} ago"
                : "updated over a year ago";
        }
    }

    /// <summary>CSS/severity bucket used for colour cues.</summary>
    public string Tier => SimilarityPercent >= 60 ? "high"
        : SimilarityPercent >= 30 ? "medium"
        : "low";

    /// <summary>
    /// Human label for the tier. MinHash gives an estimate, not an exact score, so we lead with a
    /// qualitative label and treat the percentage as approximate rather than implying false precision.
    /// </summary>
    public string TierLabel => SimilarityPercent >= 60 ? "Strong match"
        : SimilarityPercent >= 30 ? "Good match"
        : "Some overlap";
}

/// <summary>How much two people overlap within one source type, e.g. Spotify against Spotify.</summary>
public class SharedSourceOverlap
{
    public string Source { get; set; } = "";
    public double Similarity { get; set; }
    public int SimilarityPercent => (int)Math.Round(Similarity * 100);
}
