using Profiler.Web.Profile;
using Xunit;

namespace Profiler.Web.Tests;

public class FingerprintTests
{
    private readonly FingerprintGenerator _gen = new(128);

    [Fact]
    public void EmptyFeatureSet_ZeroSimilarityWithNonEmpty()
    {
        var empty = _gen.Generate(Array.Empty<string>());
        var nonEmpty = _gen.Generate(new[] { "feature:a", "feature:b" });
        Assert.Equal(0.0, empty.Similarity(nonEmpty));
    }

    [Fact]
    public void IdenticalSets_FullSimilarity()
    {
        var features = new[] { "lang:csharp", "topic:dotnet", "genre:fiction", "shelf:read" };
        var fp1 = _gen.Generate(features);
        var fp2 = _gen.Generate(features);
        Assert.Equal(1.0, fp1.Similarity(fp2), 3);
    }

    [Fact]
    public void DisjointSets_LowSimilarity()
    {
        var setA = Enumerable.Range(0, 50).Select(i => $"feature-a:{i}").ToArray();
        var setB = Enumerable.Range(50, 50).Select(i => $"feature-b:{i}").ToArray();
        var fpA = _gen.Generate(setA);
        var fpB = _gen.Generate(setB);
        Assert.True(fpA.Similarity(fpB) < 0.1, $"Expected < 0.1 but got {fpA.Similarity(fpB)}");
    }

    [Fact]
    public void PartialOverlap_SimilarityInRange()
    {
        var common = Enumerable.Range(0, 50).Select(i => $"common:{i}").ToArray();
        var setA = common.Concat(Enumerable.Range(0, 50).Select(i => $"only-a:{i}")).ToArray();
        var setB = common.Concat(Enumerable.Range(0, 50).Select(i => $"only-b:{i}")).ToArray();
        // Jaccard = 50/150 ≈ 0.333
        var fpA = _gen.Generate(setA);
        var fpB = _gen.Generate(setB);
        var sim = fpA.Similarity(fpB);
        Assert.InRange(sim, 0.2, 0.5);
    }

    [Fact]
    public void ToJson_FromJson_RoundTrip()
    {
        var features = new[] { "a", "b", "c", "d" };
        var fp = _gen.Generate(features);
        var json = fp.ToJson();
        var restored = ProfileFingerprint.FromJson(json);
        Assert.Equal(fp.Signature, restored.Signature);
    }
}
