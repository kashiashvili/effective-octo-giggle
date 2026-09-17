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
/// The values/outlook signal can be turned off at the deployment level (Signals:ValuesEnabled=false) —
/// the data-minimizing default recommended until it is strengthened (docs/OWNER_DECISIONS.md). With it
/// off, the questionnaire is not served or collected, and no values line or "Similar outlook first"
/// sort appears on matches — while stored buckets are left intact for a clean re-enable.
/// </summary>
public class ValuesSignalDisabledTests : IClassFixture<ProfilerWebFactory>
{
    private readonly ProfilerWebFactory _factory;
    public ValuesSignalDisabledTests(ProfilerWebFactory factory) => _factory = factory;

    private HttpClient Off() =>
        _factory.WithWebHostBuilder(b => b.UseSetting("Signals:ValuesEnabled", "false"))
            .CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

    private static async Task<string> TokenAsync(HttpResponseMessage resp) =>
        Regex.Match(await resp.Content.ReadAsStringAsync(),
            "name=\"__RequestVerificationToken\"[^>]*value=\"([^\"]+)\"").Groups[1].Value;

    private static async Task RegisterAsync(HttpClient client, string username)
    {
        var page = await client.GetAsync("/account/register");
        await client.PostAsync("/account/register", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["Username"] = username,
            ["Password"] = "Tr0ubad0ur-x9",
            ["ConfirmPassword"] = "Tr0ubad0ur-x9",
            ["__RequestVerificationToken"] = await TokenAsync(page)
        }));
    }

    [Fact]
    public async Task WhenDisabled_TheQuestionnaireIsNotServed()
    {
        var client = Off();
        await RegisterAsync(client, "vdis_" + Guid.NewGuid().ToString("N")[..8]);

        var resp = await client.GetAsync("/account/values");
        Assert.Equal(HttpStatusCode.Redirect, resp.StatusCode); // bounced to dashboard, not rendered
        Assert.Contains("/sources/dashboard", resp.Headers.Location?.OriginalString ?? "");
    }

    [Fact]
    public async Task WhenDisabled_NoValuesLineOrSort_EvenIfBothSidesHaveBuckets()
    {
        var meName = "vdis_me_" + Guid.NewGuid().ToString("N")[..6];
        var otherName = "vdis_ot_" + Guid.NewGuid().ToString("N")[..6];
        var fpJson = new FingerprintGenerator(128).Generate(new[] { "v:1", "v:2", "v:3" }).ToJson();

        var client = Off();
        await RegisterAsync(client, meName);

        // Seed both sides WITH values buckets — they must still be ignored while the signal is off.
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var me = await db.Users.FirstAsync(u => u.Username == meName);
            me.ValuesProfileJson = new ValuesProfile(2, -2, 1, -1, 1, 1).ToJson(); me.ValuesScheme = ValuesQuestionnaire.Version;
            var other = new AppUser
            {
                Username = otherName, PasswordHash = "x", IsDiscoverable = true,
                ValuesProfileJson = new ValuesProfile(2, -2, 1, -1, 1, 1).ToJson(), ValuesScheme = ValuesQuestionnaire.Version
            };
            db.Users.Add(other);
            await db.SaveChangesAsync();
            foreach (var uid in new[] { me.Id, other.Id })
                db.Fingerprints.Add(new FingerprintRecord { UserId = uid, FingerprintJson = fpJson, SourcesJson = "[\"GitHub\"]" });
            await db.SaveChangesAsync();
        }

        var html = await (await client.GetAsync("/matches")).Content.ReadAsStringAsync();
        Assert.Contains(otherName, html);                    // still a match on interests
        Assert.DoesNotContain("Similar priorities", html);    // no alignment line
        Assert.DoesNotContain("Similar outlook first", html); // no values sort control
        Assert.DoesNotContain("what matters to you", html);   // no nudge either

        // The stored buckets are untouched — re-enabling brings the signal straight back.
        using var verify = _factory.Services.CreateScope();
        var db2 = verify.ServiceProvider.GetRequiredService<AppDbContext>();
        Assert.NotNull((await db2.Users.AsNoTracking().FirstAsync(u => u.Username == meName)).ValuesProfileJson);
    }
}
