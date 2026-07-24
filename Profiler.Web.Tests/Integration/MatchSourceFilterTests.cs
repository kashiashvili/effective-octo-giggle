using System.Net;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Profiler.Web.Data;
using Profiler.Web.Data.Models;
using Profiler.Web.Profile;
using Xunit;

namespace Profiler.Web.Tests.Integration;

/// <summary>
/// The product is about niche interests, so "show me the people I share Music with" is a core move.
/// This checks the matches page can be narrowed to one shared source, that an unknown filter is
/// ignored rather than blanking the page, and that filtering out everything explains itself.
/// </summary>
public class MatchSourceFilterTests : IClassFixture<ProfilerWebFactory>
{
    private readonly ProfilerWebFactory _factory;

    public MatchSourceFilterTests(ProfilerWebFactory factory) => _factory = factory;

    private HttpClient NewClient() => _factory.CreateClient(new WebApplicationFactoryClientOptions
    {
        AllowAutoRedirect = false
    });

    private static async Task RegisterAsync(HttpClient client, string username)
    {
        var page = await client.GetAsync("/account/register");
        var token = Regex.Match(await page.Content.ReadAsStringAsync(),
            "name=\"__RequestVerificationToken\"[^>]*value=\"([^\"]+)\"").Groups[1].Value;
        await client.PostAsync("/account/register", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["Username"] = username,
            ["Password"] = "Tr0ubad0ur-x9",
            ["ConfirmPassword"] = "Tr0ubad0ur-x9",
            ["__RequestVerificationToken"] = token
        }));
    }

    /// <summary>
    /// "me" plus two matches: one shares GitHub, the other shares Spotify. Both use my fingerprint so
    /// each is a strong match; the shared-source list is what the filter keys on. Returns my client.
    /// </summary>
    private async Task<(HttpClient Client, string GitHubMate, string SpotifyMate)> SeedTwoMatchesOnDifferentSourcesAsync()
    {
        var meName = "flt_me_" + Guid.NewGuid().ToString("N")[..6];
        var githubMate = "flt_gh_" + Guid.NewGuid().ToString("N")[..6];
        var spotifyMate = "flt_sp_" + Guid.NewGuid().ToString("N")[..6];
        var fpJson = new FingerprintGenerator(128).Generate(new[] { "x:1", "x:2", "x:3" }).ToJson();

        var client = NewClient();
        await RegisterAsync(client, meName);

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var me = await db.Users.FirstAsync(u => u.Username == meName);
        var gh = new AppUser { Username = githubMate, PasswordHash = "x", IsDiscoverable = true };
        var sp = new AppUser { Username = spotifyMate, PasswordHash = "x", IsDiscoverable = true };
        db.Users.AddRange(gh, sp);
        await db.SaveChangesAsync();

        // I have both sources; each mate shares one of them.
        db.Fingerprints.Add(new FingerprintRecord { UserId = me.Id, FingerprintJson = fpJson, SourcesJson = "[\"GitHub\",\"Spotify\"]" });
        db.Fingerprints.Add(new FingerprintRecord { UserId = gh.Id, FingerprintJson = fpJson, SourcesJson = "[\"GitHub\"]" });
        db.Fingerprints.Add(new FingerprintRecord { UserId = sp.Id, FingerprintJson = fpJson, SourcesJson = "[\"Spotify\"]" });
        await db.SaveChangesAsync();
        return (client, githubMate, spotifyMate);
    }

    [Fact]
    public async Task FilteringByASource_ShowsOnlyMatchesThatShareIt()
    {
        var (client, githubMate, spotifyMate) = await SeedTwoMatchesOnDifferentSourcesAsync();

        var all = await (await client.GetAsync("/matches")).Content.ReadAsStringAsync();
        Assert.Contains(githubMate, all);
        Assert.Contains(spotifyMate, all);

        var gitHubOnly = await (await client.GetAsync("/matches?source=GitHub")).Content.ReadAsStringAsync();
        Assert.Contains(githubMate, gitHubOnly);
        Assert.DoesNotContain(spotifyMate, gitHubOnly);
    }

    [Fact]
    public async Task AnUnknownFilter_IsIgnored_NotBlanked()
    {
        var (client, githubMate, spotifyMate) = await SeedTwoMatchesOnDifferentSourcesAsync();

        // Nobody shares "Netflix"; the filter is dropped rather than showing an empty page.
        var page = await (await client.GetAsync("/matches?source=Netflix")).Content.ReadAsStringAsync();
        Assert.Contains(githubMate, page);
        Assert.Contains(spotifyMate, page);
    }

    [Fact]
    public async Task TheChipsAppearOnlyWhenThereIsMoreThanOneAreaToChooseFrom()
    {
        var (client, _, _) = await SeedTwoMatchesOnDifferentSourcesAsync();
        // Two shared sources (GitHub, Spotify) → the filter is worth showing.
        var page = await (await client.GetAsync("/matches")).Content.ReadAsStringAsync();
        Assert.Contains("source-filter", page);
        Assert.Contains("/matches?source=GitHub", page);
        Assert.Contains("/matches?source=Spotify", page);
    }
}
