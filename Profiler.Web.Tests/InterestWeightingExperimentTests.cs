using Xunit;
using Xunit.Abstractions;

namespace Profiler.Web.Tests;

/// <summary>
/// Product experiment (not a feature guard, no production code involved): would rarity/IDF weighting
/// meaningfully improve matching, enough to justify its cost? Plain Jaccard is rarity-blind — two
/// people who share only "everyone likes Python" score the same as two who share "we both love
/// Byzantine history", even though the rare shared interest is the one that predicts a real
/// connection. Weighting would fix that, but it needs a global feature-frequency oracle (new retained
/// state, against the data-minimisation north star) and a fingerprint-scheme migration. So measure the
/// upside FIRST, on synthetic populations, before deciding whether the privacy cost is worth paying.
///
/// The experiment computes document frequencies from a synthetic population, weights features by
/// IDF = ln(N/df), and compares how well plain vs weighted Jaccard separate "shared a rare niche" from
/// "shared only common interests". If weighting barely widens the gap, the bet is not worth the
/// retained-state cost and should be dropped; if it widens it a lot, that is the evidence to build it.
/// </summary>
public class InterestWeightingExperimentTests
{
    private readonly ITestOutputHelper _out;
    public InterestWeightingExperimentTests(ITestOutputHelper output) => _out = output;

    private const int Population = 2_000;
    private const int Commons = 8;   // "everyone" interests
    private const int Niches = 40;   // rare shared interests
    private const int NicheSize = 6; // features per niche

    private static string[] Common(int i) => new[] { $"common:c{i}" };
    private static string[] Niche(int n) => Enumerable.Range(0, NicheSize).Select(t => $"niche:n{n}:t{t}").ToArray();

    /// <summary>A synthetic person: most commons (high df), one niche (low df), and personal noise.</summary>
    private static HashSet<string> Person(Random rng, int uid, int niche)
    {
        var f = new HashSet<string>();
        for (var c = 0; c < Commons; c++)
            if (rng.NextDouble() < 0.7) f.UnionWith(Common(c)); // each common held by ~70%
        f.UnionWith(Niche(niche));
        for (var k = 0; k < 8; k++) f.Add($"personal:{uid}:{k}"); // unique, dilutes the union
        return f;
    }

    private static double Jaccard(HashSet<string> a, HashSet<string> b)
    {
        var inter = a.Count(b.Contains);
        var union = a.Count + b.Count - inter;
        return union == 0 ? 0 : (double)inter / union;
    }

    // A feature the oracle has never seen has df≈1, i.e. it is maximally rare — so unknown features
    // get the maximal weight, not zero. Weighting them out (as zero) would wrongly let personal noise
    // vanish from the union and inflate similarity.
    private static double WeightedJaccard(HashSet<string> a, HashSet<string> b, Dictionary<string, double> w, double defaultWeight)
    {
        double W(string f) => w.TryGetValue(f, out var x) ? x : defaultWeight;
        double interW = a.Where(b.Contains).Sum(W);
        double unionW = a.Union(b).Sum(W);
        return unionW == 0 ? 0 : interW / unionW;
    }

