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

    [Theory]
    [InlineData("Byzantine History!", "byzantine-history")]
    [InlineData("byzantine  history", "byzantine-history")]
    [InlineData("Byzantine-History", "byzantine-history")]
    [InlineData("  sci-fi  ", "sci-fi")]
    [InlineData("modular synthesis", "modular-synthesis")]
    public void NormalizeCustom_ResolvesCaseSpacingAndPunctuation(string raw, string expected)
    {
        // The whole point: people who mean the same thing must land on the same slug despite typing it
        // differently, or free-text interests never match.
        Assert.Equal(expected, InterestCatalog.NormalizeCustom(raw));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("a")]     // too short after normalizing
    [InlineData("!!!")]   // no alphanumerics
    [InlineData(null)]
    public void NormalizeCustom_RejectsEmptyOrTooShort(string? raw)
    {
        Assert.Null(InterestCatalog.NormalizeCustom(raw));
    }

    [Theory]
    [InlineData("C++", "cpp")]
    [InlineData("c++", "cpp")]
    [InlineData("C#", "csharp")]
    [InlineData("F#", "fsharp")]
    public void NormalizeCustom_RescuesSymbolDefinedTokens(string raw, string expected)
    {
        // Without the alias these strip to "c"/"f" and vanish, despite being common interests.
        Assert.Equal(expected, InterestCatalog.NormalizeCustom(raw));
    }

    [Fact]
    public void TypedCpp_UnifiesWithTheCatalogCppTag()
    {
        // Typing "C++" should reach the same feature as picking the C++ checkbox (which bridges to
        // GitHub's language:c++), not vanish.
        Assert.Equal(new[] { "language:c++" }, InterestCatalog.CustomFeatures(new[] { "C++" }));
    }

    [Fact]
    public void NormalizeCustom_CapsLength()
    {
        var slug = InterestCatalog.NormalizeCustom(new string('a', 100));
        Assert.NotNull(slug);
        Assert.True(slug!.Length <= 40, $"slug should be capped, was {slug.Length}");
    }

    [Fact]
    public void CustomFeatures_MapsKnownConceptsToTheCatalog_AndDedupes()
    {
        // A typed interest that matches a catalog concept unifies with picking it (and, for languages,
        // with a connector user) instead of forming a separate `interest:*` island.
        Assert.Equal(new[] { "language:python" }, InterestCatalog.CustomFeatures(new[] { "Python" }));
        Assert.Equal(new[] { "self-reading:sci-fi" }, InterestCatalog.CustomFeatures(new[] { "Sci-Fi" }));

        // Unknown interests become the shared interest: namespace.
        Assert.Equal(new[] { "interest:byzantine-history" }, InterestCatalog.CustomFeatures(new[] { "byzantine history" }));

        // Two spellings of one interest collapse to a single feature.
        Assert.Single(InterestCatalog.CustomFeatures(new[] { "Sci-Fi", "sci fi", "SCI  FI" }));
    }

    [Fact]
    public void CustomFeatures_CapsCount_AndDropsGarbage()
    {
        var many = Enumerable.Range(0, 100).Select(i => $"interest number {i}").ToList();
        many.Add("!!!"); // dropped
        var result = InterestCatalog.CustomFeatures(many);
        Assert.True(result.Count <= InterestCatalog.MaxCustomInterests);
        Assert.All(result, f => Assert.False(string.IsNullOrWhiteSpace(f)));
    }

    [Fact]
    public void Canonicalize_BridgesLanguagesToConnectorVocabulary_AndPassesOthersThrough()
    {
        var mapped = InterestCatalog.Canonicalize(new[]
        {
            "self-tech:python", "self-tech:cpp", "self-music:jazz", "self-outdoors:climbing"
        });

        // Bridged languages become the exact string GitHub emits (lowercased language:*), so a
        // self-describer and a GitHub user land on the same feature.
        Assert.Contains("language:python", mapped);
        Assert.Contains("language:c++", mapped);
        Assert.DoesNotContain("self-tech:python", mapped);
        // Non-bridged interests are untouched — no connector has a single canonical string for them yet.
        Assert.Contains("self-music:jazz", mapped);
        Assert.Contains("self-outdoors:climbing", mapped);
        // 1:1, so the count never changes (FeatureCount stays the number of picks).
        Assert.Equal(4, mapped.Count);
    }

    [Fact]
    public void BridgedLanguage_MatchesAConnectorFeature_ThroughTheRealPipeline()
    {
        // The whole point of bridging: a self-describer who picks Python must produce the same feature a
        // GitHub connector user does, so their fingerprints actually overlap.
        var gen = new FingerprintGenerator(128, pepper: "bridge-test");
        var selfDescribed = gen.Generate(InterestCatalog.Expand(InterestCatalog.Canonicalize(
            new[] { "self-tech:python", "self-tech:rust" })));
        var githubUser = gen.Generate(new[] { "language:python", "language:rust", "language:go" });

        // Without the bridge these would share nothing (self-tech:* vs language:*). With it they overlap.
        Assert.True(selfDescribed.Similarity(githubUser) > 0,
            "a bridged self-described language should match the connector's language feature");
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
