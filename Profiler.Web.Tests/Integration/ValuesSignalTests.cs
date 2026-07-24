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
/// The values signal is the most sensitive thing the product asks for, so its privacy promise —
/// answers are used once and discarded, only a coarse bucket is kept — has to hold against the
/// database, not just by construction. These exercise the real opt-in flow and the match display.
/// </summary>
public class ValuesSignalTests : IClassFixture<ProfilerWebFactory>
{
    private readonly ProfilerWebFactory _factory;

    public ValuesSignalTests(ProfilerWebFactory factory) => _factory = factory;

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

    private static async Task<HttpResponseMessage> SubmitValuesAsync(
        HttpClient client, int novelty, int routine, int ideas, int tradition, bool consent = true)
    {
        var page = await client.GetAsync("/account/values");
        page.EnsureSuccessStatusCode();
        var fields = new Dictionary<string, string>
        {
            ["Answers[novelty]"] = novelty.ToString(),
            ["Answers[routine]"] = routine.ToString(),
            ["Answers[ideas]"] = ideas.ToString(),
            ["Answers[tradition]"] = tradition.ToString(),
            ["__RequestVerificationToken"] = await TokenAsync(page)
        };
        if (consent) fields["Consent"] = "true";
        return await client.PostAsync("/account/values", new FormUrlEncodedContent(fields));
    }

    [Fact]
    public async Task TakingTheQuestionnaire_StoresOnlyTheBucket_NotTheAnswers()
    {
        var client = NewClient();
        var user = "val_" + Guid.NewGuid().ToString("N")[..8];
        await RegisterAsync(client, user);

        // A distinctive, unambiguous open response.
        var resp = await SubmitValuesAsync(client, novelty: 5, routine: 1, ideas: 5, tradition: 1);
        Assert.Equal(HttpStatusCode.Redirect, resp.StatusCode);

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var stored = await db.Users.AsNoTracking().FirstAsync(u => u.Username == user);

        // The derived bucket is kept…
        Assert.Equal(2, stored.ValuesOpenness);
        Assert.Equal(ValuesQuestionnaire.Version, stored.ValuesScheme);

        // …and the raw answers appear nowhere in the database, in any column of any table.
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
        // No per-item answer key survives — only the bucket does.
        foreach (var key in new[] { "novelty", "routine", "ideas", "tradition" })
            Assert.DoesNotContain(key, everything);
    }

    [Fact]
    public async Task WithoutConsent_NothingIsSaved()
    {
        var client = NewClient();
        var user = "valnc_" + Guid.NewGuid().ToString("N")[..8];
        await RegisterAsync(client, user);

        var resp = await SubmitValuesAsync(client, 5, 1, 5, 1, consent: false);
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode); // form re-rendered

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        Assert.Null((await db.Users.AsNoTracking().FirstAsync(u => u.Username == user)).ValuesOpenness);
    }

    [Fact]
    public async Task Removing_ClearsTheBucket()
    {
        var client = NewClient();
        var user = "valdel_" + Guid.NewGuid().ToString("N")[..8];
        await RegisterAsync(client, user);
        await SubmitValuesAsync(client, 5, 1, 5, 1);

        var page = await client.GetAsync("/account/values");
        var token = await TokenAsync(page);
        var resp = await client.PostAsync("/account/values/delete", new FormUrlEncodedContent(
            new Dictionary<string, string> { ["__RequestVerificationToken"] = token }));
        Assert.Equal(HttpStatusCode.Redirect, resp.StatusCode);

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var stored = await db.Users.AsNoTracking().FirstAsync(u => u.Username == user);
        Assert.Null(stored.ValuesOpenness);
        Assert.Null(stored.ValuesScheme);
    }

    [Fact]
    public async Task MatchCard_ShowsCoarseAlignment_WhenBothTookIt()
    {
        var meName = "val_me_" + Guid.NewGuid().ToString("N")[..6];
        var closeName = "val_close_" + Guid.NewGuid().ToString("N")[..6];
        var farName = "val_far_" + Guid.NewGuid().ToString("N")[..6];
        var fpJson = new FingerprintGenerator(128).Generate(new[] { "v:1", "v:2", "v:3" }).ToJson();

        var client = NewClient();
        await RegisterAsync(client, meName);
        await SubmitValuesAsync(client, novelty: 5, routine: 1, ideas: 5, tradition: 1); // me: +2

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var me = await db.Users.FirstAsync(u => u.Username == meName);
            var close = new AppUser { Username = closeName, PasswordHash = "x", IsDiscoverable = true, ValuesOpenness = 2, ValuesScheme = ValuesQuestionnaire.Version };
            var far = new AppUser { Username = farName, PasswordHash = "x", IsDiscoverable = true, ValuesOpenness = -2, ValuesScheme = ValuesQuestionnaire.Version };
            db.Users.AddRange(close, far);
            await db.SaveChangesAsync();
            foreach (var uid in new[] { me.Id, close.Id, far.Id })
                db.Fingerprints.Add(new FingerprintRecord { UserId = uid, FingerprintJson = fpJson, SourcesJson = "[\"GitHub\"]" });
            await db.SaveChangesAsync();
        }

        var html = await (await client.GetAsync("/matches")).Content.ReadAsStringAsync();
        Assert.Contains("Similar outlook", html);   // vs the +2 user
        Assert.Contains("Different outlook", html);  // vs the -2 user
    }
}
