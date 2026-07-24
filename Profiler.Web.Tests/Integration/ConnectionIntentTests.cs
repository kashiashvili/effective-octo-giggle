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
/// Connection intent is the first non-interest compatibility signal. It has to behave like the other
/// opt-in profile fields: save when chosen, stay private when not, show to matches, and appear in the
/// data the user can export — and it must never turn into free text or a blended score.
/// </summary>
public class ConnectionIntentTests : IClassFixture<ProfilerWebFactory>
{
    private readonly ProfilerWebFactory _factory;

    public ConnectionIntentTests(ProfilerWebFactory factory) => _factory = factory;

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

    [Fact]
    public async Task Intent_SavesAndAppearsInTheExport_ThenCanBeCleared()
    {
        var client = NewClient();
        var user = "int_" + Guid.NewGuid().ToString("N")[..8];
        await RegisterAsync(client, user);

        var save = await PostFormAsync(client, "/account/profile", "/account/profile",
            new() { ["ConnectionIntent"] = "collaborators" });
        Assert.Equal(HttpStatusCode.Redirect, save.StatusCode);

        var data = await client.GetAsync("/account/data");
        Assert.Contains("Collaborators on projects", await data.Content.ReadAsStringAsync());

        var json = await (await client.GetAsync("/account/data.json")).Content.ReadAsStringAsync();
        Assert.Contains("\"ConnectionIntent\": \"collaborators\"", json);

        // Selecting "prefer not to say" clears it back to unspecified.
        await PostFormAsync(client, "/account/profile", "/account/profile", new() { ["ConnectionIntent"] = "" });
        var cleared = await (await client.GetAsync("/account/data.json")).Content.ReadAsStringAsync();
        Assert.Contains("\"ConnectionIntent\": null", cleared);
    }

    [Fact]
    public async Task AnOffListIntent_IsRejected_NotStored()
    {
        var client = NewClient();
        var user = "intbad_" + Guid.NewGuid().ToString("N")[..8];
        await RegisterAsync(client, user);

        var resp = await PostFormAsync(client, "/account/profile", "/account/profile",
            new() { ["ConnectionIntent"] = "spouse-with-yacht" });
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode); // re-rendered form, not a redirect

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var stored = await db.Users.AsNoTracking().FirstAsync(u => u.Username == user);
        Assert.Null(stored.ConnectionIntent);
    }

    [Fact]
    public async Task AMatchsIntent_ShowsOnTheCard_AsALabelNotAScore()
    {
        var meName = "int_me_" + Guid.NewGuid().ToString("N")[..6];
        var otherName = "int_ot_" + Guid.NewGuid().ToString("N")[..6];
        var fpJson = new FingerprintGenerator(128).Generate(new[] { "i:1", "i:2", "i:3" }).ToJson();

        var client = NewClient();
        await RegisterAsync(client, meName);

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var me = await db.Users.FirstAsync(u => u.Username == meName);
            var other = new AppUser
            {
                Username = otherName, PasswordHash = "x", IsDiscoverable = true, ConnectionIntent = "discussion"
            };
            db.Users.Add(other);
            await db.SaveChangesAsync();
            db.Fingerprints.Add(new FingerprintRecord { UserId = me.Id, FingerprintJson = fpJson, SourcesJson = "[\"GitHub\"]" });
            db.Fingerprints.Add(new FingerprintRecord { UserId = other.Id, FingerprintJson = fpJson, SourcesJson = "[\"GitHub\"]" });
            await db.SaveChangesAsync();
        }

        var page = await (await client.GetAsync("/matches")).Content.ReadAsStringAsync();
        Assert.Contains(otherName, page);
        Assert.Contains(ConnectionIntent.LabelFor("discussion")!, page); // the label, shown as text
        Assert.DoesNotContain("discussion\"", page);                     // not the raw key
    }

    /// <summary>
    /// Intent is only a compatibility signal when it is mutual. When the viewer and a match are here
    /// for the same thing the card says so ("both here for"); when they differ it just shows theirs.
    /// </summary>
    [Fact]
    public async Task WhenBothChoseTheSameIntent_TheCardCallsItMutual()
    {
        var meName = "mut_me_" + Guid.NewGuid().ToString("N")[..6];
        var sameName = "mut_same_" + Guid.NewGuid().ToString("N")[..6];
        var diffName = "mut_diff_" + Guid.NewGuid().ToString("N")[..6];
        var fpJson = new FingerprintGenerator(128).Generate(new[] { "q:1", "q:2", "q:3" }).ToJson();

        var client = NewClient();
        await RegisterAsync(client, meName);
        // The viewer is here for collaboration.
        await PostFormAsync(client, "/account/profile", "/account/profile", new() { ["ConnectionIntent"] = "collaborators" });

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var me = await db.Users.FirstAsync(u => u.Username == meName);
            var same = new AppUser { Username = sameName, PasswordHash = "x", IsDiscoverable = true, ConnectionIntent = "collaborators" };
            var diff = new AppUser { Username = diffName, PasswordHash = "x", IsDiscoverable = true, ConnectionIntent = "discussion" };
            db.Users.AddRange(same, diff);
            await db.SaveChangesAsync();
            foreach (var uid in new[] { me.Id, same.Id, diff.Id })
                db.Fingerprints.Add(new FingerprintRecord { UserId = uid, FingerprintJson = fpJson, SourcesJson = "[\"GitHub\"]" });
            await db.SaveChangesAsync();
        }

        var page = await (await client.GetAsync("/matches")).Content.ReadAsStringAsync();
        Assert.Contains("You're both here for: " + ConnectionIntent.LabelFor("collaborators"), page);
        // The one who chose differently is shown plainly, not as mutual.
        Assert.Contains("Here for: " + ConnectionIntent.LabelFor("discussion"), page);
    }
}
