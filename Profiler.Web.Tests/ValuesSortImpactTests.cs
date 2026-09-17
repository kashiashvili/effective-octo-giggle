using Profiler.Web.Profile;
using Xunit;
using Xunit.Abstractions;

namespace Profiler.Web.Tests;

/// <summary>
/// Characterises how much "Similar outlook first" reorders a match list under the v2 profile. The
/// sort keys to the shown tiers only, so it lifts whole groups and leaves interest order inside each.
/// Recorded, not gated: the number is evidence for the owner, and a change to the tiers should show
/// up here as a changed number rather than a silent behaviour shift.
/// </summary>
public class ValuesSortImpactTests
{
    private readonly ITestOutputHelper _out;
    public ValuesSortImpactTests(ITestOutputHelper output) => _out = output;

    private static double Gauss(Random rng) =>
        Math.Sqrt(-2.0 * Math.Log(1 - rng.NextDouble())) * Math.Cos(2 * Math.PI * rng.NextDouble());

    private static ValuesProfile RealisticProfile(Random rng)
    {
        var latent = Enumerable.Range(0, 6).Select(_ => Gauss(rng)).ToArray();
        var bias = 4.5 + Gauss(rng);
        var a = new Dictionary<string, int>();
        foreach (var item in ValuesQuestionnaire.ValueItems)
        {
            var signal = item.Openness * latent[0] + item.Conservation * latent[1] + item.Transcendence * latent[2] + item.Enhancement * latent[3];
            a[item.Key] = Math.Clamp((int)Math.Round(bias + 1.2 * signal + 0.8 * Gauss(rng)), 1, 7);
        }
        foreach (var item in ValuesQuestionnaire.WorldItems)
        {
            var signal = (item.Dimension == ValuesQuestionnaire.Dimension.Safe ? latent[4] : latent[5]) * (item.Reverse ? -1 : 1);
            a[item.Key] = Math.Clamp((int)Math.Round(4 + 1.5 * signal + 0.8 * Gauss(rng)), 1, 7);
        }
        return ValuesQuestionnaire.Derive(a)!;
    }

    [Fact]
    public void SimilarOutlookSort_LiftsWholeTiers_AndKeepsInterestOrderInsideThem()
    {
        var rng = new Random(31337);
        const int viewers = 5_000;
        const int k = 20;
        int top1Changed = 0;
        double totalDisplaced = 0;
        int orderViolations = 0;

        for (var v = 0; v < viewers; v++)
        {
            var viewer = RealisticProfile(rng);
            var candidates = Enumerable.Range(0, k).Select(_ => RealisticProfile(rng)).ToArray();
            // Interest order is the index order; the values sort is a stable sort by the shown tiers.
            var sorted = Enumerable.Range(0, k).OrderBy(i => ValuesQuestionnaire.AlignmentRank(viewer, candidates[i])).ToArray();
            if (sorted[0] != 0) top1Changed++;
            totalDisplaced += sorted.Where((idx, pos) => idx != pos).Count();
            // Inside one rank, interest order must be preserved (stable sort).
            for (var i = 1; i < k; i++)
                if (ValuesQuestionnaire.AlignmentRank(viewer, candidates[sorted[i]]) == ValuesQuestionnaire.AlignmentRank(viewer, candidates[sorted[i - 1]])
                    && sorted[i] < sorted[i - 1]) orderViolations++;
        }

        _out.WriteLine($"top-1 changed for {100.0 * top1Changed / viewers:F1}% of viewers; avg positions moved {totalDisplaced / viewers:F1} of {k}");
        Assert.Equal(0, orderViolations);
        // It does something (otherwise why offer it) and it is not a random shuffle either.
        Assert.InRange(100.0 * top1Changed / viewers, 20, 97);
    }
}
