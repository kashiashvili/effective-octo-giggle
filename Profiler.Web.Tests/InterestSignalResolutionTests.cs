using Profiler.Web.Profile;
using Profiler.Web.ViewModels;
using Xunit;
using Xunit.Abstractions;

namespace Profiler.Web.Tests;

/// <summary>
/// Assumption test (not a feature guard) for the product's *core* signal: does MinHash interest
/// similarity actually discriminate on realistic profiles, and are the match tiers reachable? The
/// card labels a pair "Strong match", "Good match", or "Some overlap" from estimated Jaccard (see
/// <see cref="MatchViewModel.TierLabel"/>). Jaccard = |A∩B| / |A∪B| over interest *sets* runs low for
/// real people — two users who share a niche but each have their own extras dilute the union fast —
/// so the open question is whether those tiers ever fire for genuinely similar users, or whether every
/// real match collapses into the bottom tier and good matches are mis-communicated as weak.
///
/// This drove a recalibration: with the original 30%/60% cut-offs the simulation put "Strong match"
/// at 0% even for same-niche pairs and labelled most genuine matches "Some overlap". The thresholds
/// were moved to the realistic range (Good ≥15%, Strong ≥35%). These tests characterise the mechanism
/// on synthetic community-structured profiles (no real users) against the *real* <see cref="MatchViewModel"/>
/// tiers, so they fail loudly if a future change to hashing, truncation, or the cut-offs shifts the
/// resolution. The values axis got the same treatment in <see cref="ValuesSignalResolutionTests"/>.
/// </summary>
public class InterestSignalResolutionTests
{
    private readonly ITestOutputHelper _out;
    public InterestSignalResolutionTests(ITestOutputHelper output) => _out = output;

    // A fixed non-empty pepper: the scheme is peppered in production, and the resolution should not
    // depend on the secret. Any stable value characterises the same mechanism.
    private static readonly FingerprintGenerator Gen = new(numHashes: 128, pepper: "resolution-test-pepper");

    /// <summary>
    /// A few interest "communities", each a pool of correlated feature labels in the same shape the
    /// real connectors emit (language:*, topic:*, genre:*, artist:*). A member takes the whole shared
    /// core of their community plus a handful of idiosyncratic personal features, modelling "everyone
    /// into this niche shares its core, plus their own extras".
    /// </summary>
    private static readonly string[][] Communities =
    {
        new[] { "language:python", "language:rust", "topic:machine-learning", "topic:data-science",
                "topic:neural-networks", "starred-topic:llm", "topic:statistics", "language:c++" },
        new[] { "genre:indie-rock", "genre:shoegaze", "artist:radiohead", "artist:slowdive",
                "genre:dream-pop", "artist:my-bloody-valentine", "genre:post-rock" },
        new[] { "topic:climbing", "topic:bouldering", "topic:mountaineering", "topic:trail-running",
                "topic:ultralight-backpacking", "topic:via-ferrata" },
        new[] { "genre:sci-fi", "topic:hard-scifi", "author:ted-chiang", "author:liu-cixin",
                "genre:space-opera", "topic:worldbuilding", "author:ursula-le-guin" },
        new[] { "topic:home-cooking", "topic:fermentation", "topic:sourdough", "topic:bbq",
                "genre:cookbooks", "topic:knife-skills" },
    };

    private static List<string> Member(Random rng, int community, int personalCount, int uid)
    {
        // Whole shared core of the chosen community...
        var features = new List<string>(Communities[community]);
        // ...plus personal features unique to this user, so they never coincide across users and
        // realistically dilute the union (an eclectic person has more personal noise).
        for (var k = 0; k < personalCount; k++)
            features.Add($"personal:{uid}:{community}:{k}");
        return features;
    }

    private static double TrueJaccard(IEnumerable<string> a, IEnumerable<string> b)
    {
        var sa = a.ToHashSet();
        var sb = b.ToHashSet();
        var inter = sa.Count(sb.Contains);
        var union = sa.Count + sb.Count - inter;
        return union == 0 ? 0.0 : (double)inter / union;
    }

    private static double Estimate(IEnumerable<string> a, IEnumerable<string> b) =>
        Gen.Generate(a).Similarity(Gen.Generate(b));

    // Test against the *real* product tiers, so this simulation guards the actual card calibration.
    private static string Tier(double similarity) =>
        new MatchViewModel { Similarity = similarity }.TierLabel;

