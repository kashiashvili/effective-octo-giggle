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
/// The v2 values and worldview signal end to end (docs/DESIGN_VALUES.md): the questionnaire stores
/// six coarse numbers and nothing else, needs consent, can be removed, shows the person their own
/// profile, and reaches match cards as an explained coarse alignment with a sort — withheld while
/// hidden, never a filter.
/// </summary>
public class ValuesSignalTests : IClassFixture<ProfilerWebFactory>
{
    private readonly ProfilerWebFactory _factory;
    public ValuesSignalTests(ProfilerWebFactory factory) => _factory = factory;

    private HttpClient NewClient() => _factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

    private static string TokenIn(string html)
    {
        var m = Regex.Match(html, "name=\"__RequestVerificationToken\"[^>]*value=\"([^\"]+)\"");
        Assert.True(m.Success, "Antiforgery token not found");
        return m.Groups[1].Value;
    }

    private static async Task RegisterAsync(HttpClient client, string username)
    {
        var page = await client.GetAsync("/account/register");
        var resp = await client.PostAsync("/account/register", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["Username"] = username, ["Password"] = "Tr0ubad0ur-x9", ["ConfirmPassword"] = "Tr0ubad0ur-x9",
            ["__RequestVerificationToken"] = TokenIn(await page.Content.ReadAsStringAsync())
        }));
        Assert.Equal(HttpStatusCode.Redirect, resp.StatusCode);
    }

    /// <summary>Answer patterns in the questionnaire's own keys. Baseline 4, overrides on top; world items as given.</summary>
    private static Dictionary<string, string> Pattern(int safe, int enticing, params (string Key, int Value)[] overrides)
    {
        var a = new Dictionary<string, string>();
        foreach (var item in ValuesQuestionnaire.ValueItems) a[$"Answers[{item.Key}]"] = "4";
        foreach (var (k, v) in overrides) a[$"Answers[{k}]"] = v.ToString();
        a["Answers[safe-place]"] = safe.ToString(); a["Answers[trust]"] = (8 - safe).ToString();
        a["Answers[interesting]"] = enticing.ToString(); a["Answers[dull]"] = (8 - enticing).ToString();
        return a;
    }

    private static Dictionary<string, string> Caring(int safe = 6, int enticing = 6) =>
        Pattern(safe, enticing, ("loyalty", 7), ("fairness", 7), ("influence", 1), ("achievement", 2));
    private static Dictionary<string, string> Ambitious(int safe = 2, int enticing = 2) =>
        Pattern(safe, enticing, ("achievement", 7), ("influence", 7), ("loyalty", 2), ("fairness", 1));

    private static async Task<HttpResponseMessage> SubmitAsync(HttpClient client, Dictionary<string, string> answers, bool consent = true)
    {
        var page = await client.GetAsync("/account/values");
        page.EnsureSuccessStatusCode();
        var fields = new Dictionary<string, string>(answers) { ["__RequestVerificationToken"] = TokenIn(await page.Content.ReadAsStringAsync()) };
        if (consent) fields["Consent"] = "true";
        return await client.PostAsync("/account/values", new FormUrlEncodedContent(fields));
    }

    private async Task<AppUser> StoredAsync(string username)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        return await db.Users.AsNoTracking().FirstAsync(u => u.Username == username);
    }

    private async Task SeedMatchAsync(string username, string fpJson, ValuesProfile? profile)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var u = new AppUser { Username = username, PasswordHash = "x", IsDiscoverable = true, ValuesProfileJson = profile?.ToJson(), ValuesScheme = profile == null ? null : ValuesQuestionnaire.Version };
        db.Users.Add(u);
        await db.SaveChangesAsync();
        db.Fingerprints.Add(new FingerprintRecord { UserId = u.Id, FingerprintJson = fpJson, SourcesJson = "[\"GitHub\"]" });
        await db.SaveChangesAsync();
    }

    private static string CardOf(string html, string username)
    {
        var start = html.IndexOf(username, StringComparison.Ordinal);
        Assert.True(start >= 0, $"{username} should be on the list");
        var end = html.IndexOf("class=\"match-card\"", start, StringComparison.Ordinal);
        return end < 0 ? html[start..] : html[start..end];
    }

    [Fact]
    public async Task Answering_StoresSixCoarseNumbersAndTheScheme_NothingElse_AndShowsTheOwnProfile()
    {
        var user = "val_" + Guid.NewGuid().ToString("N")[..8];
        var client = NewClient();
        await RegisterAsync(client, user);
        var before = await StoredAsync(user);

        var resp = await SubmitAsync(client, Caring());
        Assert.Equal(HttpStatusCode.Redirect, resp.StatusCode);
        Assert.Contains("/account/values", resp.Headers.Location!.ToString());

        var stored = await StoredAsync(user);
        var profile = ValuesProfile.FromJson(stored.ValuesProfileJson);
        Assert.NotNull(profile);
        Assert.True(profile!.Transcendence >= 1 && profile.Enhancement <= -1, $"caring pattern → {stored.ValuesProfileJson}");
        Assert.True(profile.Safe > 0 && profile.Enticing > 0);
        Assert.Equal(ValuesQuestionnaire.Version, stored.ValuesScheme);
        // The JSON is exactly six small numbers — no answer, no item key, no free text.
        Assert.Matches(@"^\{(""[ocntse]"":-?[0-2],?){6}\}$", stored.ValuesProfileJson!);
        // Nothing else about the account changed.
        Assert.Equal(before.Bio, stored.Bio); Assert.Equal(before.Contact, stored.Contact); Assert.Equal(before.ShowableInterestsJson, stored.ShowableInterestsJson);

        // The person sees their own profile in words, with the science name beside it.
        var page = await client.GetStringAsync("/account/values");
        Assert.Contains("Your profile", page);
        Assert.Contains("caring for people and the planet", page);
        Assert.Contains("Self-transcendence", page);
        Assert.Contains("above your average", page);
        Assert.Contains("Update my profile", page);

        // Dashboard names the top priority and no longer nudges; the match list stops nudging too.
        var dashboard = await client.GetStringAsync("/sources/dashboard");
        Assert.Contains("Set — caring for people and the planet comes first for you", dashboard);
    }

    [Fact]
    public async Task WithoutConsent_NothingIsSaved()
    {
        var user = "valc_" + Guid.NewGuid().ToString("N")[..8];
        var client = NewClient();
        await RegisterAsync(client, user);
        var resp = await SubmitAsync(client, Caring(), consent: false);
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        Assert.Contains("confirm you understand", await resp.Content.ReadAsStringAsync());
        Assert.Null((await StoredAsync(user)).ValuesProfileJson);
    }

    [Fact]
    public async Task AnIncompleteSet_IsRefused()
    {
        var user = "vali_" + Guid.NewGuid().ToString("N")[..8];
        var client = NewClient();
        await RegisterAsync(client, user);
        var answers = Caring();
        answers.Remove("Answers[tradition]");
        var resp = await SubmitAsync(client, answers);
        Assert.Contains("answer every question", await resp.Content.ReadAsStringAsync());
        Assert.Null((await StoredAsync(user)).ValuesProfileJson);
    }

    [Fact]
    public async Task Removing_ClearsTheProfileAndScheme()
    {
        var user = "valr_" + Guid.NewGuid().ToString("N")[..8];
        var client = NewClient();
        await RegisterAsync(client, user);
        await SubmitAsync(client, Caring());
        var page = await client.GetAsync("/account/values");
        var del = await client.PostAsync("/account/values/delete", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = TokenIn(await page.Content.ReadAsStringAsync())
        }));
        Assert.Equal(HttpStatusCode.Redirect, del.StatusCode);
        var stored = await StoredAsync(user);
        Assert.Null(stored.ValuesProfileJson);
        Assert.Null(stored.ValuesScheme);
    }

    [Fact]
    public async Task MatchCards_ExplainTheAlignment_SortByIt_AndWithholdItWhileHidden()
    {
        var tag = Guid.NewGuid().ToString("N")[..6];
        var (me, close, far, none) = ("vm_me_" + tag, "vm_close_" + tag, "vm_far_" + tag, "vm_none_" + tag);
        var gen = new FingerprintGenerator(128);
        var mine = gen.Generate(new[] { $"{tag}:1", $"{tag}:2", $"{tag}:3", $"{tag}:4" }).ToJson();
        var stronger = mine; // the "far" person is the stronger interest match
        var weaker = gen.Generate(new[] { $"{tag}:1", $"{tag}:2", $"{tag}:x", $"{tag}:y" }).ToJson();

        var client = NewClient();
        await RegisterAsync(client, me);
        await SubmitAsync(client, Caring());
        var myProfile = ValuesProfile.FromJson((await StoredAsync(me)).ValuesProfileJson)!;
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var meRow = await db.Users.FirstAsync(u => u.Username == me);
            db.Fingerprints.Add(new FingerprintRecord { UserId = meRow.Id, FingerprintJson = mine, SourcesJson = "[\"GitHub\"]" });
            await db.SaveChangesAsync();
        }
        await SeedMatchAsync(close, weaker, myProfile);                                   // same priorities, weaker interests
        await SeedMatchAsync(far, stronger, new ValuesProfile(0, -1, -2, 2, -2, -2));     // opposite priorities, stronger interests
        await SeedMatchAsync(none, weaker, null);

        var html = await client.GetStringAsync("/matches");
        Assert.Contains("Similar priorities — you both put caring for people and the planet first · similar view of the world", CardOf(html, close));
        Assert.Contains("Different priorities — you differ most on caring for people and the planet · a different view of the world", CardOf(html, far));
        Assert.DoesNotContain("priorities", CardOf(html, none));
        Assert.DoesNotContain("Two minutes on what matters to you", html); // no nudge once set
        Assert.Contains("Similar outlook first", html);

        // Default order is interest order (far first); the values sort lifts the similar one.
        Assert.True(html.IndexOf(far, StringComparison.Ordinal) < html.IndexOf(close, StringComparison.Ordinal));
        var sorted = await client.GetStringAsync("/matches?sort=values");
        Assert.True(sorted.IndexOf(close, StringComparison.Ordinal) < sorted.IndexOf(far, StringComparison.Ordinal));
        Assert.Contains(none, sorted); // never a filter

        // Hidden viewer: withheld, like bio and contact.
        var dashboard = await client.GetStringAsync("/sources/dashboard");
        await client.PostAsync("/account/visibility", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["discoverable"] = "false", ["__RequestVerificationToken"] = TokenIn(dashboard)
        }));
        Assert.DoesNotContain("priorities —", await client.GetStringAsync("/matches"));
    }

    [Fact]
    public async Task TheMatchListAndDashboard_NudgeUntilItIsAnswered()
    {
        var tag = Guid.NewGuid().ToString("N")[..6];
        var client = NewClient();
        await RegisterAsync(client, "vn_" + tag);
        var dashboard = await client.GetStringAsync("/sources/dashboard");
        Assert.Contains("What matters to you", dashboard);
        var quick = dashboard[dashboard.IndexOf("quick-actions", StringComparison.Ordinal)..];
        Assert.True(quick.IndexOf("/account/values", StringComparison.Ordinal) < quick.IndexOf("/matches", StringComparison.Ordinal), "values comes before matches in the quick actions until answered");

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var meRow = await db.Users.FirstAsync(u => u.Username == "vn_" + tag);
            db.Fingerprints.Add(new FingerprintRecord { UserId = meRow.Id, FingerprintJson = new FingerprintGenerator(128).Generate(new[] { $"{tag}:a" }).ToJson(), SourcesJson = "[\"GitHub\"]" });
            await db.SaveChangesAsync();
        }
        await SeedMatchAsync("vn_o_" + tag, new FingerprintGenerator(128).Generate(new[] { $"{tag}:a" }).ToJson(), null);
        Assert.Contains("Two minutes on what matters to you", await client.GetStringAsync("/matches"));
    }
}
