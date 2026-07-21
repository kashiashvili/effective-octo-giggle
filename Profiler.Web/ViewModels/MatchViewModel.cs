namespace Profiler.Web.ViewModels;

public class MatchViewModel
{
    public string Username { get; set; } = "";
    public double Similarity { get; set; }
    public int SimilarityPercent => (int)Math.Round(Similarity * 100);
    public List<string> SharedSources { get; set; } = new();

    /// <summary>Optional public bio the matched user chose to share.</summary>
    public string? Bio { get; set; }

    /// <summary>Optional contact detail the matched user chose to share. Rendered as plain text.</summary>
    public string? Contact { get; set; }

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
