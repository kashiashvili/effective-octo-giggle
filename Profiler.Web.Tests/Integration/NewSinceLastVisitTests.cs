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
/// The product collects no email, so it cannot notify anyone out of band — the only way to give a
/// returning user a reason to come back is to tell them, on arrival, what changed since they last
/// looked. This checks that "since last visit" means what it says: nothing on a first visit, a count
/// when a match refreshes, and nothing again once it has been seen.
/// </summary>
public class NewSinceLastVisitTests : IClassFixture<ProfilerWebFactory>
{
    private readonly ProfilerWebFactory _factory;

    public NewSinceLastVisitTests(ProfilerWebFactory factory) => _factory = factory;

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

    private const string BannerMarker = "refreshed their interests since your last visit";

    /// <summary>
    /// Seeds "me" matched with one other user, with both timestamps controlled: my last visit was an
    /// hour ago, the match refreshed half an hour ago — i.e. after I last looked. Returns my client.
    /// </summary>
    private async Task<HttpClient> SeedMatchRefreshedSinceLastVisitAsync(string meName, string otherName)
    {
        var fpJson = new FingerprintGenerator(128).Generate(new[] { "s:1", "s:2", "s:3" }).ToJson();
        var client = NewClient();
        await RegisterAsync(client, meName);

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var me = await db.Users.FirstAsync(u => u.Username == meName);
        me.LastMatchesViewedAt = DateTime.UtcNow.AddHours(-1);
        var other = new AppUser { Username = otherName, PasswordHash = "x", IsDiscoverable = true };
        db.Users.Add(other);
        await db.SaveChangesAsync();

        db.Fingerprints.Add(new FingerprintRecord { UserId = me.Id, FingerprintJson = fpJson, SourcesJson = "[\"GitHub\"]", UpdatedAt = DateTime.UtcNow.AddHours(-2) });
        db.Fingerprints.Add(new FingerprintRecord { UserId = other.Id, FingerprintJson = fpJson, SourcesJson = "[\"GitHub\"]", UpdatedAt = DateTime.UtcNow.AddMinutes(-30) });
        await db.SaveChangesAsync();
        return client;
    }

    [Fact]
    public async Task AMatchRefreshedSinceTheLastVisit_ShowsTheBanner_ThenItClearsOnceSeen()
    {
        var meName = "since_me_" + Guid.NewGuid().ToString("N")[..6];
        var client = await SeedMatchRefreshedSinceLastVisitAsync(meName, "since_other_" + Guid.NewGuid().ToString("N")[..6]);

        // This visit: the match refreshed after my recorded last visit, so it is surfaced as new —
        // and the visit updates my marker to now.
        var first = await (await client.GetAsync("/matches")).Content.ReadAsStringAsync();
        Assert.Contains(BannerMarker, first);

        // Next visit: nothing has changed since the marker moved to now, so the banner is gone.
        var second = await (await client.GetAsync("/matches")).Content.ReadAsStringAsync();
        Assert.DoesNotContain(BannerMarker, second);
    }

    [Fact]
    public async Task TheFirstEverVisit_ShowsNoBanner_BecauseThereIsNothingToCompareTo()
    {
        var meName = "since_first_" + Guid.NewGuid().ToString("N")[..6];
        var otherName = "since_ftgt_" + Guid.NewGuid().ToString("N")[..6];
        var fpJson = new FingerprintGenerator(128).Generate(new[] { "s:1", "s:2", "s:3" }).ToJson();

        var client = NewClient();
        await RegisterAsync(client, meName);
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var me = await db.Users.FirstAsync(u => u.Username == meName);
            Assert.Null(me.LastMatchesViewedAt); // never viewed
            var other = new AppUser { Username = otherName, PasswordHash = "x", IsDiscoverable = true };
            db.Users.Add(other);
            await db.SaveChangesAsync();
            db.Fingerprints.Add(new FingerprintRecord { UserId = me.Id, FingerprintJson = fpJson, SourcesJson = "[\"GitHub\"]", UpdatedAt = DateTime.UtcNow });
            db.Fingerprints.Add(new FingerprintRecord { UserId = other.Id, FingerprintJson = fpJson, SourcesJson = "[\"GitHub\"]", UpdatedAt = DateTime.UtcNow });
            await db.SaveChangesAsync();
        }

        var page = await (await client.GetAsync("/matches")).Content.ReadAsStringAsync();
        Assert.Contains(otherName, page);            // they match
        Assert.DoesNotContain(BannerMarker, page);   // but there is no prior visit to be "new" since
    }
}
