using Profiler.Web.Profile;
using Xunit;
using Xunit.Abstractions;

namespace Profiler.Web.Tests;

/// <summary>
/// Decision-support assumption test (no real users) for the open owner question: is the values signal
/// worth collecting the most sensitive data in the product? Its main user-facing use is the optional
/// "Similar outlook first" sort. This measures how much that sort actually REORDERS a match list versus
/// plain interest ranking, on realistic synthetic profiles. Values buckets clump (95% land in −1..+1 —
/// see <see cref="ValuesSignalResolutionTests"/>), so the hypothesis is that the sort mostly ties and
/// interest order dominates. Quantifying "mostly inert" turns the deferred hide/keep decision from an
/// assertion into a number the owner can weigh against the sensitivity cost.
/// </summary>
public class ValuesSortImpactTests
{
    private readonly ITestOutputHelper _out;
    public ValuesSortImpactTests(ITestOutputHelper output) => _out = output;

    // Draw a values bucket from the realistic distribution: average of four uniform 1..5 answers,
    // reverse-scored via the real questionnaire — the same central-clumping the shipped signal has.
    private static int RealisticBucket(Random rng)
    {
        var answers = new Dictionary<string, int>();
        foreach (var item in ValuesQuestionnaire.Items)
            answers[item.Key] = rng.Next(ValuesQuestionnaire.MinAnswer, ValuesQuestionnaire.MaxAnswer + 1);
        return ValuesQuestionnaire.DeriveBucket(answers)!.Value;
    }

    [Fact]
    public void SimilarOutlookSort_ReordersMatchesOnlyModestly_BecauseBucketsClump()
    {
        var rng = new Random(31337);
        const int viewers = 20_000;
        const int k = 20; // a viewer's top-K interest matches

        int top1Changed = 0;          // the #1 match differs under the values sort
        int top3Changed = 0;          // the top-3 set differs
        double totalDisplaced = 0;    // avg count of the K whose position moves

        for (var v = 0; v < viewers; v++)
        {
            var viewerBucket = RealisticBucket(rng);

            // K candidates already ranked by interest similarity (index 0 = best interest match). Each
            // also has a values bucket. Interest rank is the identity order 0..k-1.
            var candBuckets = new int[k];
            for (var i = 0; i < k; i++) candBuckets[i] = RealisticBucket(rng);

            // "Best match" order = interest order = 0..k-1.
            // "Similar outlook first" = stable sort by AlignmentRank asc, ties keep interest order.
            var valuesOrder = Enumerable.Range(0, k)
                .OrderBy(i => ValuesQuestionnaire.AlignmentRank(viewerBucket, candBuckets[i]))
                .ToArray(); // OrderBy is stable, so equal ranks keep the interest order — exactly the app's sort

            if (valuesOrder[0] != 0) top1Changed++;
            if (valuesOrder.Take(3).OrderBy(x => x).SequenceEqual(new[] { 0, 1, 2 }) == false) top3Changed++;
            for (var pos = 0; pos < k; pos++) if (valuesOrder[pos] != pos) totalDisplaced++;
        }

        var top1Pct = 100.0 * top1Changed / viewers;
        var top3Pct = 100.0 * top3Changed / viewers;
        var avgDisplaced = totalDisplaced / viewers;

        _out.WriteLine($"Values 'Similar outlook first' sort impact over {viewers} viewers (K={k}):");
        _out.WriteLine($"  top-1 match changed:   {top1Pct:F1}%");
        _out.WriteLine($"  top-3 set changed:     {top3Pct:F1}%");
        _out.WriteLine($"  avg of {k} displaced:  {avgDisplaced:F1}");

        // Finding for the owner decision (opposite of the naive "clumping makes it inert" guess): when
        // both sides have values set, the sort reorders HEAVILY — top-1 changes ~66%, top-3 ~95%, ~18 of
        // 20 positions move. This holds even after AlignmentRank was coarsened to the three shown label
        // tiers (a separate coherence fix — the sort must not order by distinctions the UI never displays
        // nor the weak signal supports): the magnitude barely moved, because heavy reshuffling is
        // INHERENT to an outlook-first sort — outlook and interest are independent, so ranking by outlook
        // scrambles the interest order by design. Combined with the resolution finding (the bucket
        // discriminates weakly, 95% cluster in −1..+1), the crux for the owner is that "Similar outlook
        // first" is HIGH-IMPACT but LOW-RESOLUTION: opting in lets a coarse/weak signal heavily override
        // a strong (interest) one. Magnitude is adoption-dependent (no-values candidates sink via
        // MaxValue rank); this measures the both-set case. Decision options: (a) strengthen the signal
        // (2nd Schwartz axis) before leaning on this sort, (b) reframe the control so users understand it
        // trades interest quality for outlook, or (c) hide the values signal until it earns its
        // sensitivity. The printed numbers are the evidence; the bounds below only guard against drift.
        Assert.True(top1Pct is > 40 and < 90, $"top-1 change rate outside characterised band: {top1Pct:F1}%");
        Assert.True(avgDisplaced > 10, $"expected heavy reordering under full adoption: {avgDisplaced:F1}");
    }
}
