using System.Net;
using System.Net.Http.Headers;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Profiler.Web.Controllers;
using Profiler.Web.Data;
using Profiler.Web.Data.Models;
using Xunit;

namespace Profiler.Web.Tests.Integration;

/// <summary>
/// The metrics endpoint lets the owner see whether the compatibility signals are used, without
/// exposing anything per-user. It must be off unless a secret is configured, gated by that secret,
/// and unreachable with an ordinary session — counts were deliberately hidden from users elsewhere.
/// </summary>
public class MetricsTests : IClassFixture<ProfilerWebFactory>
{
    private readonly ProfilerWebFactory _factory;

    public MetricsTests(ProfilerWebFactory factory) => _factory = factory;

    private const string Token = "metrics-secret-zzqx";

    private HttpClient WithToken() =>
        _factory.WithWebHostBuilder(b => b.UseSetting(MetricsController.TokenKey, Token))
            .CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

    [Fact]
    public async Task WhenNoTokenConfigured_TheRouteDoesNotEvenExist()
    {
        // The default factory sets no Metrics:Token — the feature is off and hidden.
        var client = _factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        var resp = await client.GetAsync("/metrics");
        Assert.Equal(HttpStatusCode.NotFound, resp.StatusCode);
    }

    [Fact]
    public async Task WithoutTheBearerToken_ItIsRefused()
    {
        var client = WithToken();
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/metrics")).StatusCode);

        var req = new HttpRequestMessage(HttpMethod.Get, "/metrics");
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", "wrong-token");
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.SendAsync(req)).StatusCode);
    }

    [Fact]
    public async Task WithTheToken_ItReturnsAggregateCounts_AndNoPerUserData()
    {
        var client = WithToken();

        // Seed a user who has set both new signals.
        string username = "metric_" + Guid.NewGuid().ToString("N")[..8];
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            db.Users.Add(new AppUser
            {
                Username = username, PasswordHash = "x",
                ConnectionIntent = "collaborators", ValuesProfileJson = new Profiler.Web.Profile.ValuesProfile(2, -1, 1, 0, 1, 1).ToJson(), ValuesScheme = "schwartz-v2"
            });
            await db.SaveChangesAsync();
        }

        var req = new HttpRequestMessage(HttpMethod.Get, "/metrics");
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", Token);
        var resp = await client.SendAsync(req);
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);

        var body = await resp.Content.ReadAsStringAsync();
        Assert.Contains("\"totalUsers\"", body, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("withConnectionIntent", body, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("withValuesProfile", body, StringComparison.OrdinalIgnoreCase);
        // The distributions are keyed by bucket/intent, not by anyone — the username must not leak.
        Assert.DoesNotContain(username, body);
        // With a tiny cohort the per-category breakdowns are withheld, so a single user's exact
        // intent/values cannot be read off the distribution.
        Assert.Contains("breakdownsWithheldBelowCohort", body, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("\"connectionIntentBreakdown\":null", body.Replace(" ", ""), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task FunnelAndReturnCounts_AreAggregatesOfStoredTimestamps_AndNameNobody()
    {
        var client = WithToken();
        var before = await ReadAsync(client);

        // Three accounts: one registered days ago who came back to the match list today (returned),
        // one who registered and looked today (viewed, not returned), one who never looked.
        var tag = Guid.NewGuid().ToString("N")[..8];
        var now = DateTime.UtcNow;
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var returned = new AppUser { Username = "ret_" + tag, PasswordHash = "x", CreatedAt = now.AddDays(-3), LastMatchesViewedAt = now };
            var viewed = new AppUser { Username = "vw_" + tag, PasswordHash = "x", CreatedAt = now, LastMatchesViewedAt = now };
            var never = new AppUser { Username = "nv_" + tag, PasswordHash = "x", CreatedAt = now.AddDays(-10) };
            db.Users.AddRange(returned, viewed, never);
            db.SourceFingerprints.Add(new SourceFingerprintRecord
            {
                User = returned, Source = SourcesController.SelfDescribedSource, FeatureCount = 5
            });
            await db.SaveChangesAsync();
        }

        var (after, body) = await ReadWithBodyAsync(client);

        Assert.Equal(3, after.TotalUsers - before.TotalUsers);
        Assert.Equal(2, after.RegisteredLast7Days - before.RegisteredLast7Days);
        Assert.Equal(2, after.ViewedMatches - before.ViewedMatches);
        Assert.Equal(1, after.ReturnedAfterFirstDay - before.ReturnedAfterFirstDay);
        Assert.Equal(2, after.ActiveLast7Days - before.ActiveLast7Days);
        // Aggregates only: no account is named anywhere in the payload.
        Assert.DoesNotContain(tag, body);
    }

    [Fact]
    public async Task FingerprintsBySource_IsWithheldBelowTheCohort_AndCountsAccountsPerSourceAbove()
    {
        // Own factory: the shared one must stay a tiny cohort for the withholding tests above.
        using var own = new ProfilerWebFactory();
        var client = own.WithWebHostBuilder(b => b.UseSetting(MetricsController.TokenKey, Token))
            .CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

        using (var scope = own.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var first = new AppUser { Username = "src_0", PasswordHash = "x" };
            db.Users.Add(first);
            db.SourceFingerprints.Add(new SourceFingerprintRecord { User = first, Source = SourcesController.SelfDescribedSource, FeatureCount = 5 });
            await db.SaveChangesAsync();
        }
        var (small, _) = await ReadWithBodyAsync(client);
        Assert.Null(small.FingerprintsBySource);

        using (var scope = own.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            for (var i = 1; i < 10; i++)
                db.Users.Add(new AppUser { Username = $"src_{i}", PasswordHash = "x" });
            await db.SaveChangesAsync();
        }
        var (large, _) = await ReadWithBodyAsync(client);
        Assert.Equal(10, large.TotalUsers);
        Assert.NotNull(large.FingerprintsBySource);
        Assert.Equal(1, large.FingerprintsBySource![SourcesController.SelfDescribedSource]);
    }

    private sealed record Snapshot(
        int TotalUsers, int RegisteredLast7Days, int ViewedMatches, int ReturnedAfterFirstDay, int ActiveLast7Days,
        Dictionary<string, int>? FingerprintsBySource);

    private async Task<Snapshot> ReadAsync(HttpClient client) => (await ReadWithBodyAsync(client)).Snapshot;

    private async Task<(Snapshot Snapshot, string Body)> ReadWithBodyAsync(HttpClient client)
    {
        var req = new HttpRequestMessage(HttpMethod.Get, "/metrics");
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", Token);
        var resp = await client.SendAsync(req);
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        var body = await resp.Content.ReadAsStringAsync();
        var snapshot = System.Text.Json.JsonSerializer.Deserialize<Snapshot>(body,
            new System.Text.Json.JsonSerializerOptions { PropertyNameCaseInsensitive = true })!;
        return (snapshot, body);
    }

    [Fact]
    public async Task AnOrdinarySession_DoesNotGrantAccess()
    {
        var client = WithToken();

        // Register (gets an auth cookie) but present no bearer token.
        var page = await client.GetAsync("/account/register");
        var token = System.Text.RegularExpressions.Regex.Match(
            await page.Content.ReadAsStringAsync(),
            "name=\"__RequestVerificationToken\"[^>]*value=\"([^\"]+)\"").Groups[1].Value;
        await client.PostAsync("/account/register", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["Username"] = "sess_" + Guid.NewGuid().ToString("N")[..8],
            ["Password"] = "Tr0ubad0ur-x9",
            ["ConfirmPassword"] = "Tr0ubad0ur-x9",
            ["__RequestVerificationToken"] = token
        }));

        // The session cookie is now set, but the operator token is what gates /metrics.
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/metrics")).StatusCode);
    }
}