    [Fact]
    public void MinHashEstimate_TracksTrueJaccard_WithinSamplingError()
    {
        // 128 hashes give a Jaccard estimate with standard error ≈ 1/sqrt(128) ≈ 0.088. If the mean
        // absolute error over many varied pairs is much larger than that, truncation or hashing is
        // losing information and every tier boundary is unreliable.
        var rng = new Random(2026);
        double totalAbsErr = 0;
        const int pairs = 4_000;
        var uid = 0;
        for (var i = 0; i < pairs; i++)
        {
            var ca = rng.Next(Communities.Length);
            var cb = rng.Next(Communities.Length);
            var a = Member(rng, ca, rng.Next(0, 25), uid++);
            var b = Member(rng, cb, rng.Next(0, 25), uid++);
            totalAbsErr += Math.Abs(Estimate(a, b) - TrueJaccard(a, b));
        }
        var mae = totalAbsErr / pairs;
        _out.WriteLine($"MinHash estimate vs true Jaccard, mean abs error over {pairs} pairs: {mae:F4}");
        Assert.True(mae < 0.05, $"128-hash MinHash estimate drifted from true Jaccard (MAE {mae:F4})");
    }

    [Fact]
    public void SameNichePairs_SeparateFromStrangers_AndTheTiersAreReachable()
    {
        var rng = new Random(4242);
        const int n = 20_000;

        // Strangers: two *different* communities, so they share no niche core and only their unique
        // personal features (which never coincide). This is the honest "people not in your niche" case.
        var strangerSims = new List<double>(n);
        var uid = 0;
        for (var i = 0; i < n; i++)
        {
            var ca = rng.Next(Communities.Length);
            var cb = (ca + 1 + rng.Next(Communities.Length - 1)) % Communities.Length; // distinct
            var a = Member(rng, ca, rng.Next(3, 25), uid++);
            var b = Member(rng, cb, rng.Next(3, 25), uid++);
            strangerSims.Add(Estimate(a, b));
        }

        // Same-niche pairs: both share a community core, each with their own personal extras. This is
        // the "these two should match" case the tiers exist to surface.
        var sameSims = new List<double>(n);
        for (var i = 0; i < n; i++)
        {
            var c = rng.Next(Communities.Length);
            var a = Member(rng, c, rng.Next(3, 25), uid++);
            var b = Member(rng, c, rng.Next(3, 25), uid++);
            sameSims.Add(Estimate(a, b));
        }

        double Mean(List<double> xs) => xs.Average();
        double TierPct(List<double> xs, string label) => 100.0 * xs.Count(s => Tier(s) == label) / xs.Count;
        double Pctile(List<double> xs, double p)
        {
            var sorted = xs.OrderBy(x => x).ToList();
            return sorted[Math.Min(sorted.Count - 1, (int)(p / 100.0 * sorted.Count))];
        }
        string Line(List<double> xs) =>
            $"mean {Mean(xs):F3}  Strong {TierPct(xs, "Strong match"):F1}%  Good {TierPct(xs, "Good match"):F1}%  " +
            $"Some {TierPct(xs, "Some overlap"):F1}%  |  P50 {Pctile(xs, 50):F3} P90 {Pctile(xs, 90):F3} P99 {Pctile(xs, 99):F3}";

        _out.WriteLine($"Strangers (n={n}):  {Line(strangerSims)}");
        _out.WriteLine($"Same-niche (n={n}): {Line(sameSims)}");

        // 1. Discrimination: sharing a niche must lift similarity far above strangers. If this fails,
        //    MinHash interest similarity is not a usable compatibility proxy at all.
        Assert.True(Mean(sameSims) - Mean(strangerSims) > 0.18,
            $"same-niche pairs barely separated from strangers (Δmean {Mean(sameSims) - Mean(strangerSims):F3})");

        // 2. Strangers stay in the bottom tier — a stranger reading "Good match" would be noise dressed
        //    as signal. They share nothing, so estimated Jaccard sits at ~0.
        Assert.True(TierPct(strangerSims, "Some overlap") > 98,
            $"strangers escaped the bottom tier too often (Some {TierPct(strangerSims, "Some overlap"):F1}%)");

        // 3. The upper tiers are *reachable* under the recalibrated cut-offs: most same-niche pairs earn
        //    at least "Good", and "Strong" fires for a real (if small) slice — the top of the overlap
        //    distribution. Under the old 30/60 cut-offs Strong was 0% and Good was ~17%.
        var sameGood = TierPct(sameSims, "Good match");
        var sameStrong = TierPct(sameSims, "Strong match");
        Assert.True(sameGood + sameStrong > 55,
            $"most same-niche pairs should reach Good or better; got {sameGood + sameStrong:F1}%");
        Assert.True(sameStrong is > 2 and < 40,
            $"'Strong match' should be rare but reachable for same-niche pairs; got {sameStrong:F1}%");
    }
}
