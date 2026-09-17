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
/// "You're in their top matches too": the list is cut at the top 20 in each direction, so the badge
/// says whether reaching out lands on someone who would see the viewer as well. Shown only when
/// true, computed per match from their own viewpoint (their hides, not the viewer's), and never
/// while the viewer is hidden.
/// </summary>
public class MutualMatchTests : IClassFixture<ProfilerWebFactory>
{
    private readonly ProfilerWebFactory _factory;
    public MutualMatchTests(ProfilerWebFactory factory) => _factory = factory;

    private const string Badge = "You're in their top matches too";

    private HttpClient NewClient() => _factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

    private static string AntiforgeryIn(string html) =>
        Regex.Match(html, "name=\"__RequestVerificationToken\"[^>]*value=\"([^\"]+)\"").Groups[1].Value;

    private static async Task RegisterAsync(HttpClient client, string username)
    {
        var page = await client.GetAsync("/account/register");
        var resp = await client.PostAsync("/account/register", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["Username"] = username, ["Password"] = "Tr0ubad0ur-x9", ["ConfirmPassword"] = "Tr0ubad0ur-x9",
            ["__RequestVerificationToken"] = AntiforgeryIn(await page.Content.ReadAsStringAsync()),
        }));
        Assert.Equal(HttpStatusCode.Redirect, resp.StatusCode);
    }

    /// <summary>A discoverable account with a fingerprint, created directly (no client needed).</summary>
    private async Task<int> SeedUserAsync(string username, params string[] features)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var gen = scope.ServiceProvider.GetRequiredService<FingerprintGenerator>();
        var user = await db.Users.FirstOrDefaultAsync(u => u.Username == username);
        if (user == null)
        {
            user = new AppUser { Username = username, NormalizedUsername = username, PasswordHash = "x", IsDiscoverable = true };
            db.Users.Add(user);
            await db.SaveChangesAsync();
        }
        db.Fingerprints.Add(new FingerprintRecord { UserId = user.Id, FingerprintJson = gen.Generate(features).ToJson(), SourcesJson = "[\"GitHub\"]" });
        await db.SaveChangesAsync();
        return user.Id;
    }

    private static string CardOf(string html, string username)
    {
        var start = html.IndexOf(username, StringComparison.Ordinal);
        Assert.True(start >= 0, $"{username} should be on the list");
        var end = html.IndexOf("class=\"match-card\"", start, StringComparison.Ordinal);
        return end < 0 ? html[start..] : html[start..end];
    }

    [Fact]
    public async Task Badge_OnlyWhenTheViewerIsInTheirTopList_FromTheirOwnViewpoint_AndNeverWhileHidden()
    {
        var tag = Guid.NewGuid().ToString("N")[..6];
        var me = "mut_me_" + tag;
        var mutual = "mut_yes_" + tag;
        var crowded = "mut_no_" + tag;
        var client = NewClient();
        await RegisterAsync(client, me);

        // My features; "mutual" shares most of them and knows nobody else, so I am at the top of their list.
        var mine = Enumerable.Range(1, 12).Select(i => $"{tag}:m{i}").ToArray();
        await SeedUserAsync(me, mine);
        await SeedUserAsync(mutual, mine.Take(9).Concat(new[] { $"{tag}:y1", $"{tag}:y2" }).ToArray());

        // "crowded" shares a little with me but has twenty identical twins, so I fall outside their top 20.
        var crowdedFeatures = Enumerable.Range(1, 12).Select(i => $"{tag}:c{i}").Concat(mine.Take(3)).ToArray();
        await SeedUserAsync(crowded, crowdedFeatures);
        for (var i = 0; i < 20; i++)
            await SeedUserAsync($"mut_twin{i}_{tag}", crowdedFeatures);

        var html = await client.GetStringAsync("/matches");
        Assert.Contains(Badge, CardOf(html, mutual));
        Assert.DoesNotContain(Badge, CardOf(html, crowded));

        // Their viewpoint, not mine: once "mutual" hides me, I am no longer in their list.
        int myId, mutualId;
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            myId = (await db.Users.SingleAsync(u => u.Username == me)).Id;
            mutualId = (await db.Users.SingleAsync(u => u.Username == mutual)).Id;
            db.UserBlocks.Add(new UserBlock { BlockerId = mutualId, BlockedId = myId });
            await db.SaveChangesAsync();
        }
        // A hide is symmetric on the list itself: they vanish from mine entirely.
        Assert.DoesNotContain(mutual, await client.GetStringAsync("/matches"));
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            db.UserBlocks.RemoveRange(db.UserBlocks.Where(b => b.BlockerId == mutualId && b.BlockedId == myId));
            await db.SaveChangesAsync();
        }
        Assert.Contains(Badge, CardOf(await client.GetStringAsync("/matches"), mutual));

        // Hidden viewer: in nobody's list, so no badge anywhere.
        var dashboard = await client.GetStringAsync("/sources/dashboard");
        var hide = await client.PostAsync("/account/visibility", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["discoverable"] = "false", ["__RequestVerificationToken"] = AntiforgeryIn(dashboard)
        }));
        Assert.Equal(HttpStatusCode.Redirect, hide.StatusCode);
        Assert.DoesNotContain(Badge, await client.GetStringAsync("/matches"));
    }
}
