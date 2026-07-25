namespace Profiler.Web.Profile;

/// <summary>
/// The second, optional compatibility signal: a short self-report of one values axis —
/// openness-to-change vs. conservation — grounded in Schwartz's public two-axis structure but
/// written in our own plain words (no licensed instrument). This is the derivation only: raw answers
/// go in, a single coarse bucket comes out, and the caller stores nothing but the bucket. The raw
/// answers are never persisted, exactly like interest features.
///
/// Deliberately non-clinical: it is a rough self-placement, presented to matches as coarse alignment
/// ("similar / different outlook"), never a score, a personality label, or a hard filter.
/// </summary>
public static class ValuesQuestionnaire
{
    /// <summary>Scheme version, stored beside a derived bucket so a future item change is detectable.</summary>
    public const string Version = "openness-v1";

    /// <summary>Answers run 1 (strongly disagree) to 5 (strongly agree).</summary>
    public const int MinAnswer = 1;
    public const int MaxAnswer = 5;

    /// <summary>Derived bucket range: negative = conservation-leaning, positive = openness-leaning.</summary>
    public const int MinBucket = -2;
    public const int MaxBucket = 2;

    public record Item(string Key, string Statement, bool Reverse);

    /// <summary>
    /// The openness↔conservation items. Reverse items lean toward conservation, so agreement counts
    /// negatively. Own wording; kept short and everyday.
    /// </summary>
    public static readonly IReadOnlyList<Item> Items = new[]
    {
        new Item("novelty", "I enjoy trying things I've never done before.", Reverse: false),
        new Item("routine", "I prefer familiar routines over surprises.", Reverse: true),
        new Item("ideas", "New and unconventional ideas excite me.", Reverse: false),
        new Item("tradition", "I value tradition and stability.", Reverse: true),
    };

    /// <summary>
    /// Reduces raw answers to a coarse bucket in [-2, 2], or null if the set is incomplete or any
    /// answer is out of range. Each item contributes (answer - 3), reversed for conservation items,
    /// so a fully-open response averages +2 and a fully-traditional one −2.
    /// </summary>
    public static int? DeriveBucket(IReadOnlyDictionary<string, int> answers)
    {
        if (answers is null) return null;

        var total = 0;
        foreach (var item in Items)
        {
            if (!answers.TryGetValue(item.Key, out var a)) return null;   // must answer every item
            if (a < MinAnswer || a > MaxAnswer) return null;              // reject out-of-range
            var centered = a - 3;                                        // -2..+2
            total += item.Reverse ? -centered : centered;
        }

        var average = (double)total / Items.Count;                       // -2..+2
        return Math.Clamp((int)Math.Round(average, MidpointRounding.AwayFromZero), MinBucket, MaxBucket);
    }

    /// <summary>
    /// Ordering key for the "Similar outlook first" sort — smaller is closer, <see cref="int.MaxValue"/>
    /// when either side has no signal (so those sort last). Deliberately keyed to the SAME three coarse
    /// tiers the <see cref="AlignmentLabel"/> shows (Similar / Some overlap / Different), NOT the raw
    /// 0..4 distance. Two reasons, both from the evidence: the raw distance draws distinctions finer than
    /// the UI ever displays (a user can't see why two "Some overlap" matches are ordered differently),
    /// and the underlying bucket discriminates weakly (95% of people cluster in −1..+1, so a 1-vs-2
    /// distance is mostly noise — see ValuesSortImpactTests). Ranking by the shown tier lifts genuinely
    /// similar-outlook matches to the top while letting the stronger interest signal order within each
    /// tier, instead of a coarse/noisy signal finely reshuffling a strong one.
    /// </summary>
    public static int AlignmentRank(int? a, int? b)
    {
        if (a is null || b is null) return int.MaxValue;
        return Math.Abs(a.Value - b.Value) switch
        {
            0 => 0,       // Similar outlook
            1 or 2 => 1,  // Some overlap in outlook
            _ => 2,       // Different outlook
        };
    }

    /// <summary>
    /// Coarse, wordy alignment between two buckets — never a number. Thresholds are calibrated so the
    /// label actually discriminates: averaging four items is a central-tendency machine (see
    /// ValuesSignalResolutionTests — 95% of profiles land in −1..+1), so a generous "within one"
    /// rule labelled ~78% of random pairs "similar", saying almost nothing. "Similar" now means an
    /// exact match; one step apart is "some overlap"; further is "different". Null if either side has
    /// no values signal.
    /// </summary>
    public static string? AlignmentLabel(int? a, int? b)
    {
        if (a is null || b is null) return null;
        return Math.Abs(a.Value - b.Value) switch
        {
            0 => "Similar outlook",
            1 or 2 => "Some overlap in outlook",
            _ => "Different outlook",
        };
    }
}