    [Fact]
    public void RarityWeighting_WidensSeparationBetweenRareAndCommonOverlap()
    {
        var rng = new Random(20260725);

        // Build a population and its per-feature document frequency.
        var people = new List<HashSet<string>>(Population);
        for (var u = 0; u < Population; u++)
            people.Add(Person(rng, u, rng.Next(Niches)));

        var df = new Dictionary<string, int>();
        foreach (var p in people)
            foreach (var f in p)
                df[f] = df.GetValueOrDefault(f) + 1;
        // IDF weight: common features (high df) are cheap, rare ones (low df) are expensive.
        var w = df.ToDictionary(kv => kv.Key, kv => Math.Log((double)Population / kv.Value));

        // Construct many pairs of each type with matched, controlled structure so the ONLY difference
        // is what they share (a common interest vs a rare niche), not their set sizes.
        var commonOnly = new List<(HashSet<string> a, HashSet<string> b)>();
        var nicheShared = new List<(HashSet<string> a, HashSet<string> b)>();
        var uid = 1_000_000;
        for (var i = 0; i < 5_000; i++)
        {
            // common-only: different niches (so they share commons but no niche).
            var n1 = rng.Next(Niches);
            var n2 = (n1 + 1 + rng.Next(Niches - 1)) % Niches;
            commonOnly.Add((Person(rng, uid++, n1), Person(rng, uid++, n2)));
            // niche-shared: same niche.
            var n = rng.Next(Niches);
            nicheShared.Add((Person(rng, uid++, n), Person(rng, uid++, n)));
        }

        var maxWeight = Math.Log(Population); // an unseen (unique personal) feature is maximally rare
        double MeanJ(List<(HashSet<string> a, HashSet<string> b)> ps) => ps.Average(p => Jaccard(p.a, p.b));
        double MeanW(List<(HashSet<string> a, HashSet<string> b)> ps) => ps.Average(p => WeightedJaccard(p.a, p.b, w, maxWeight));

        var commonJ = MeanJ(commonOnly);
        var nicheJ = MeanJ(nicheShared);
        var commonW = MeanW(commonOnly);
        var nicheW = MeanW(nicheShared);

        // "Separation" = how many times higher a genuine (niche) overlap scores than a spurious
        // (common-only) overlap. Higher is better: it means the metric can tell "we share a real niche"
        // from "we both like a popular thing".
        var plainSep = nicheJ / commonJ;
        var weightedSep = nicheW / commonW;

        _out.WriteLine($"Plain Jaccard:    common-only {commonJ:F3}  niche {nicheJ:F3}  separation {plainSep:F2}x");
        _out.WriteLine($"Weighted Jaccard: common-only {commonW:F3}  niche {nicheW:F3}  separation {weightedSep:F2}x");
        _out.WriteLine($"Weighting improves separation by {weightedSep / plainSep:F2}x");

        // Verdict signal 1: weighting widens the gap substantially — a real niche overlap counts for
        // far more than a common-interest overlap. This is the core evidence the bet is worth its
        // retained-state cost.
        Assert.True(weightedSep > plainSep * 1.8,
            $"weighting did not widen separation enough (plain {plainSep:F2}x → weighted {weightedSep:F2}x)");

        // Verdict signal 2: weighting drives spurious "we both like a popular thing" overlap toward
        // zero, so it can no longer masquerade as a match, while genuine niche overlap stays high.
        Assert.True(commonW < 0.03, $"weighting failed to suppress common-only overlap ({commonW:F3})");
        Assert.True(nicheW > commonW * 8, $"weighted niche overlap not clearly above common ({nicheW:F3} vs {commonW:F3})");
    }

    [Fact]
    public void PlainJaccard_CannotTell_CommonSharedFrom_RareShared_ButWeightedCan()
    {
        // A crisp constructed case. Two pairs, identical set structure — each pair shares exactly one
        // feature and has the same personal noise — differing only in WHICH feature they share.
        var personal = Enumerable.Range(0, 10).Select(k => $"pers:{k}").ToArray();
        HashSet<string> With(string shared, int side) =>
            new(personal.Take(5).Select(p => $"{p}:{side}").Append(shared).Append($"anchor:{side}"));

        var popularA = With("common:python", 1);
        var popularB = With("common:python", 2);
        var rareA = With("niche:byzantine-history", 1);
        var rareB = With("niche:byzantine-history", 2);

        // Frequencies: python is everywhere, byzantine-history is rare.
        var w = new Dictionary<string, double>
        {
            ["common:python"] = Math.Log(2000.0 / 1500), // ~0.29, cheap
            ["niche:byzantine-history"] = Math.Log(2000.0 / 12), // ~5.1, expensive
        };

        // Plain Jaccard is identical — it is blind to which interest was shared.
        Assert.Equal(Jaccard(popularA, popularB), Jaccard(rareA, rareB), 3);

        // Weighted Jaccard ranks the rare-shared pair far above the common-shared pair. Unique
        // personal/anchor features are unseen here, so they take the maximal weight (df≈1).
        var maxWeight = Math.Log(2000.0);
        var wPopular = WeightedJaccard(popularA, popularB, w, maxWeight);
        var wRare = WeightedJaccard(rareA, rareB, w, maxWeight);
        _out.WriteLine($"Same structure, plain J identical ({Jaccard(popularA, popularB):F3}); " +
                       $"weighted: python-shared {wPopular:F3} vs byzantine-shared {wRare:F3}");
        Assert.True(wRare > wPopular * 3,
            $"weighting should rank a rare shared interest well above a common one ({wRare:F3} vs {wPopular:F3})");
    }
}
