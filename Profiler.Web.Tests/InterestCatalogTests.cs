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
}
