using Profiler.Web.Profile;
using Xunit;

namespace Profiler.Web.Tests;

public class InterestCatalogTests
{
    [Fact]
    public void EveryCatalogFeature_IsValid_AndUnique()
    {
        var features = InterestCatalog.Categories
            .SelectMany(c => c.Tags.Select(t => InterestCatalog.Feature(c, t)))
            .ToList();

        Assert.NotEmpty(features);
        // No collisions: two people ticking the same interest must land on one shared feature, and no
        // two different interests may share a feature string (that would fake overlap).
        Assert.Equal(features.Count, features.Distinct().Count());
        Assert.All(features, f => Assert.True(InterestCatalog.IsValidFeature(f)));
        Assert.Equal(features.Count, InterestCatalog.ValidFeatures.Count);
    }

    [Fact]
    public void FeatureStrings_UseThePrefixValueShape_TheFingerprintExpects()
    {
        foreach (var category in InterestCatalog.Categories)
        {
            Assert.False(string.IsNullOrWhiteSpace(category.Prefix));
            foreach (var tag in category.Tags)
            {
                Assert.Equal($"{category.Prefix}:{tag.Slug}", InterestCatalog.Feature(category, tag));
                Assert.False(string.IsNullOrWhiteSpace(tag.Slug));
                Assert.False(string.IsNullOrWhiteSpace(tag.Display));
                Assert.DoesNotContain(":", tag.Slug); // the ':' is the prefix separator, not part of a slug
            }
        }
    }

    [Fact]
    public void UnknownOrCraftedFeatures_AreRejected()
    {
        Assert.False(InterestCatalog.IsValidFeature(null));
        Assert.False(InterestCatalog.IsValidFeature(""));
        Assert.False(InterestCatalog.IsValidFeature("self-tech:not-a-real-tag"));
        Assert.False(InterestCatalog.IsValidFeature("language:python")); // a connector-shaped feature, not a catalog one
        Assert.False(InterestCatalog.IsValidFeature("../etc/passwd"));
    }

    [Fact]
    public void EverySelfDescribedFeature_ThemesToARealTheme_NotTheFallback()
    {
        // The lens must recognise every self-described prefix, otherwise a user's own picks would all
        // fall into "Other interests" and the one-shot summary would be useless.
        foreach (var category in InterestCatalog.Categories)
        {
            var oneFeature = InterestCatalog.Feature(category, category.Tags[0]);
            var themes = InterestLens.Summarize(new[] { oneFeature });
            Assert.Single(themes);
            Assert.NotEqual("Other interests", themes[0].Name);
        }
    }

    [Fact]
    public void Expand_ReplicatesEachFeature_ByItsRarityWeight()
    {
        // A rare interest weighs 3, a common one 1, everything else 2; an unknown feature stays at 1.
        Assert.Equal(3, InterestCatalog.WeightOfSlug("shoegaze"));   // rare
        Assert.Equal(1, InterestCatalog.WeightOfSlug("python"));     // common
        Assert.Equal(2, InterestCatalog.WeightOfSlug("typescript")); // neutral

        var expanded = InterestCatalog.Expand(new[] { "self-music:shoegaze", "self-tech:python", "language:python" });
        // rare -> 3 copies, common -> 1, unknown (connector-shaped) -> 1
        Assert.Equal(3, expanded.Count(f => f.StartsWith("self-music:shoegaze")));
        Assert.Equal(1, expanded.Count(f => f == "self-tech:python"));
        Assert.Equal(1, expanded.Count(f => f == "language:python"));
        // The replicas are distinct strings so they hash to different MinHash slots.
        Assert.Equal(expanded.Count, expanded.Distinct().Count());
    }

    [Fact]
    public void WeightedFingerprint_RanksARareSharedInterest_AboveACommonOne()
    {
        // Two people whose profiles are identical in structure — same number of personal (neutral)
        // picks, disjoint — and who share exactly one interest. The ONLY difference between the two
        // scenarios is whether the shared interest is rare or common. Weighting must score the rare
        // overlap higher, through the real fingerprint pipeline.
        var gen = new FingerprintGenerator(128, pepper: "weight-test");
        var aPersonal = new[] { "self-tech:typescript", "self-tech:go", "self-tech:databases", "self-music:metal", "self-music:techno" };
        var bPersonal = new[] { "self-tech:security", "self-tech:devops", "self-reading:mystery-crime", "self-screen:animation", "self-gaming:strategy-games" };

        double SimSharing(string shared)
        {
            var a = gen.Generate(InterestCatalog.Expand(aPersonal.Append(shared)));
            var b = gen.Generate(InterestCatalog.Expand(bPersonal.Append(shared)));
            return a.Similarity(b);
        }

        var common = SimSharing("self-tech:python");   // weight 1
        var rare = SimSharing("self-music:shoegaze");   // weight 3
        Assert.True(rare > common,
            $"a rare shared interest should score above a common one (rare {rare:F3} vs common {common:F3})");
    }
}
