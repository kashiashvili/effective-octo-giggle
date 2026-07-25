using System.Net;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Profiler.Web.Controllers;
using Profiler.Web.Data;
using Xunit;

namespace Profiler.Web.Tests.Integration;

/// <summary>
/// Self-described interests exist so a normal, non-developer user — who cannot mint the OAuth tokens
/// the connectors demand — can still produce a real interest fingerprint and be matched. These prove
/// the funnel actually works end to end and that the picks are discarded like any other raw data.
/// </summary>
public class SelfDescribedInterestsTests : IClassFixture<ProfilerWebFactory>
{
    private readonly ProfilerWebFactory _factory;

    public SelfDescribedInterestsTests(ProfilerWebFactory factory) => _factory = factory;

    private HttpClient NewClient() => _factory.CreateClient(new WebApplicationFactoryClientOptions
    {
        AllowAutoRedirect = false
    });

    private static async Task<string> TokenAsync(HttpResponseMessage resp)
    {
        var m = Regex.Match(await resp.Content.ReadAsStringAsync(),
            "name=\"__RequestVerificationToken\"[^>]*value=\"([^\"]+)\"");
        Assert.True(m.Success, "Antiforgery token not found");
        return m.Groups[1].Value;
    }

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

    private static async Task<HttpResponseMessage> PickAsync(HttpClient client, params string[] features)
    {
        var page = await client.GetAsync("/sources/interests");
        page.EnsureSuccessStatusCode();
        var fields = new List<KeyValuePair<string, string>>
        {
            new("__RequestVerificationToken", await TokenAsync(page))
        };
        foreach (var f in features) fields.Add(new("features", f));
        return await client.PostAsync("/sources/interests", new FormUrlEncodedContent(fields));
    }

    [Fact]
    public async Task PickingInterests_BuildsAFingerprint_AndStoresNoTags()
    {
        var client = NewClient();
        var user = "sdi_" + Guid.NewGuid().ToString("N")[..8];
        await RegisterAsync(client, user);

        var picks = new[] { "self-tech:rust", "self-music:jazz", "self-outdoors:climbing", "self-science:astronomy" };
        var resp = await PickAsync(client, picks);
        Assert.Equal(HttpStatusCode.Redirect, resp.StatusCode);

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var me = await db.Users.AsNoTracking().FirstAsync(u => u.Username == user);

        // A matching fingerprint now exists — the whole point: a user with zero connectable accounts
        // can be matched.
        Assert.True(await db.Fingerprints.AsNoTracking().AnyAsync(f => f.UserId == me.Id));
        var source = await db.SourceFingerprints.AsNoTracking()
            .FirstAsync(s => s.UserId == me.Id && s.Source == SourcesController.SelfDescribedSource);
        Assert.Equal(picks.Length, source.FeatureCount); // count is kept…

        // …but the picks themselves are discarded: no tag slug or feature string survives anywhere.
        var json = new System.Text.Json.JsonSerializerOptions
        {
            ReferenceHandler = System.Text.Json.Serialization.ReferenceHandler.IgnoreCycles
        };
        var everything = string.Join("\n", new[]
        {
            System.Text.Json.JsonSerializer.Serialize(await db.Users.AsNoTracking().ToListAsync(), json),
            System.Text.Json.JsonSerializer.Serialize(await db.Fingerprints.AsNoTracking().ToListAsync(), json),
            System.Text.Json.JsonSerializer.Serialize(await db.SourceFingerprints.AsNoTracking().ToListAsync(), json),
        });
        foreach (var f in picks)
        {
            Assert.DoesNotContain(f, everything);                  // no "self-tech:rust"
            Assert.DoesNotContain(f.Split(':')[1], everything);    // no bare "rust"/"jazz"/…
        }
    }

    [Fact]
    public async Task TwoUsers_WhoPickTheSameInterests_MatchEachOther()
    {
        var shared = new[]
        {
            "self-tech:rust", "self-tech:machine-learning", "self-music:jazz",
            "self-reading:sci-fi", "self-science:astronomy", "self-outdoors:climbing"
        };

        var aName = "sdi_a_" + Guid.NewGuid().ToString("N")[..6];
        var bName = "sdi_b_" + Guid.NewGuid().ToString("N")[..6];

        var a = NewClient();
        await RegisterAsync(a, aName);
        await PickAsync(a, shared);

        var b = NewClient();
        await RegisterAsync(b, bName);
        await PickAsync(b, shared);

        // A sees B in their matches — the fingerprint built purely from self-described interests is a
        // real, comparable signature.
        var aMatches = await (await a.GetAsync("/matches")).Content.ReadAsStringAsync();
        Assert.Contains(bName, aMatches);
        // Identical picks are a strong overlap, so it should not read as the bottom "Some overlap" tier.
        Assert.Contains("Self-described", aMatches); // the shared source is surfaced on the card
    }

    [Fact]
    public async Task EmptyOrCraftedSelection_BuildsNothing()
    {
        var client = NewClient();
        var user = "sdi_empty_" + Guid.NewGuid().ToString("N")[..8];
        await RegisterAsync(client, user);

        // Only invalid/crafted features → treated as no selection, form re-rendered, nothing saved.
        var resp = await PickAsync(client, "language:python", "../secret", "self-tech:not-real");
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var me = await db.Users.AsNoTracking().FirstAsync(u => u.Username == user);
        Assert.False(await db.Fingerprints.AsNoTracking().AnyAsync(f => f.UserId == me.Id));
        Assert.False(await db.SourceFingerprints.AsNoTracking().AnyAsync(s => s.UserId == me.Id));
    }

    [Fact]
    public async Task Repicking_ReplacesTheSet_AndAppearsInTheDataExport()
    {
        var client = NewClient();
        var user = "sdi_re_" + Guid.NewGuid().ToString("N")[..8];
        await RegisterAsync(client, user);

        await PickAsync(client, "self-tech:rust", "self-tech:go", "self-music:jazz");
        await PickAsync(client, "self-food:coffee"); // replace with a single, different pick

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var me = await db.Users.AsNoTracking().FirstAsync(u => u.Username == user);
        var sources = await db.SourceFingerprints.AsNoTracking()
            .Where(s => s.UserId == me.Id && s.Source == SourcesController.SelfDescribedSource)
            .ToListAsync();
        Assert.Single(sources);              // one self-described row, not two
        Assert.Equal(1, sources[0].FeatureCount); // replaced, not merged (would be 4)

        // Free export parity: the self-described source shows up in the data page like any source.
        var data = await (await client.GetAsync("/account/data")).Content.ReadAsStringAsync();
        Assert.Contains("Self-described", data);
    }
}
