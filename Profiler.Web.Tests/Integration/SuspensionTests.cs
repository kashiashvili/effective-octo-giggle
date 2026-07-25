using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Profiler.Web.Controllers;
using Profiler.Web.Data;
using Profiler.Web.Data.Models;
using Profiler.Web.Profile;
using Xunit;

namespace Profiler.Web.Tests.Integration;

/// <summary>
/// Suspension is the operator's action on reports: a reversible flag (not a delete — a leaked token
/// must not be able to destroy accounts) that removes an account from everyone's matches and refuses
/// its sessions and logins. It is behind the same operator token as the rest of /metrics.
/// </summary>
public class SuspensionTests : IClassFixture<ProfilerWebFactory>
{
    private readonly ProfilerWebFactory _factory;
    public SuspensionTests(ProfilerWebFactory factory) => _factory = factory;

    private const string Token = "metrics-secret-zzqx";

    private HttpClient PlainClient() => _factory.CreateClient(new WebApplicationFactoryClientOptions
    {
        AllowAutoRedirect = false
    });

    private HttpClient OperatorClient() =>
        _factory.WithWebHostBuilder(b => b.UseSetting(MetricsController.TokenKey, Token))
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

    private async Task<HttpResponseMessage> SuspendAsync(string username, bool suspend)
    {
        var req = new HttpRequestMessage(HttpMethod.Post, "/metrics/suspend")
        {
            Content = JsonContent.Create(new { Username = username, Suspend = suspend })
        };
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", Token);
        return await OperatorClient().SendAsync(req);
    }

    [Fact]
    public async Task Suspending_RemovesTheAccountFromMatches_AndReinstatingRestoresIt()
    {
        var viewer = "sus_v_" + Guid.NewGuid().ToString("N")[..6];
        var target = "sus_t_" + Guid.NewGuid().ToString("N")[..6];
        var fpJson = new FingerprintGenerator(128).Generate(new[] { "x:1", "x:2", "x:3" }).ToJson();

        var client = PlainClient();
        await RegisterAsync(client, viewer);
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var me = await db.Users.FirstAsync(u => u.Username == viewer);
            var them = new AppUser { Username = target, PasswordHash = "x", IsDiscoverable = true };
            db.Users.Add(them);
            await db.SaveChangesAsync();
            foreach (var uid in new[] { me.Id, them.Id })
                db.Fingerprints.Add(new FingerprintRecord { UserId = uid, FingerprintJson = fpJson, SourcesJson = "[\"GitHub\"]" });
            await db.SaveChangesAsync();
        }

        Assert.Contains(target, await (await client.GetAsync("/matches")).Content.ReadAsStringAsync());

        Assert.Equal(HttpStatusCode.OK, (await SuspendAsync(target, true)).StatusCode);
        Assert.DoesNotContain(target, await (await client.GetAsync("/matches")).Content.ReadAsStringAsync());

        Assert.Equal(HttpStatusCode.OK, (await SuspendAsync(target, false)).StatusCode);
        Assert.Contains(target, await (await client.GetAsync("/matches")).Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task ASuspendedAccounts_ExistingSession_IsRejected()
    {
        var user = "sus_sess_" + Guid.NewGuid().ToString("N")[..6];
        var client = PlainClient();
        await RegisterAsync(client, user); // now holds an auth cookie

        // Authenticated before suspension.
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/sources/dashboard")).StatusCode);

        Assert.Equal(HttpStatusCode.OK, (await SuspendAsync(user, true)).StatusCode);

        // The persistent cookie is now refused on the next request — bounced to login.
        var after = await client.GetAsync("/sources/dashboard");
        Assert.Equal(HttpStatusCode.Redirect, after.StatusCode);
        Assert.Contains("/account/login", after.Headers.Location?.OriginalString ?? "");
    }

    [Fact]
    public async Task ASuspendedAccount_CannotLogInAgain()
    {
        var user = "sus_login_" + Guid.NewGuid().ToString("N")[..6];
        var client = PlainClient();
        await RegisterAsync(client, user);
        await SuspendAsync(user, true);

        var login = await client.GetAsync("/account/login");
        var resp = await client.PostAsync("/account/login", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["Username"] = user,
            ["Password"] = "Tr0ubad0ur-x9",
            ["__RequestVerificationToken"] = await TokenAsync(login)
        }));
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode); // re-rendered form, not a redirect to dashboard
        Assert.Contains("suspended", await resp.Content.ReadAsStringAsync(), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task TheSuspendEndpoint_IsOperatorTokenGated()
    {
        var user = "sus_gate_" + Guid.NewGuid().ToString("N")[..6];
        var client = PlainClient();
        await RegisterAsync(client, user);

        // No token configured on the plain factory → the route does not exist.
        var noToken = await client.PostAsync("/metrics/suspend",
            JsonContent.Create(new { Username = user, Suspend = true }));
        Assert.Equal(HttpStatusCode.NotFound, noToken.StatusCode);

        // Token configured but a wrong bearer presented → unauthorized, and nothing changes.
        var req = new HttpRequestMessage(HttpMethod.Post, "/metrics/suspend")
        {
            Content = JsonContent.Create(new { Username = user, Suspend = true })
        };
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", "wrong");
        Assert.Equal(HttpStatusCode.Unauthorized, (await OperatorClient().SendAsync(req)).StatusCode);

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        Assert.Null((await db.Users.AsNoTracking().FirstAsync(u => u.Username == user)).SuspendedAt);
    }

    [Fact]
    public async Task AMalformedBody_DoesNotRevealTheRoute_WhenTheFeatureIsOff()
    {
        // The token check runs before model binding, so a bad body with no token configured must still
        // 404 (route hidden) rather than 400 (which would admit the route exists).
        var client = PlainClient();

        var missingField = await client.PostAsync("/metrics/suspend",
            JsonContent.Create(new { suspend = true })); // no username
        Assert.Equal(HttpStatusCode.NotFound, missingField.StatusCode);

        var garbage = await client.PostAsync("/metrics/suspend",
            new StringContent("not json", System.Text.Encoding.UTF8, "application/json"));
        Assert.Equal(HttpStatusCode.NotFound, garbage.StatusCode);
    }
}
