using Profiler.Web.Profile;
using Xunit;

namespace Profiler.Web.Tests;

/// <summary>
/// The lens is the first thing a new user sees after connecting, so it has to be honest: the counts
/// must add up to what was found, the order must be stable, and features it doesn't recognise must be
/// kept (as "Other") rather than silently dropped so the totals lie.
/// </summary>
public class InterestLensTests
{
    [Fact]
    public void FoldsRelatedPrefixesIntoOneTheme()
    {
        var themes = InterestLens.Summarize(new[]
        {
            "spotify-artist:aphex-twin", "lastfm-tag:idm", "soundcloud-genre:ambient",
            "language:csharp", "starred-topic:dotnet"
        });

        Assert.Equal(3, themes.Single(t => t.Name == "Music").Count);
        Assert.Equal(2, themes.Single(t => t.Name == "Programming & tech").Count);
    }

    [Fact]
    public void OrdersByCountThenNameSoTheOrderIsStable()
    {
        var themes = InterestLens.Summarize(new[]
        {
            "steam-game:factorio",                       // Gaming: 1
            "language:rust", "starred-topic:wasm",       // Programming & tech: 2
            "netflix-genre:scifi", "shelf:read"          // Film & TV: 1, Reading: 1
        });

        Assert.Equal("Programming & tech", themes[0].Name); // highest count first
        // The three ties (count 1) follow in ordinal name order.
        Assert.Equal(new[] { "Film & TV", "Gaming", "Reading" }, themes.Skip(1).Select(t => t.Name).ToArray());
    }

    [Fact]
    public void CountsAlwaysSumToTheNumberOfRealFeatures()
    {
        var features = new[] { "language:go", "genre:fantasy", "steam-hours:100", "spotify-genre:jazz" };
        Assert.Equal(features.Length, InterestLens.Summarize(features).Sum(t => t.Count));
    }

    [Fact]
    public void UnknownPrefixesBecomeOtherInterests_NotDropped()
    {
        var themes = InterestLens.Summarize(new[] { "brand-new-source-x:thing", "another-unknown:y" });
        Assert.Equal(2, themes.Single(t => t.Name == "Other interests").Count);
    }

    [Fact]
    public void IgnoresBlankFeatures()
    {
        var themes = InterestLens.Summarize(new[] { "language:c", "", "   " });
        Assert.Equal(1, themes.Sum(t => t.Count));
    }

    [Fact]
    public void EmptyInput_ProducesNoThemes()
    {
        Assert.Empty(InterestLens.Summarize(System.Array.Empty<string>()));
    }
}
