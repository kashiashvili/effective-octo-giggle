using Profiler.Web.Profile;
using Xunit;
using Xunit.Abstractions;

namespace Profiler.Web.Tests;

/// <summary>
/// Assumption test (not a feature guard): does one coarse openness axis actually discriminate, or
/// does averaging four items pull almost everyone to the centre so that most pairs read "similar
/// outlook" and the signal says little? The answer decides whether the deferred second Schwartz axis
/// is worth its added sensitivity. These characterise the mechanism on synthetic profiles — no real
/// users needed — and fail loudly if a future change to the items or bucketing shifts the resolution.
/// </summary>
public class ValuesSignalResolutionTests
{
    private readonly ITestOutputHelper _out;
    public ValuesSignalResolutionTests(ITestOutputHelper output) => _out = output;

    private static int[] DeriveMany(int seed, int n)
    {
        var rng = new Random(seed);
        var buckets = new int[5]; // index 0..4 => bucket -2..+2
        for (var i = 0; i < n; i++)
        {
            var answers = new Dictionary<string, int>();
            foreach (var item in ValuesQuestionnaire.Items)
                answers[item.Key] = rng.Next(ValuesQuestionnaire.MinAnswer, ValuesQuestionnaire.MaxAnswer + 1);
            var b = ValuesQuestionnaire.DeriveBucket(answers)!.Value;
            buckets[b + 2]++;
        }
        return buckets;
    }

    [Fact]
    public void UniformRandomAnswers_ClumpTowardTheCentre_SoOneAxisDiscriminatesWeakly()
    {
        const int n = 200_000;
        var buckets = DeriveMany(seed: 12345, n: n);
        var pct = buckets.Select(c => 100.0 * c / n).ToArray();

        _out.WriteLine($"Bucket distribution from uniform-random answers (n={n}):");
        for (var b = -2; b <= 2; b++)
            _out.WriteLine($"  {b,2}: {pct[b + 2]:F1}%");

        // The middle three buckets (−1..+1) dominate: averaging four items is a central-tendency
        // machine, so the extremes are rare and most random pairs will read "similar/some overlap".
        var middle = pct[1] + pct[2] + pct[3];
        Assert.True(middle > 85, $"expected central clumping (>85% in -1..+1), got {middle:F1}%");
        Assert.True(pct[0] + pct[4] < 15, "the ±2 extremes should be rare");

        // Recorded finding: a single 5-point-averaged axis has limited resolution. This is evidence
        // that meaningful values *differentiation* needs either more axes (the deferred second
        // Schwartz axis) or finer/less-averaged scoring — not that the current signal is wrong, but
        // that it should not be over-relied on until broadened. Decision belongs to the owner/PO.
    }

    [Fact]
    public void PairwiseAlignment_IsMostlySimilar_UnderRandomProfiles()
    {
        var rng = new Random(999);
        int similar = 0, some = 0, different = 0;
        const int pairs = 200_000;

        int RandomBucket()
        {
            var answers = new Dictionary<string, int>();
            foreach (var item in ValuesQuestionnaire.Items)
                answers[item.Key] = rng.Next(ValuesQuestionnaire.MinAnswer, ValuesQuestionnaire.MaxAnswer + 1);
            return ValuesQuestionnaire.DeriveBucket(answers)!.Value;
        }

        for (var i = 0; i < pairs; i++)
        {
            switch (ValuesQuestionnaire.AlignmentLabel(RandomBucket(), RandomBucket()))
            {
                case "Similar outlook": similar++; break;
                case "Some overlap in outlook": some++; break;
                default: different++; break;
            }
        }

        _out.WriteLine($"Pairwise alignment under random profiles (n={pairs}):");
        _out.WriteLine($"  Similar:   {100.0 * similar / pairs:F1}%");
        _out.WriteLine($"  Some:      {100.0 * some / pairs:F1}%");
        _out.WriteLine($"  Different: {100.0 * different / pairs:F1}%");

        // After recalibrating the thresholds (exact match = "similar"), the label discriminates:
        // no single tier dominates the way "similar" did (~78%) under the old within-one rule, so the
        // label now carries information. The residual finding stands: a single averaged axis has
        // limited spread, so "some overlap" is the common case — evidence for the second-axis decision.
        var similarPct = 100.0 * similar / pairs;
        Assert.True(similarPct < 45, $"'similar outlook' should no longer dominate; got {similarPct:F1}%");
        Assert.True(100.0 * some / pairs > 40, "one averaged axis leaves most pairs in the middle tier");
    }
}
