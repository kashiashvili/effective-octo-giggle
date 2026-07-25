using System.Net;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Profiler.Web.Data;
using Xunit;

namespace Profiler.Web.Tests.Integration;

/// <summary>
/// Privacy-preserving anti-sybil on registration: a honeypot (always on) and a signed, single-use,
/// time-limited form ticket (opt-in via AntiAbuse:GuardRegistration). No third-party CAPTCHA, no PII.
/// The default-off ticket keeps dev and the rest of the suite undisturbed; these tests turn it on.
/// </summary>
public class RegistrationGuardTests : IClassFixture<ProfilerWebFactory>
{
    private readonly ProfilerWebFactory _factory;
    public RegistrationGuardTests(ProfilerWebFactory factory) => _factory = factory;

    private static readonly WebApplicationFactoryClientOptions NoRedirect = new() { AllowAutoRedirect = false };

    private HttpClient GuardOff() => _factory.CreateClient(NoRedirect);

    private HttpClient GuardOn(int minSeconds = 0) =>
        _factory.WithWebHostBuilder(b =>
        {
            b.UseSetting("AntiAbuse:GuardRegistration", "true");
            b.UseSetting("AntiAbuse:MinFormSeconds", minSeconds.ToString());
        }).CreateClient(NoRedirect);

    private static async Task<(string token, string? ticket)> ReadFormAsync(HttpClient client)
    {
        var html = await (await client.GetAsync("/account/register")).Content.ReadAsStringAsync();
        var token = Regex.Match(html, "name=\"__RequestVerificationToken\"[^>]*value=\"([^\"]+)\"").Groups[1].Value;
        var ticketMatch = Regex.Match(html, "name=\"FormTicket\"[^>]*value=\"([^\"]*)\"");
        return (token, ticketMatch.Success && ticketMatch.Groups[1].Value.Length > 0 ? ticketMatch.Groups[1].Value : null);
    }

    private static FormUrlEncodedContent Form(string user, string token, string? ticket = null, string honeypot = "")
    {
        var f = new Dictionary<string, string>
        {
            ["Username"] = user,
            ["Password"] = "Tr0ubad0ur-x9",
            ["ConfirmPassword"] = "Tr0ubad0ur-x9",
            ["Website"] = honeypot,
            ["__RequestVerificationToken"] = token,
        };
        if (ticket != null) f["FormTicket"] = ticket;
        return new FormUrlEncodedContent(f);
    }

    private async Task<bool> UserExistsAsync(string username)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        return await db.Users.AsNoTracking().AnyAsync(u => u.Username == username);
    }

    [Fact]
    public async Task Honeypot_IsAlwaysChecked_EvenWithTheGuardOff()
    {
        var client = GuardOff();
        var user = "hp_" + Guid.NewGuid().ToString("N")[..8];
        var (token, ticket) = await ReadFormAsync(client);

        var resp = await client.PostAsync("/account/register", Form(user, token, ticket, honeypot: "http://spam.example"));
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode); // re-rendered form, not a redirect
        Assert.False(await UserExistsAsync(user));
    }

    [Fact]
    public async Task WithGuardOff_RegistrationWorksWithoutATicket()
    {
        var client = GuardOff();
        var user = "off_" + Guid.NewGuid().ToString("N")[..8];
        var (token, _) = await ReadFormAsync(client);

        var resp = await client.PostAsync("/account/register", Form(user, token)); // no ticket
        Assert.Equal(HttpStatusCode.Redirect, resp.StatusCode);
        Assert.True(await UserExistsAsync(user));
    }

    [Fact]
    public async Task WithGuardOn_ADirectPostWithoutATicket_IsRejected()
    {
        var client = GuardOn();
        var user = "noticket_" + Guid.NewGuid().ToString("N")[..8];
        // Grab a valid antiforgery token but deliberately omit the form ticket (a blind POST).
        var (token, _) = await ReadFormAsync(client);

        var resp = await client.PostAsync("/account/register", Form(user, token, ticket: null));
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        Assert.False(await UserExistsAsync(user));
    }

    [Fact]
    public async Task WithGuardOn_TheNormalFormFlow_Succeeds()
    {
        var client = GuardOn();
        var user = "on_" + Guid.NewGuid().ToString("N")[..8];
        var (token, ticket) = await ReadFormAsync(client);
        Assert.NotNull(ticket); // the GET issues a ticket when enabled

        var resp = await client.PostAsync("/account/register", Form(user, token, ticket));
        Assert.Equal(HttpStatusCode.Redirect, resp.StatusCode);
        Assert.True(await UserExistsAsync(user));
    }

    [Fact]
    public async Task WithGuardOn_AReusedTicket_CannotMintASecondAccount()
    {
        // Both clients must hit the SAME app instance so they share the in-process single-use cache
        // (separate WithWebHostBuilder calls would each get their own cache). Two clients = two cookie
        // jars, so client B is not signed in and brings its own antiforgery token — only the ticket's
        // single-use is under test, not antiforgery.
        var app = _factory.WithWebHostBuilder(b =>
        {
            b.UseSetting("AntiAbuse:GuardRegistration", "true");
            b.UseSetting("AntiAbuse:MinFormSeconds", "0");
        });
        var a = app.CreateClient(NoRedirect);
        var b2 = app.CreateClient(NoRedirect);

        var (tokenA, ticket) = await ReadFormAsync(a);
        var first = "reuse_a_" + Guid.NewGuid().ToString("N")[..6];
        Assert.Equal(HttpStatusCode.Redirect, (await a.PostAsync("/account/register", Form(first, tokenA, ticket))).StatusCode);
        Assert.True(await UserExistsAsync(first));

        var (tokenB, _) = await ReadFormAsync(b2);
        var second = "reuse_b_" + Guid.NewGuid().ToString("N")[..6];
        var resp = await b2.PostAsync("/account/register", Form(second, tokenB, ticket));
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        Assert.False(await UserExistsAsync(second));
    }

    [Fact]
    public async Task WithGuardOn_AnInstantSubmit_IsRejectedByTheTimeTrap()
    {
        var client = GuardOn(minSeconds: 30); // any real submit within 30s of loading is "too quick"
        var user = "fast_" + Guid.NewGuid().ToString("N")[..8];
        var (token, ticket) = await ReadFormAsync(client);

        var resp = await client.PostAsync("/account/register", Form(user, token, ticket));
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        Assert.False(await UserExistsAsync(user));
    }
}
