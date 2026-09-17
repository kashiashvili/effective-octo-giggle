using System.Net;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Profiler.Web.Data;
using Profiler.Web.Data.Models;
using Xunit;

namespace Profiler.Web.Tests.Integration;

/// <summary>
/// Circles (docs/DESIGN_CIRCLES.md), slice 1: start, invite, join, leave; the invite is never
/// stored; an invitee without an account is carried through registration or login; deletion
/// removes memberships and an emptied circle; export lists circles; the flag hides everything.
/// </summary>
public class CirclesTests : IClassFixture<ProfilerWebFactory>
{
    private readonly ProfilerWebFactory _factory;
    public CirclesTests(ProfilerWebFactory factory) => _factory = factory;

    private HttpClient NewClient(WebApplicationFactory<Program>? factory = null) =>
        (factory ?? _factory).CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

    private static string AntiforgeryIn(string html) =>
        Regex.Match(html, "name=\"__RequestVerificationToken\"[^>]*value=\"([^\"]+)\"").Groups[1].Value;

    private static string InviteTokenIn(string html) =>
        Regex.Match(html, @"/circles/join/([A-Za-z0-9_-]+)").Groups[1].Value;

    private static async Task RegisterAsync(HttpClient client, string username, string? circleInvite = null)
    {
        var page = await client.GetAsync("/account/register");
        var fields = new Dictionary<string, string>
        {
            ["Username"] = username,
            ["Password"] = "Tr0ubad0ur-x9",
            ["ConfirmPassword"] = "Tr0ubad0ur-x9",
            ["__RequestVerificationToken"] = AntiforgeryIn(await page.Content.ReadAsStringAsync()),
        };
        if (circleInvite != null) fields["CircleInvite"] = circleInvite;
        var resp = await client.PostAsync("/account/register", new FormUrlEncodedContent(fields));
        Assert.Equal(HttpStatusCode.Redirect, resp.StatusCode);
    }

    /// <summary>POST a form with the antiforgery token from the dashboard (every circle form lives there or on the join page).</summary>
    private static async Task<HttpResponseMessage> PostAsync(HttpClient client, string path, Dictionary<string, string> fields, string tokenPage = "/sources/dashboard")
    {
        var page = await client.GetAsync(tokenPage);
        fields["__RequestVerificationToken"] = AntiforgeryIn(await page.Content.ReadAsStringAsync());
        return await client.PostAsync(path, new FormUrlEncodedContent(fields));
    }

    private static async Task<string> StartCircleAsync(HttpClient client, string name)
    {
        var resp = await PostAsync(client, "/circles/create", new() { ["name"] = name });
        Assert.Equal(HttpStatusCode.Redirect, resp.StatusCode);
        var dashboard = await client.GetStringAsync("/sources/dashboard");
        Assert.Contains(name, dashboard);
        var token = InviteTokenIn(dashboard);
        Assert.False(string.IsNullOrEmpty(token), "dashboard must show an invite link for the new circle");
        return token;
    }

    [Fact]
    public async Task StartJoinLeave_ThroughTheInviteLink_AndNothingAboutTheLinkIsStored()
    {
        var tag = Guid.NewGuid().ToString("N")[..6];
        var circleName = "Tuesday book club " + tag;

        var a = NewClient();
        await RegisterAsync(a, "circ_a_" + tag);
        var token = await StartCircleAsync(a, circleName);
        Assert.Contains("1 member<", await a.GetStringAsync("/sources/dashboard"));

        var b = NewClient();
        await RegisterAsync(b, "circ_b_" + tag);
        var invite = await b.GetStringAsync($"/circles/join/{token}");
        Assert.Contains(circleName, invite);
        Assert.Contains("Join", invite);
        Assert.DoesNotContain(circleName, await b.GetStringAsync("/sources/dashboard")); // the link alone joins nobody

        var joined = await PostAsync(b, "/circles/join", new() { ["token"] = token }, tokenPage: $"/circles/join/{token}");
        Assert.Equal(HttpStatusCode.Redirect, joined.StatusCode);
        var bDashboard = await b.GetStringAsync("/sources/dashboard");
        Assert.Contains(circleName, bDashboard);
        Assert.Contains("2 members", bDashboard);

        // Joining twice is idempotent.
        await PostAsync(b, "/circles/join", new() { ["token"] = token }, tokenPage: $"/circles/join/{token}");

        int circleId;
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var circle = await db.Circles.SingleAsync(c => c.Name == circleName);
            circleId = circle.Id;
            Assert.Equal(2, await db.CircleMemberships.CountAsync(m => m.CircleId == circle.Id));

            // Structural privacy assertion: a membership carries nothing but the pair and a time —
            // no inviter, no token, no source.
            var columns = db.Model.FindEntityType(typeof(CircleMembership))!.GetProperties().Select(p => p.Name).OrderBy(n => n).ToArray();
            Assert.Equal(new[] { "CircleId", "Id", "JoinedAt", "UserId" }, columns);
            Assert.Equal(new[] { "CreatedAt", "Id", "Name" }, db.Model.FindEntityType(typeof(Circle))!.GetProperties().Select(p => p.Name).OrderBy(n => n).ToArray());
            Assert.DoesNotContain(token, circle.Name);
        }

