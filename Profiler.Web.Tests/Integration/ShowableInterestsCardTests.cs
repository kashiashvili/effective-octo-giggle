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
/// Showable interests are opt-in PUBLIC labels a user puts on their own match card — the concrete
/// reason (and icebreaker) a stranger has to reach out. They behave like the other opt-in profile
/// fields: save when set, appear in the export, show to matches (shared ones led with), and stay
/// hidden while the viewer is hidden. They are deliberately SEPARATE from the discarded matching
/// fingerprint.
/// </summary>
public class ShowableInterestsCardTests : IClassFixture<ProfilerWebFactory>
{
    private readonly ProfilerWebFactory _factory;

    public ShowableInterestsCardTests(ProfilerWebFactory factory) => _factory = factory;

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

    private static async Task<HttpResponseMessage> PostFormAsync(
        HttpClient client, string getUrl, string postUrl, Dictionary<string, string> fields)
    {
        var page = await client.GetAsync(getUrl);
        page.EnsureSuccessStatusCode();
        fields["__RequestVerificationToken"] = await TokenAsync(page);
        return await client.PostAsync(postUrl, new FormUrlEncodedContent(fields));
    }

    private static async Task RegisterAsync(HttpClient client, string username)
        => await PostFormAsync(client, "/account/register", "/account/register", new()
        {
            ["Username"] = username,
            ["Password"] = "Tr0ubad0ur-x9",
            ["ConfirmPassword"] = "Tr0ubad0ur-x9"
        });

    private static async Task SeedMatchAsync(ProfilerWebFactory factory, string viewer, string other,
        string? otherShowableJson)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var fpJson = new FingerprintGenerator(128).Generate(new[] { "s:1", "s:2", "s:3" }).ToJson();
        var me = await db.Users.FirstAsync(u => u.Username == viewer);
        var them = new AppUser
        {
            Username = other, PasswordHash = "x", IsDiscoverable = true, ShowableInterestsJson = otherShowableJson
        };
        db.Users.Add(them);
        await db.SaveChangesAsync();
        foreach (var uid in new[] { me.Id, them.Id })
            db.Fingerprints.Add(new FingerprintRecord { UserId = uid, FingerprintJson = fpJson, SourcesJson = "[\"GitHub\"]" });
        await db.SaveChangesAsync();
    }

    [Fact]
    public async Task ShowableInterests_SaveDeduped_AndAppearInTheExport()
    {
        var client = NewClient();
        var user = "si_" + Guid.NewGuid().ToString("N")[..8];
        await RegisterAsync(client, user);

        var save = await PostFormAsync(client, "/account/profile", "/account/profile",
            new() { ["ShowableInterests"] = "Sea Kayaking\nsea kayaking\nByzantine History" });
        Assert.Equal(HttpStatusCode.Redirect, save.StatusCode);

        var json = await (await client.GetAsync("/account/data.json")).Content.ReadAsStringAsync();
        Assert.Contains("Sea Kayaking", json);
        Assert.Contains("Byzantine History", json);
        // The case-duplicate collapsed — only one "sea kayaking" survives.
        Assert.Single(Regex.Matches(json, "(?i)sea kayaking"));

        // The human-readable transparency page must show them too — it promises "exactly what we store".
        var htmlPage = await (await client.GetAsync("/account/data")).Content.ReadAsStringAsync();
        Assert.Contains("Sea Kayaking", htmlPage);
        Assert.Contains("Shown interests", htmlPage);
    }

    [Fact]
    public async Task WhenBothShowTheSameInterest_TheCardLeadsWithIt_AsAMutualHook()
    {
        var meName = "si_me_" + Guid.NewGuid().ToString("N")[..6];
        var otherName = "si_ot_" + Guid.NewGuid().ToString("N")[..6];

        var client = NewClient();
        await RegisterAsync(client, meName);
        await PostFormAsync(client, "/account/profile", "/account/profile",
            new() { ["ShowableInterests"] = "sea kayaking\nchess" });

        await SeedMatchAsync(_factory, meName, otherName,
            ShowableInterests.Serialize(new List<string> { "Sea Kayaking", "byzantine history" }));

        var page = await (await client.GetAsync("/matches")).Content.ReadAsStringAsync();
        Assert.Contains(otherName, page);
        Assert.Contains("You both want to talk about", page);
        Assert.Contains("Sea Kayaking", page); // the match's spelling of the shared one
    }

    [Fact]
    public async Task WhenOnlyTheMatchShowsInterests_TheCardOffersThemAsAnIcebreaker()
    {
        var meName = "si_me2_" + Guid.NewGuid().ToString("N")[..6];
        var otherName = "si_ot2_" + Guid.NewGuid().ToString("N")[..6];

        var client = NewClient();
        await RegisterAsync(client, meName); // viewer shows nothing

        await SeedMatchAsync(_factory, meName, otherName,
            ShowableInterests.Serialize(new List<string> { "modular synthesis", "trail running" }));

        var page = await (await client.GetAsync("/matches")).Content.ReadAsStringAsync();
        Assert.Contains("Ask them about", page);
        Assert.Contains("modular synthesis", page);
        Assert.DoesNotContain("You both want to talk about", page); // no overlap
    }

    [Fact]
    public async Task WhileHidden_TheViewerDoesNotSeeAMatchsShowableInterests()
    {
        var meName = "si_hid_" + Guid.NewGuid().ToString("N")[..6];
        var otherName = "si_hidot_" + Guid.NewGuid().ToString("N")[..6];

        var client = NewClient();
        await RegisterAsync(client, meName);
        await SeedMatchAsync(_factory, meName, otherName,
            ShowableInterests.Serialize(new List<string> { "spelunking" }));

        // Hide the viewer: like bio/contact, a match's showable interests are then withheld.
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var me = await db.Users.FirstAsync(u => u.Username == meName);
            me.IsDiscoverable = false;
            await db.SaveChangesAsync();
        }

        var page = await (await client.GetAsync("/matches")).Content.ReadAsStringAsync();
        Assert.Contains(otherName, page);            // still a match
        Assert.DoesNotContain("spelunking", page);   // but their showable interests are withheld
    }
}
