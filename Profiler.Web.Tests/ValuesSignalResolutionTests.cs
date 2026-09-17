using Profiler.Web.Profile;
using Xunit;
using Xunit.Abstractions;

namespace Profiler.Web.Tests;

/// <summary>
/// Assumption test for the v2 profile (docs/DESIGN_VALUES.md §7): does a four-priority, centred
/// profile actually discriminate, where the v1 single axis clumped 95% of people into three levels?
/// Synthetic people have a latent priority vector plus item noise and a personal scale bias (the
/// thing centring is meant to remove). Guards resolution against future item or quantisation changes.
/// </summary>
public class ValuesSignalResolutionTests
{
    private readonly ITestOutputHelper _out;
    public ValuesSignalResolutionTests(ITestOutputHelper output) => _out = output;

    private static double Gauss(Random rng) =>
        Math.Sqrt(-2.0 * Math.Log(1 - rng.NextDouble())) * Math.Cos(2 * Math.PI * rng.NextDouble());

    /// <summary>A latent person: four priority tendencies and two world beliefs, standard normal.</summary>
    private static double[] Latent(Random rng) => Enumerable.Range(0, 6).Select(_ => Gauss(rng)).ToArray();

    /// <summary>Answers a latent person would give: latent signal + item noise + a personal scale bias.</summary>
    private static Dictionary<string, int> Answer(double[] latent, Random rng, double noise = 0.8)
    {
        var bias = 4.5 + 1.0 * Gauss(rng); // some people rate everything high, some low
        var a = new Dictionary<string, int>();
        foreach (var item in ValuesQuestionnaire.ValueItems)
        {
            var signal = item.Openness * latent[0] + item.Conservation * latent[1] + item.Transcendence * latent[2] + item.Enhancement * latent[3];
            a[item.Key] = Math.Clamp((int)Math.Round(bias + 1.2 * signal + noise * Gauss(rng)), 1, 7);
        }
        foreach (var item in ValuesQuestionnaire.WorldItems)
        {
            var signal = (item.Dimension == ValuesQuestionnaire.Dimension.Safe ? latent[4] : latent[5]) * (item.Reverse ? -1 : 1);
            a[item.Key] = Math.Clamp((int)Math.Round(4 + 1.5 * signal + noise * Gauss(rng)), 1, 7);
        }
        return a;
    }

    [Fact]
    public void EachPriority_UsesAllFiveLevels_AndDoesNotClump()
    {
        var rng = new Random(2026);
        const int n = 20_000;
        var counts = new int[6, 5];
        for (var i = 0; i < n; i++)
        {
            var p = ValuesQuestionnaire.Derive(Answer(Latent(rng), rng))!;
            var levels = p.Priorities.Concat(p.World).ToArray();
            for (var d = 0; d < 6; d++) counts[d, levels[d] + 2]++;
        }
        string[] names = { "openness", "conservation", "transcendence", "enhancement", "safe", "enticing" };
        for (var d = 0; d < 6; d++)
        {
            var pct = Enumerable.Range(0, 5).Select(l => 100.0 * counts[d, l] / n).ToArray();
            _out.WriteLine($"{names[d],-13} " + string.Join(" ", pct.Select(x => $"{x,5:F1}%")));
            // Not a central-tendency machine. Observed 2026-09-17: centre 30.2–39.6%, each extreme
            // 5.3–10.8%. Pinned near those figures, because PROJECT_STATE and PRODUCT_LOG quote them:
            // halving the resolution must fail here rather than pass quietly.
            Assert.InRange(pct[2], 25, 45);
            Assert.All(Enumerable.Range(0, 5), l => Assert.True(pct[l] > 2, $"{names[d]}: level {l - 2} only {pct[l]:F1}% — all five must be reachable"));
            Assert.InRange(pct[0] + pct[4], 8, 25);
        }
    }

    [Fact]
    public void RandomPairs_SpreadAcrossAllThreeTiers_AndSharedLatentPairs_ReadCloser()
    {
        var rng = new Random(99);
        const int pairs = 20_000;
        var randomTiers = new int[3];
        var twinTiers = new int[3];
        for (var i = 0; i < pairs; i++)
        {
            var a = ValuesQuestionnaire.Derive(Answer(Latent(rng), rng))!;
            var b = ValuesQuestionnaire.Derive(Answer(Latent(rng), rng))!;
            randomTiers[ValuesQuestionnaire.Tier(ValuesQuestionnaire.PriorityDistance(a, b))]++;

            // Two people with the same latent priorities, answering independently with noise.
            var latent = Latent(rng);
            var c = ValuesQuestionnaire.Derive(Answer(latent, rng))!;
            var d = ValuesQuestionnaire.Derive(Answer(latent, rng))!;
            twinTiers[ValuesQuestionnaire.Tier(ValuesQuestionnaire.PriorityDistance(c, d))]++;
        }
        double Pct(int[] t, int i) => 100.0 * t[i] / pairs;
        _out.WriteLine($"random pairs: similar {Pct(randomTiers, 0):F1}%  overlap {Pct(randomTiers, 1):F1}%  different {Pct(randomTiers, 2):F1}%");
        _out.WriteLine($"same-latent pairs: similar {Pct(twinTiers, 0):F1}%  overlap {Pct(twinTiers, 1):F1}%  different {Pct(twinTiers, 2):F1}%");

        // Figures the docs quote (observed 2026-09-17): strangers 16.7% similar / 29.7% different,
        // shared-priority pairs 69.0% similar / 0.4% different. Pinned, not merely bounded.
        Assert.InRange(Pct(randomTiers, 0), 12, 22);
        Assert.InRange(Pct(randomTiers, 2), 24, 36);
        Assert.InRange(Pct(twinTiers, 0), 62, 76);
        Assert.InRange(Pct(twinTiers, 2), 0, 3);
        Assert.True(Pct(twinTiers, 0) > 3 * Pct(randomTiers, 0), "shared priorities must read similar far more often than random pairs");
    }

    [Fact]
    public void Centring_RemovesScaleUse_SoGenerousAndStingyRatersWithTheSamePrioritiesMatch()
    {
        var rng = new Random(5);
        var agreements = 0;
        const int n = 5_000;
        for (var i = 0; i < n; i++)
        {
            var latent = Latent(rng);
            var generous = Answer(latent, rng, noise: 0.3);
            var stingy = Answer(latent, rng, noise: 0.3);
            // Push one person's whole scale up and the other's down, priorities untouched.
            foreach (var k in ValuesQuestionnaire.ValueItems.Select(x => x.Key))
            {
                generous[k] = Math.Min(7, generous[k] + 2);
                stingy[k] = Math.Max(1, stingy[k] - 2);
            }
            var a = ValuesQuestionnaire.Derive(generous)!;
            var b = ValuesQuestionnaire.Derive(stingy)!;
            if (ValuesQuestionnaire.Tier(ValuesQuestionnaire.PriorityDistance(a, b)) == 0) agreements++;
        }
        var pct = 100.0 * agreements / n;
        _out.WriteLine($"generous vs stingy rater, same priorities: read similar {pct:F1}%");
        // Observed 76.1%; the docs quote it, so pin it rather than bounding it loosely.
        Assert.InRange(pct, 70, 85);
    }
}
