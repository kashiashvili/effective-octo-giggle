using System.Net;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Profiler.Web.Data;
using Profiler.Web.Data.Models;
using Profiler.Web.Profile;
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
        Assert.Contains("1 member ·", await a.GetStringAsync("/sources/dashboard"));

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
        Assert.Contains("1 member ·", await a.GetStringAsync("/sources/dashboard"));
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

    /// <summary>Gives a registered user a fingerprint so they take part in matching.</summary>
    private async Task SeedFingerprintAsync(string username, params string[] features)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var gen = scope.ServiceProvider.GetRequiredService<FingerprintGenerator>();
        var me = await db.Users.SingleAsync(u => u.Username == username);
        db.Fingerprints.Add(new FingerprintRecord { UserId = me.Id, FingerprintJson = gen.Generate(features).ToJson(), SourcesJson = "[\"GitHub\"]" });
        await db.SaveChangesAsync();
    }

    private static string CardOf(string matchesHtml, string username)
    {
        var start = matchesHtml.IndexOf(username, StringComparison.Ordinal);
        Assert.True(start >= 0, $"{username} should be on the match list");
        var end = matchesHtml.IndexOf("class=\"match-card\"", start, StringComparison.Ordinal);
        return end < 0 ? matchesHtml[start..] : matchesHtml[start..end];
    }

    [Fact]
    public async Task SameCircle_IsAChipAndASort_OnlyBetweenMembers_OnlyWhileVisible_NeverAFilter()
    {
        var tag = Guid.NewGuid().ToString("N")[..6];
        var (aName, bName, cName) = ("circ_ma_" + tag, "circ_mb_" + tag, "circ_mc_" + tag);
        var a = NewClient(); await RegisterAsync(a, aName);
        var b = NewClient(); await RegisterAsync(b, bName);
        var c = NewClient(); await RegisterAsync(c, cName);
        // C is the stronger interest match for A; B (the circle-mate) is weaker but above the floor.
        var mine = Enumerable.Range(1, 10).Select(i => $"{tag}:{i}").ToArray();
        await SeedFingerprintAsync(aName, mine);
        await SeedFingerprintAsync(cName, mine);
        await SeedFingerprintAsync(bName, mine.Take(5).Concat(new[] { $"{tag}:b1", $"{tag}:b2" }).ToArray());

        var token = await StartCircleAsync(a, "Run club " + tag);
        await PostAsync(b, "/circles/join", new() { ["token"] = token }, tokenPage: $"/circles/join/{token}");

        // Chip on the circle-mate's card only; the outsider still appears (never a filter).
        var matches = await a.GetStringAsync("/matches");
        Assert.Contains("Same circle: Run club " + tag, CardOf(matches, bName));
        Assert.DoesNotContain("Same circle", CardOf(matches, cName));
        Assert.Contains("Same circle first", matches);
        // Default order is interest order: the stronger match first.
        Assert.True(matches.IndexOf(cName, StringComparison.Ordinal) < matches.IndexOf(bName, StringComparison.Ordinal), "default order is by interest");
        // The sort lifts the circle-mate without dropping anyone.
        var sorted = await a.GetStringAsync("/matches?sort=circle");
        Assert.True(sorted.IndexOf(bName, StringComparison.Ordinal) < sorted.IndexOf(cName, StringComparison.Ordinal), "sort=circle lifts the circle-mate");
        Assert.Contains(cName, sorted);

        // The outsider has no circle: no chip, no sort offered, and B's card carries nothing.
        var outsider = await c.GetStringAsync("/matches");
        Assert.DoesNotContain("Same circle", outsider);

        // Hidden viewer: the chip and the sort are withheld, like bio and contact.
        Assert.Equal(HttpStatusCode.Redirect, (await PostAsync(a, "/account/visibility", new() { ["discoverable"] = "false" })).StatusCode);
        var hidden = await a.GetStringAsync("/matches");
        Assert.DoesNotContain("Same circle:", hidden);
        Assert.DoesNotContain("Same circle first", hidden);
        await PostAsync(a, "/account/visibility", new() { ["discoverable"] = "true" });

        // Leaving removes the chip.
        int circleId;
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            circleId = (await db.Circles.SingleAsync(x => x.Name == "Run club " + tag)).Id;
        }
        await PostAsync(b, "/circles/leave", new() { ["circleId"] = circleId.ToString() });
        Assert.DoesNotContain("Same circle:", await a.GetStringAsync("/matches"));
    }

    [Fact]
    public async Task WithCirclesOff_TheChipAndSortDisappear_ButRowsStay()
    {
        var tag = Guid.NewGuid().ToString("N")[..6];
        var (aName, bName) = ("circ_fa_" + tag, "circ_fb_" + tag);
        var a = NewClient(); await RegisterAsync(a, aName);
        var b = NewClient(); await RegisterAsync(b, bName);
        var features = new[] { $"{tag}:1", $"{tag}:2", $"{tag}:3" };
        await SeedFingerprintAsync(aName, features);
        await SeedFingerprintAsync(bName, features);
        var token = await StartCircleAsync(a, "Choir " + tag);
        await PostAsync(b, "/circles/join", new() { ["token"] = token }, tokenPage: $"/circles/join/{token}");
        Assert.Contains("Same circle: Choir " + tag, await a.GetStringAsync("/matches"));

        using var off = _factory.WithWebHostBuilder(h => h.UseSetting("Signals:CirclesEnabled", "false"));
        var aOff = NewClient(off);
        var loginPage = await aOff.GetStringAsync("/account/login");
        var login = await aOff.PostAsync("/account/login", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["Username"] = aName, ["Password"] = "Tr0ubad0ur-x9", ["__RequestVerificationToken"] = AntiforgeryIn(loginPage)
        }));
        Assert.Equal(HttpStatusCode.Redirect, login.StatusCode);
        var matches = await aOff.GetStringAsync("/matches");
        Assert.Contains(bName, matches);
        Assert.DoesNotContain("Same circle", matches);

        // With circles off an invite carried into login goes nowhere near a 404: plain dashboard.
        var bOff = NewClient(off);
        var bLoginPage = await bOff.GetStringAsync($"/account/login?circle={token}");
        Assert.DoesNotContain($"value=\"{token}\"", bLoginPage);
        var bLogin = await bOff.PostAsync("/account/login", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["Username"] = bName, ["Password"] = "Tr0ubad0ur-x9", ["CircleInvite"] = token, ["__RequestVerificationToken"] = AntiforgeryIn(bLoginPage)
        }));
        Assert.Contains("/sources/dashboard", bLogin.Headers.Location!.ToString());

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        Assert.Equal(2, await db.CircleMemberships.CountAsync(m => db.Circles.Any(x => x.Id == m.CircleId && x.Name == "Choir " + tag)));
    }

    [Fact]
    public async Task Metrics_CountCirclesAndMembers_AsPlainTotals()
    {
        var tag = Guid.NewGuid().ToString("N")[..6];
        const string metricsToken = "circles-metrics-zzqx";
        var op = _factory.WithWebHostBuilder(h => h.UseSetting(Profiler.Web.Controllers.MetricsController.TokenKey, metricsToken))
            .CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        async Task<(int Circles, int Users)> ReadAsync()
        {
            var req = new HttpRequestMessage(HttpMethod.Get, "/metrics");
            req.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", metricsToken);
            var body = await (await op.SendAsync(req)).Content.ReadAsStringAsync();
            using var doc = System.Text.Json.JsonDocument.Parse(body);
            return (doc.RootElement.GetProperty("circles").GetInt32(), doc.RootElement.GetProperty("usersInCircles").GetInt32());
        }

        var before = await ReadAsync();
        var a = NewClient(); await RegisterAsync(a, "circ_me_" + tag);
        var b = NewClient(); await RegisterAsync(b, "circ_mf_" + tag);
        var token = await StartCircleAsync(a, "Metrics " + tag);
        await PostAsync(b, "/circles/join", new() { ["token"] = token }, tokenPage: $"/circles/join/{token}");
        var after = await ReadAsync();

        Assert.Equal(1, after.Circles - before.Circles);
        Assert.Equal(2, after.Users - before.Users);
    }

    [Fact]
    public async Task OneAccount_CannotExceedTheMembershipCap_OrTheRateLimit()
    {
        var tag = Guid.NewGuid().ToString("N")[..6];
        var a = NewClient();
        await RegisterAsync(a, "circ_cap_" + tag);
        for (var i = 0; i < Profiler.Web.Controllers.CirclesController.MaxCirclesPerUser; i++)
            Assert.Equal(HttpStatusCode.Redirect, (await PostAsync(a, "/circles/create", new() { ["name"] = $"cap {tag} {i}" })).StatusCode);

        await PostAsync(a, "/circles/create", new() { ["name"] = $"cap {tag} overflow" });
        var dashboard = await a.GetStringAsync("/sources/dashboard");
        Assert.Contains("at most", dashboard);
        Assert.DoesNotContain($"cap {tag} overflow", dashboard);

        // Joining past the cap is refused the same way, and the membership is not created.
        var host = NewClient();
        await RegisterAsync(host, "circ_caph_" + tag);
        var token = await StartCircleAsync(host, "cap host " + tag);
        await PostAsync(a, "/circles/join", new() { ["token"] = token }, tokenPage: $"/circles/join/{token}");
        Assert.DoesNotContain("cap host " + tag, await a.GetStringAsync("/sources/dashboard"));

        // The per-IP limiter meters create and join like the other write endpoints.
        using var tight = _factory.WithWebHostBuilder(h => h.UseSetting("RateLimiting:CirclesPermitLimit", "2"));
        var r = NewClient(tight);
        await RegisterAsync(r, "circ_rate_" + tag);
        Assert.Equal(HttpStatusCode.Redirect, (await PostAsync(r, "/circles/create", new() { ["name"] = "rate 1 " + tag })).StatusCode);
        Assert.Equal(HttpStatusCode.Redirect, (await PostAsync(r, "/circles/create", new() { ["name"] = "rate 2 " + tag })).StatusCode);
        var third = await PostAsync(r, "/circles/create", new() { ["name"] = "rate 3 " + tag });
        Assert.Equal(HttpStatusCode.TooManyRequests, third.StatusCode);
        Assert.Contains("Too many circle changes", await third.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task TheRecoveryPagesInviteHandOff_IsShownOnce()
    {
        var tag = Guid.NewGuid().ToString("N")[..6];
        var host = NewClient();
        await RegisterAsync(host, "circ_once_h_" + tag);
        var token = await StartCircleAsync(host, "Once " + tag);

        var invitee = NewClient();
        await RegisterAsync(invitee, "circ_once_" + tag, circleInvite: token);
        Assert.Contains("join the circle", await invitee.GetStringAsync("/account/recovery-code"));

        // A later recovery-code page (a regenerated code) must not resurrect the hand-off.
        var regen = await PostAsync(invitee, "/account/recovery-code", new() { ["password"] = "Tr0ubad0ur-x9" });
        Assert.Equal(HttpStatusCode.Redirect, regen.StatusCode);
        var again = await invitee.GetStringAsync("/account/recovery-code");
        Assert.Contains("recovery code", again, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("join the circle", again);
    }

    [Fact]
    public async Task TheEmptyMatchList_OffersTheCircleInviteLink_OnceTheViewerHasACircle()
    {
        var tag = Guid.NewGuid().ToString("N")[..6];
        var a = NewClient();
        await RegisterAsync(a, "circ_inv_" + tag);

        // No fingerprint yet: the no-accounts path leads.
        var noFingerprint = await a.GetStringAsync("/matches");
        Assert.Contains("/sources/interests", noFingerprint);

        // A fingerprint nobody overlaps with: the empty state offers the plain register link.
        await SeedFingerprintAsync("circ_inv_" + tag, $"{tag}:only-me-1", $"{tag}:only-me-2");
        var plain = await a.GetStringAsync("/matches");
        Assert.Contains("/account/register", plain);
        Assert.DoesNotContain("/circles/join/", plain);

        // With a circle, the same box shares the circle's link and says where it leads.
        await StartCircleAsync(a, "Invite box " + tag);
        var withCircle = await a.GetStringAsync("/matches");
        Assert.Contains("/circles/join/", withCircle);
        Assert.Contains("joins <strong>Invite box " + tag + "</strong>", withCircle);
        Assert.DoesNotContain("/account/register\"", withCircle);
    }

    [Fact]
    public async Task TheCircleView_ShowsEveryMember_EvenBelowTheFloor_ToMembersOnly_HonouringHidesAndVisibility()
    {
        var tag = Guid.NewGuid().ToString("N")[..6];
        var (aName, bName, cName, dName) = ("circ_va_" + tag, "circ_vb_" + tag, "circ_vc_" + tag, "circ_vd_" + tag);
        var a = NewClient(); await RegisterAsync(a, aName);
        var b = NewClient(); await RegisterAsync(b, bName);
        var c = NewClient(); await RegisterAsync(c, cName);
        var d = NewClient(); await RegisterAsync(d, dName);
        // B shares nothing with A (below the floor), C has no fingerprint at all.
        await SeedFingerprintAsync(aName, $"{tag}:a1", $"{tag}:a2", $"{tag}:a3");
        await SeedFingerprintAsync(bName, $"{tag}:b1", $"{tag}:b2", $"{tag}:b3");

        var token = await StartCircleAsync(a, "Mixed group " + tag);
        await PostAsync(b, "/circles/join", new() { ["token"] = token }, tokenPage: $"/circles/join/{token}");
        await PostAsync(c, "/circles/join", new() { ["token"] = token }, tokenPage: $"/circles/join/{token}");
        int circleId;
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            circleId = (await db.Circles.SingleAsync(x => x.Name == "Mixed group " + tag)).Id;
        }

        // Invisible on the global list, present in the circle view, honestly labelled.
        Assert.DoesNotContain(bName, await a.GetStringAsync("/matches"));
        var view = await a.GetStringAsync($"/circles/{circleId}");
        Assert.Contains("Mixed group " + tag, view);
        Assert.Contains("3 members", view);
        Assert.Contains("No overlap yet", CardOf(view, bName));
        Assert.Contains("No fingerprint yet", CardOf(view, cName));
        Assert.DoesNotContain("% shared", CardOf(view, bName)); // no false precision below the floor
        Assert.Contains("/circles/join/", view);
        Assert.Contains($"/circles/{circleId}", await a.GetStringAsync("/sources/dashboard"));

        // Members only.
        Assert.Equal(HttpStatusCode.NotFound, (await d.GetAsync($"/circles/{circleId}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await a.GetAsync("/circles/999999")).StatusCode);

        // A hide applies both ways; a hidden member is withheld from others but still sees the circle.
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var aId = (await db.Users.SingleAsync(u => u.Username == aName)).Id;
            var bId = (await db.Users.SingleAsync(u => u.Username == bName)).Id;
            db.UserBlocks.Add(new UserBlock { BlockerId = bId, BlockedId = aId });
            await db.SaveChangesAsync();
        }
        Assert.DoesNotContain(bName, await a.GetStringAsync($"/circles/{circleId}"));
        Assert.DoesNotContain(aName, await b.GetStringAsync($"/circles/{circleId}"));
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            db.UserBlocks.RemoveRange(db.UserBlocks.Where(x => db.Users.Any(u => u.Id == x.BlockerId && u.Username == bName)));
            await db.SaveChangesAsync();
        }
        // Reciprocity: A's contact line is on B's view while B is discoverable, withheld once B hides.
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var aUser = await db.Users.SingleAsync(u => u.Username == aName);
            aUser.Bio = "bio " + tag; aUser.Contact = "contact-" + tag;
            await db.SaveChangesAsync();
        }
        Assert.Contains("contact-" + tag, CardOf(await b.GetStringAsync($"/circles/{circleId}"), aName));
        await PostAsync(b, "/account/visibility", new() { ["discoverable"] = "false" });
        Assert.DoesNotContain(bName, await a.GetStringAsync($"/circles/{circleId}"));
        var hiddenView = await b.GetStringAsync($"/circles/{circleId}");
        Assert.Contains(aName, hiddenView);
        Assert.DoesNotContain("contact-" + tag, hiddenView);
        Assert.DoesNotContain("bio " + tag, hiddenView);
        Assert.Contains("Hidden while you are.", hiddenView);

        // Flag off: 404.
        using var off = _factory.WithWebHostBuilder(h => h.UseSetting("Signals:CirclesEnabled", "false"));
        var aOff = NewClient(off);
        var loginPage = await aOff.GetStringAsync("/account/login");
        await aOff.PostAsync("/account/login", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["Username"] = aName, ["Password"] = "Tr0ubad0ur-x9", ["__RequestVerificationToken"] = AntiforgeryIn(loginPage)
        }));
        Assert.Equal(HttpStatusCode.NotFound, (await aOff.GetAsync($"/circles/{circleId}")).StatusCode);
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