        // Leave: B first (circle stays), then A (circle disappears with its last member).
        Assert.Equal(HttpStatusCode.Redirect, (await PostAsync(b, "/circles/leave", new() { ["circleId"] = circleId.ToString() })).StatusCode);
        Assert.DoesNotContain(circleName, await b.GetStringAsync("/sources/dashboard"));
        Assert.Contains("1 member<", await a.GetStringAsync("/sources/dashboard"));
        await PostAsync(a, "/circles/leave", new() { ["circleId"] = circleId.ToString() });
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            Assert.False(await db.Circles.AnyAsync(c => c.Id == circleId));
            Assert.False(await db.CircleMemberships.AnyAsync(m => m.CircleId == circleId));
        }
    }

    [Fact]
    public async Task TamperedOrExpiredInvite_IsRefused_AndJoinsNobody()
    {
        var tag = Guid.NewGuid().ToString("N")[..6];
        var a = NewClient();
        await RegisterAsync(a, "circ_t_" + tag);

        var page = await a.GetAsync("/circles/join/not-a-real-token");
        Assert.Equal(HttpStatusCode.OK, page.StatusCode);
        Assert.Contains("expired", await page.Content.ReadAsStringAsync(), StringComparison.OrdinalIgnoreCase);

        var post = await PostAsync(a, "/circles/join", new() { ["token"] = "garbage" });
        Assert.Contains("expired", await post.Content.ReadAsStringAsync(), StringComparison.OrdinalIgnoreCase);

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var me = await db.Users.SingleAsync(u => u.Username == "circ_t_" + tag);
        Assert.False(await db.CircleMemberships.AnyAsync(m => m.UserId == me.Id));
    }

    [Fact]
    public async Task AnInviteeWithoutAnAccount_IsCarriedThroughRegistrationAndLogin_ToTheJoinPage()
    {
        var tag = Guid.NewGuid().ToString("N")[..6];
        var host = NewClient();
        await RegisterAsync(host, "circ_h_" + tag);
        var token = await StartCircleAsync(host, "Climbing crew " + tag);

        // Anonymous: the invite page offers to register or sign in, carrying the token.
        var anon = NewClient();
        var invite = await anon.GetStringAsync($"/circles/join/{token}");
        Assert.Contains($"/account/register?circle={token}", invite);
        Assert.Contains($"/account/login?circle={token}", invite);
        var registerForm = await anon.GetStringAsync($"/account/register?circle={token}");
        Assert.Contains($"value=\"{token}\"", registerForm);

        // Register with the invite: the recovery code still comes first, then the join page.
        await RegisterAsync(anon, "circ_n_" + tag, circleInvite: token);
        var recovery = await anon.GetStringAsync("/account/recovery-code");
        Assert.Contains($"/circles/join/{token}", recovery);
        Assert.Contains("join the circle", recovery);
        var joinPage = await anon.GetStringAsync($"/circles/join/{token}");
        Assert.Contains("Climbing crew " + tag, joinPage);
        Assert.DoesNotContain("You're already in this circle", joinPage);

        // Login with the invite: straight to the join page.
        var returning = NewClient();
        var loginForm = await returning.GetStringAsync($"/account/login?circle={token}");
        Assert.Contains($"value=\"{token}\"", loginForm);
        var login = await returning.PostAsync("/account/login", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["Username"] = "circ_n_" + tag,
            ["Password"] = "Tr0ubad0ur-x9",
            ["CircleInvite"] = token,
            ["__RequestVerificationToken"] = AntiforgeryIn(loginForm),
        }));
        Assert.Equal(HttpStatusCode.Redirect, login.StatusCode);
        Assert.Contains($"/circles/join/{token}", login.Headers.Location!.ToString());

        // A junk "circle" parameter is dropped, not reflected.
        var junk = await NewClient().GetStringAsync("/account/register?circle=%3Cscript%3E");
        Assert.DoesNotContain("<script>", junk);
    }

    [Fact]
    public async Task DeletingAnAccount_RemovesItsMemberships_AndACircleItEmptied()
    {
        var tag = Guid.NewGuid().ToString("N")[..6];
        var a = NewClient();
        await RegisterAsync(a, "circ_da_" + tag);
        var token = await StartCircleAsync(a, "Pottery night " + tag);
        var b = NewClient();
        await RegisterAsync(b, "circ_db_" + tag);
        await PostAsync(b, "/circles/join", new() { ["token"] = token }, tokenPage: $"/circles/join/{token}");

        Assert.Equal(HttpStatusCode.Redirect, (await PostAsync(a, "/account/delete", new() { ["password"] = "Tr0ubad0ur-x9" })).StatusCode);
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            Assert.False(await db.Users.AnyAsync(u => u.Username == "circ_da_" + tag));
            var circle = await db.Circles.SingleAsync(c => c.Name == "Pottery night " + tag); // B is still in it
            Assert.Equal(1, await db.CircleMemberships.CountAsync(m => m.CircleId == circle.Id));
        }

        Assert.Equal(HttpStatusCode.Redirect, (await PostAsync(b, "/account/delete", new() { ["password"] = "Tr0ubad0ur-x9" })).StatusCode);
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            Assert.False(await db.Circles.AnyAsync(c => c.Name == "Pottery night " + tag));
        }
    }

    [Fact]
    public async Task TheExport_ListsCirclesByName()
    {
        var tag = Guid.NewGuid().ToString("N")[..6];
        var a = NewClient();
        await RegisterAsync(a, "circ_x_" + tag);
        await StartCircleAsync(a, "Sourdough " + tag);

        var json = await a.GetStringAsync("/account/data.json");
        Assert.Contains("Sourdough " + tag, json);
        Assert.Contains("\"Circles\"", json);
        var page = await a.GetStringAsync("/account/data");
        Assert.Contains("Sourdough " + tag, page);
    }

    [Fact]
    public async Task WithCirclesOff_NothingIsOfferedOrReachable()
    {
        var tag = Guid.NewGuid().ToString("N")[..6];
        using var off = _factory.WithWebHostBuilder(b => b.UseSetting("Signals:CirclesEnabled", "false"));
        var a = NewClient(off);
        await RegisterAsync(a, "circ_off_" + tag);

        Assert.DoesNotContain("Your circles", await a.GetStringAsync("/sources/dashboard"));
        Assert.Equal(HttpStatusCode.NotFound, (await a.GetAsync("/circles/join/anything")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await PostAsync(a, "/circles/create", new() { ["name"] = "x" })).StatusCode);
    }

    [Fact]
    public async Task ACircleName_IsValidated_AndRenderedAsPlainText()
    {
        var tag = Guid.NewGuid().ToString("N")[..6];
        var a = NewClient();
        await RegisterAsync(a, "circ_v_" + tag);

        var tooLong = await PostAsync(a, "/circles/create", new() { ["name"] = new string('x', 41) });
        Assert.Equal(HttpStatusCode.Redirect, tooLong.StatusCode);
        Assert.DoesNotContain(new string('x', 41), await a.GetStringAsync("/sources/dashboard"));

        var html = "<b>bold " + tag + "</b>";
        await PostAsync(a, "/circles/create", new() { ["name"] = html });
        var dashboard = await a.GetStringAsync("/sources/dashboard");
        Assert.Contains("&lt;b&gt;bold " + tag, dashboard);
        Assert.DoesNotContain(html, dashboard);
    }
}
