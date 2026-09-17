namespace Profiler.Web.ViewModels;

public class MatchViewModel
{
    public string Username { get; set; } = "";
    public double Similarity { get; set; }
    public int SimilarityPercent => (int)Math.Round(Similarity * 100);

    // The similarity bar is a visual cue, not a second number. Raw MinHash Jaccard over interest sets
    // tops out well below 100% — a synthetic simulation (InterestSignalResolutionTests) found almost
    // nothing above ~0.50 even for same-niche pairs — so filling the bar to the raw percent would draw
    // a top-tier "Strong match" as a barely-third-full bar that visually contradicts its own label.
    // Map the meaningful range [0, BarFullScalePercent] onto the full width, so the fill tracks the
    // tier (Good floor ~30% full, Strong floor ~70% full, top of the range full). The honest estimate
    // stays in the "~X% shared" text beside the bar; this only rescales the visual, never the number.
    private const int BarFullScalePercent = 50;

    /// <summary>Width, 3–100, for the similarity bar — rescaled to the realistic Jaccard range.</summary>
    public int BarPercent => Math.Clamp((int)Math.Round(SimilarityPercent * 100.0 / BarFullScalePercent), 3, 100);

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

    /// <summary>Interests the matched user chose to show publicly — a concrete icebreaker. Empty when
    /// they shared none or the viewer is hidden.</summary>
    public List<string> ShowableInterests { get; set; } = new();

    /// <summary>Of those, the ones the viewer also chose to show — the shared conversation hooks, led with.</summary>
    public List<string> SharedShowableInterests { get; set; } = new();

    /// <summary>Optional public bio the matched user chose to share.</summary>
    public string? Bio { get; set; }

    /// <summary>Optional contact detail the matched user chose to share. Rendered as plain text.</summary>
    public string? Contact { get; set; }

    /// <summary>
    /// Circles both of you are in, by name. A concrete social fact shown as a chip and usable as a
    /// sort — never a filter, never part of the similarity. Empty while the viewer is hidden.
    /// </summary>
    public List<string> SharedCircles { get; set; } = new();

    /// <summary>
    /// True when the viewer is in this person's own top matches as well — reaching out would not be a
    /// message to a stranger. Computed at request time from the same fingerprints; false while the
    /// viewer is hidden (a hidden account is in nobody's list).
    /// </summary>
    public bool RanksYouToo { get; set; }

    /// <summary>Human label for the matched user's connection intent, or null if unspecified/hidden.</summary>
    public string? ConnectionIntentLabel { get; set; }

    /// <summary>True when the match is here for the same thing as the viewer — a mutual signal, not just a display.</summary>
    public bool SharesViewerIntent { get; set; }

    /// <summary>Coarse values alignment with the viewer ("Similar outlook"), or null if either side has none/hidden.</summary>
    public string? ValuesAlignmentLabel { get; set; }

    /// <summary>Why the label says what it says: the priority both put first, or the one they differ on most.</summary>
    public string? ValuesAlignmentReason { get; set; }

    /// <summary>"similar view of the world" etc., when both answered the worldview part.</summary>
    public string? WorldAlignmentLabel { get; set; }

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

    // Tier cut-offs are calibrated to how MinHash Jaccard actually behaves over interest *sets*, not
    // to a naïve 0–100 scale. Jaccard = |A∩B| / |A∪B| runs low for real people: even two users who
    // share a whole niche each bring their own extras, which dilute the union. A synthetic-profile
    // simulation (InterestSignalResolutionTests) put same-niche pairs at median ~0.21 and P90 ~0.34,
    // with essentially none above 0.50, while strangers sit at ~0. The original 30/60 cut-offs made
    // "Strong match" unreachable (0% even for same-niche pairs) and labelled genuine matches "Some
    // overlap". These thresholds place "Strong" around the top decile of same-niche overlap and
    // "Good" clearly above stranger noise, so each tier carries information.
    public const int GoodMatchPercent = 15;
    public const int StrongMatchPercent = 35;

    /// <summary>CSS/severity bucket used for colour cues.</summary>
    public string Tier => SimilarityPercent >= StrongMatchPercent ? "high"
        : SimilarityPercent >= GoodMatchPercent ? "medium"
        : "low";

    /// <summary>
    /// Human label for the tier. MinHash gives an estimate, not an exact score, so we lead with a
    /// qualitative label and treat the percentage as approximate rather than implying false precision.
    /// </summary>
    public string TierLabel => SimilarityPercent >= StrongMatchPercent ? "Strong match"
        : SimilarityPercent >= GoodMatchPercent ? "Good match"
        : "Some overlap";
}

/// <summary>How much two people overlap within one source type, e.g. Spotify against Spotify.</summary>
public class SharedSourceOverlap
{
    public string Source { get; set; } = "";
    public double Similarity { get; set; }
    public int SimilarityPercent => (int)Math.Round(Similarity * 100);
}
