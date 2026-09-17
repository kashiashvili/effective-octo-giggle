using System.Net;
using System.Net.Http.Headers;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.AspNetCore.Mvc.Testing;
using Profiler.Web.Data;
using Profiler.Web.Data.Models;
using Profiler.Web.Profile;
using Xunit;

namespace Profiler.Web.Tests.Integration;

public class AuthFlowTests : IClassFixture<ProfilerWebFactory>
{
    private readonly ProfilerWebFactory _factory;

    public AuthFlowTests(ProfilerWebFactory factory) => _factory = factory;

    private HttpClient NewClient() => _factory.CreateClient(new WebApplicationFactoryClientOptions
    {
        AllowAutoRedirect = false
    });

    private static async Task<string> ExtractTokenAsync(HttpResponseMessage resp)
    {
        var html = await resp.Content.ReadAsStringAsync();
        var m = Regex.Match(html, "name=\"__RequestVerificationToken\"[^>]*value=\"([^\"]+)\"");
        Assert.True(m.Success, "Antiforgery token not found on page");
        return m.Groups[1].Value;
    }

    private static async Task<HttpResponseMessage> PostFormAsync(
        HttpClient client, string getUrl, string postUrl, Dictionary<string, string> fields)
    {
        var page = await client.GetAsync(getUrl);
        page.EnsureSuccessStatusCode();
        fields["__RequestVerificationToken"] = await ExtractTokenAsync(page);
        return await client.PostAsync(postUrl, new FormUrlEncodedContent(fields));
    }

    private static Task<HttpResponseMessage> RegisterAsync(HttpClient client, string username, string password = "Tr0ubad0ur-x9")
        => PostFormAsync(client, "/account/register", "/account/register", new()
        {
            ["Username"] = username,
            ["Password"] = password,
            ["ConfirmPassword"] = password
        });

    [Theory]
    [InlineData("/sources/dashboard")]
    [InlineData("/sources/connect")]
    [InlineData("/matches")]
    [InlineData("/account/profile")]
    public async Task AnonymousProtectedPage_RedirectsToLogin(string url)
    {
        var client = NewClient();
        var resp = await client.GetAsync(url);

        Assert.Equal(HttpStatusCode.Redirect, resp.StatusCode);
        Assert.Contains("/account/login", resp.Headers.Location!.ToString());
    }

    [Fact]
    public async Task Register_AutoAuthenticates_AndLandsOnTheRecoveryCode()
    {
        var client = NewClient();
        var user = "reg_" + Guid.NewGuid().ToString("N")[..8];

        var reg = await RegisterAsync(client, user);
        Assert.Equal(HttpStatusCode.Redirect, reg.StatusCode);
        // The code is shown before anything else, because it is only ever shown once; that page
        // then leads on to the no-accounts path first, with connecting a source as the alternative.
        Assert.Contains("/account/recovery-code", reg.Headers.Location!.ToString());
        var codePage = await client.GetAsync("/account/recovery-code");
        var codeHtml = await codePage.Content.ReadAsStringAsync();
        Assert.Contains("/sources/interests", codeHtml);
        Assert.Contains("/sources/connect", codeHtml);

        // The auth cookie set by registration should now grant access to a protected page.
        var dash = await client.GetAsync("/sources/dashboard");
        Assert.Equal(HttpStatusCode.OK, dash.StatusCode);
        Assert.Contains(user, await dash.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Login_WithWrongPassword_ShowsError()
    {
        var user = "bad_" + Guid.NewGuid().ToString("N")[..8];
        await RegisterAsync(NewClient(), user);

        var client = NewClient(); // fresh, unauthenticated
        var resp = await PostFormAsync(client, "/account/login", "/account/login", new()
        {
            ["Username"] = user,
            ["Password"] = "wrong-password"
        });

        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        Assert.Contains("Invalid username or password", await resp.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Profile_SaveRoundTrips()
    {
        var client = NewClient();
        var user = "prof_" + Guid.NewGuid().ToString("N")[..8];
        await RegisterAsync(client, user);

        var bio = "loves " + Guid.NewGuid().ToString("N")[..6];
        var save = await PostFormAsync(client, "/account/profile", "/account/profile", new()
        {
            ["Bio"] = bio,
            ["Contact"] = "@" + user
        });
        Assert.Equal(HttpStatusCode.Redirect, save.StatusCode);

        var dash = await client.GetAsync("/sources/dashboard");
        Assert.Contains(bio, await dash.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Register_RejectsCommonPassword()
    {
        var user = "weak_" + Guid.NewGuid().ToString("N")[..8];
        var resp = await RegisterAsync(NewClient(), user, "password123");

        Assert.Equal(HttpStatusCode.OK, resp.StatusCode); // re-rendered form, not a redirect
        Assert.Contains("commonly used", await resp.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Login_RateLimited_ReturnsStyled429()
    {
        // This factory keeps the limit high; drop it so the limiter actually trips.
        using var limited = _factory.WithWebHostBuilder(b => b.UseSetting("RateLimiting:LoginPermitLimit", "1"));
        var client = limited.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

        HttpResponseMessage? last = null;
        for (var i = 0; i < 3; i++)
        {
            last = await PostFormAsync(client, "/account/login", "/account/login", new()
            {
                ["Username"] = "nobody",
                ["Password"] = "wrong-password"
            });
            if (last.StatusCode == HttpStatusCode.TooManyRequests) break;
        }

        Assert.Equal(HttpStatusCode.TooManyRequests, last!.StatusCode);
        Assert.Equal("text/html", last.Content.Headers.ContentType!.MediaType);
        var html = await last.Content.ReadAsStringAsync();
        Assert.Contains("Too many attempts", html);
        Assert.Contains("/css/style.css", html); // styled, not bare text
    }

    [Fact]
    public async Task Connect_OversizedUpload_ReturnsFriendly413_NotAGenericErrorPage()
    {
        var client = NewClient();
        var user = "big_" + Guid.NewGuid().ToString("N")[..8];
        await RegisterAsync(client, user);

        var connectPage = await client.GetAsync("/sources/connect");
        connectPage.EnsureSuccessStatusCode();
        var token = await ExtractTokenAsync(connectPage);

        // Just over the connect POST's 35 MB pipeline-level cap ([RequestSizeLimit] in
        // SourcesController) — generated in memory, never written to disk.
        var oversizedFile = new byte[36 * 1024 * 1024];

        using var content = new MultipartFormDataContent
        {
            { new StringContent(token), "__RequestVerificationToken" }
        };
        var fileContent = new ByteArrayContent(oversizedFile);
        fileContent.Headers.ContentType = new MediaTypeHeaderValue("text/csv");
        content.Add(fileContent, "GoodreadsCsv", "goodreads.csv");

        var resp = await client.PostAsync("/sources/connect", content);

        Assert.Equal(HttpStatusCode.RequestEntityTooLarge, resp.StatusCode);
        var html = await resp.Content.ReadAsStringAsync();
        Assert.Contains("too large", html, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("10 MB", html);
        Assert.Contains("35 MB", html);
        Assert.Contains("/sources/connect", html);
        Assert.Contains("/css/style.css", html); // styled, not bare text
        Assert.DoesNotContain("Something went wrong", html); // not the generic /Home/Error page
    }

    [Fact]
    public async Task Disconnect_RecombinesFingerprint_AndRemovesItWithTheLastSource()
    {
        var client = NewClient();
        var user = "disc_" + Guid.NewGuid().ToString("N")[..8];
        await RegisterAsync(client, user);

        var generator = new FingerprintGenerator(128);
        var githubRaw = generator.GenerateRaw(new[] { "language:c", "topic:kernel" });
        var rssRaw = generator.GenerateRaw(new[] { "rss-topic:privacy", "rss-keyword:crypto" });

        int userId;
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            userId = (await db.Users.FirstAsync(u => u.Username == user)).Id;

            db.SourceFingerprints.Add(new SourceFingerprintRecord
            {
                UserId = userId, Source = "GitHub", FeatureCount = 2,
                RawSignatureJson = System.Text.Json.JsonSerializer.Serialize(githubRaw)
            });
            db.SourceFingerprints.Add(new SourceFingerprintRecord
            {
                UserId = userId, Source = "RSS/Blogs", FeatureCount = 2,
                RawSignatureJson = System.Text.Json.JsonSerializer.Serialize(rssRaw)
            });
            db.Fingerprints.Add(new FingerprintRecord
            {
                UserId = userId,
                FingerprintJson = FingerprintGenerator.FromRaw(
                    FingerprintGenerator.CombineRaw(new[] { githubRaw, rssRaw })).ToJson(),
                SourcesJson = "[\"GitHub\",\"RSS/Blogs\"]"
            });
            await db.SaveChangesAsync();
        }

        // Dropping one source leaves the fingerprint in place, rebuilt from what remains.
        var first = await PostFormAsync(client, "/sources/dashboard", "/sources/disconnect",
            new() { ["source"] = "GitHub" });
        Assert.Equal(HttpStatusCode.Redirect, first.StatusCode);

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            Assert.False(await db.SourceFingerprints.AnyAsync(s => s.UserId == userId && s.Source == "GitHub"));

            var fp = await db.Fingerprints.SingleAsync(f => f.UserId == userId);
            Assert.Equal(FingerprintGenerator.FromRaw(rssRaw).ToJson(), fp.FingerprintJson);
            Assert.DoesNotContain("GitHub", fp.SourcesJson);
        }

        // Dropping the last source removes the fingerprint entirely.
        var second = await PostFormAsync(client, "/sources/dashboard", "/sources/disconnect",
            new() { ["source"] = "RSS/Blogs" });
        Assert.Equal(HttpStatusCode.Redirect, second.StatusCode);

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            Assert.Empty(await db.SourceFingerprints.Where(s => s.UserId == userId).ToListAsync());
            Assert.False(await db.Fingerprints.AnyAsync(f => f.UserId == userId));
        }
    }

    [Fact]
    public async Task Production_RedirectsPlainHttpToHttps()
    {
        // The redirection middleware quietly does nothing when it cannot work out which port to
        // send people to, so the port has to be supplied here just as it does in a real deployment.
        using var prod = _factory.WithWebHostBuilder(b =>
        {
            b.UseEnvironment(Environments.Production);
            b.UseSetting("https_port", "443");
            // Outside Development the app refuses to start without a fingerprint pepper, which is
            // the point of that check — supply one here as a real deployment would.
            b.UseSetting(Profiler.Web.Security.FingerprintPepper.ConfigKey, "test-only-pepper");
        });
        var client = prod.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

        var resp = await client.GetAsync("/health");

        // Development stays on plain HTTP; everything else must be pushed to HTTPS.
        Assert.Equal(HttpStatusCode.TemporaryRedirect, resp.StatusCode);
        Assert.StartsWith("https://", resp.Headers.Location!.ToString());
    }

    [Fact]
    public async Task ErrorPage_IsFriendly_AndLeaksNoInternals()
    {
        var html = await NewClient().GetStringAsync("/home/error");

        Assert.Contains("Something went wrong", html);
        Assert.DoesNotContain("Stack", html, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Exception", html, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Profiler.Web.Controllers", html);
    }

    [Fact]
    public async Task Register_RejectsWhitespaceOnlyUsername()
    {
        var client = NewClient();
        var resp = await RegisterAsync(client, "   ");

        // Must not create an account whose username trims away to nothing.
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        Assert.False(await db.Users.AnyAsync(u => u.Username == "" || u.Username == "   "));
    }

    [Fact]
    public async Task Health_IsAnonymous_AndReportsHealthy()
    {
        var resp = await NewClient().GetAsync("/health");

        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        Assert.Contains("healthy", await resp.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Hide_RemovesPersonFromMatches_And_UnhideRestores()
    {
        var client = NewClient();
        var meName = "hide_me_" + Guid.NewGuid().ToString("N")[..6];
        await RegisterAsync(client, meName);

        var otherName = "hide_other_" + Guid.NewGuid().ToString("N")[..6];
        var fpJson = new FingerprintGenerator(128).Generate(new[] { "y:1", "y:2", "y:3" }).ToJson();
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var me = await db.Users.FirstAsync(u => u.Username == meName);
            var other = new AppUser { Username = otherName, PasswordHash = "x", IsDiscoverable = true };
            db.Users.Add(other);
            await db.SaveChangesAsync();
            db.Fingerprints.Add(new FingerprintRecord { UserId = me.Id, FingerprintJson = fpJson, SourcesJson = "[]" });
            db.Fingerprints.Add(new FingerprintRecord { UserId = other.Id, FingerprintJson = fpJson, SourcesJson = "[]" });
            await db.SaveChangesAsync();
        }

        Assert.Contains(otherName, await (await client.GetAsync("/matches")).Content.ReadAsStringAsync());

        var hide = await PostFormAsync(client, "/matches", "/matches/hide", new() { ["username"] = otherName });
        Assert.Equal(HttpStatusCode.Redirect, hide.StatusCode);
        Assert.DoesNotContain(otherName, await (await client.GetAsync("/matches")).Content.ReadAsStringAsync());

        var unhide = await PostFormAsync(client, "/matches/hidden", "/matches/unhide", new() { ["username"] = otherName });
        Assert.Equal(HttpStatusCode.Redirect, unhide.StatusCode);
        Assert.Contains(otherName, await (await client.GetAsync("/matches")).Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task DataPage_And_JsonExport_ShowStoredData()
    {
        var client = NewClient();
        var user = "data_" + Guid.NewGuid().ToString("N")[..8];
        await RegisterAsync(client, user);

        var page = await client.GetAsync("/account/data");
        Assert.Equal(HttpStatusCode.OK, page.StatusCode);
        var html = await page.Content.ReadAsStringAsync();
        Assert.Contains(user, html);
        Assert.Contains("What we never store", html);

        var json = await client.GetAsync("/account/data.json");
        Assert.Equal(HttpStatusCode.OK, json.StatusCode);
        Assert.Equal("application/json", json.Content.Headers.ContentType!.MediaType);
        var body = await json.Content.ReadAsStringAsync();
        Assert.Contains(user, body);
        Assert.Contains("\"RawInterestsStored\": false", body);
    }

    [Fact]
    public async Task Discoverability_Off_HidesUserFromOthersMatches()
    {
        var client = NewClient();
        var meName = "vis_me_" + Guid.NewGuid().ToString("N")[..6];
        await RegisterAsync(client, meName);

        var otherName = "vis_other_" + Guid.NewGuid().ToString("N")[..6];
        var fpJson = new FingerprintGenerator(128).Generate(new[] { "x:1", "x:2", "x:3" }).ToJson();

        // Seed a second user who matches "me" 100% (identical signature).
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var me = await db.Users.FirstAsync(u => u.Username == meName);
            var other = new AppUser { Username = otherName, PasswordHash = "x", IsDiscoverable = true };
            db.Users.Add(other);
            await db.SaveChangesAsync();
            db.Fingerprints.Add(new FingerprintRecord { UserId = me.Id, FingerprintJson = fpJson, SourcesJson = "[\"GitHub\"]" });
            db.Fingerprints.Add(new FingerprintRecord { UserId = other.Id, FingerprintJson = fpJson, SourcesJson = "[\"GitHub\"]" });
            await db.SaveChangesAsync();
        }

        var visible = await client.GetAsync("/matches");
        Assert.Contains(otherName, await visible.Content.ReadAsStringAsync());

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var other = await db.Users.FirstAsync(u => u.Username == otherName);
            other.IsDiscoverable = false;
            await db.SaveChangesAsync();
        }

        var hidden = await client.GetAsync("/matches");
        Assert.DoesNotContain(otherName, await hidden.Content.ReadAsStringAsync());
    }

    /// <summary>
    /// The toggle used to work one way only: an account could stay permanently invisible while
    /// reading everyone's contact line, and could never be hidden in return, because hiding someone
    /// requires seeing their card first.
    /// </summary>
    [Fact]
    public async Task Discoverability_Off_AlsoHidesOtherPeoplesContactDetailsFromYou()
    {
        var client = NewClient();
        var meName = "mirror_me_" + Guid.NewGuid().ToString("N")[..6];
        await RegisterAsync(client, meName);

        var otherName = "mirror_other_" + Guid.NewGuid().ToString("N")[..6];
        const string contact = "mastodon-zzqx-4417";
        var fpJson = new FingerprintGenerator(128).Generate(new[] { "m:1", "m:2", "m:3" }).ToJson();

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var me = await db.Users.FirstAsync(u => u.Username == meName);
            var other = new AppUser
            {
                Username = otherName, PasswordHash = "x", IsDiscoverable = true, Contact = contact
            };
            db.Users.Add(other);
            await db.SaveChangesAsync();
            db.Fingerprints.Add(new FingerprintRecord { UserId = me.Id, FingerprintJson = fpJson, SourcesJson = "[\"GitHub\"]" });
            db.Fingerprints.Add(new FingerprintRecord { UserId = other.Id, FingerprintJson = fpJson, SourcesJson = "[\"GitHub\"]" });
            await db.SaveChangesAsync();
        }

        Assert.Contains(contact, await (await client.GetAsync("/matches")).Content.ReadAsStringAsync());

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var me = await db.Users.FirstAsync(u => u.Username == meName);
            me.IsDiscoverable = false;
            await db.SaveChangesAsync();
        }

        var whileHidden = await (await client.GetAsync("/matches")).Content.ReadAsStringAsync();
        // Still matched -- similarity is not the personal part -- but the contact line is withheld.
        Assert.Contains(otherName, whileHidden);
        Assert.DoesNotContain(contact, whileHidden);
    }

    [Fact]
    public async Task Register_UsernameUniqueness_IsCaseInsensitive()
    {
        var baseName = "Case" + Guid.NewGuid().ToString("N")[..8];
        var first = await RegisterAsync(NewClient(), baseName);
        Assert.Equal(HttpStatusCode.Redirect, first.StatusCode);

        // Registering the same name in a different case must be rejected.
        var second = await RegisterAsync(NewClient(), baseName.ToUpperInvariant());
        Assert.Equal(HttpStatusCode.OK, second.StatusCode);
        Assert.Contains("Username already taken", await second.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Login_IsCaseInsensitive_ForUsername()
    {
        var name = "Mixed" + Guid.NewGuid().ToString("N")[..8];
        await RegisterAsync(NewClient(), name, "Tr0ubad0ur-x9");

        var login = await PostFormAsync(NewClient(), "/account/login", "/account/login", new()
        {
            ["Username"] = name.ToLowerInvariant(),
            ["Password"] = "Tr0ubad0ur-x9"
        });

        Assert.Equal(HttpStatusCode.Redirect, login.StatusCode);
        Assert.Contains("/sources", login.Headers.Location!.ToString());
    }

    [Fact]
    public async Task ChangePassword_WrongCurrent_IsRejected()
    {
        var client = NewClient();
        var user = "pw_" + Guid.NewGuid().ToString("N")[..8];
        await RegisterAsync(client, user);

        var resp = await PostFormAsync(client, "/account/password", "/account/password", new()
        {
            ["CurrentPassword"] = "not-the-password",
            ["NewPassword"] = "brandnew123",
            ["ConfirmNewPassword"] = "brandnew123"
        });

        Assert.Equal(HttpStatusCode.OK, resp.StatusCode); // redisplays form with error
        Assert.Contains("Current password is incorrect", await resp.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task ChangePassword_UpdatesCredential_OldFailsNewWorks()
    {
        var client = NewClient();
        var user = "pw2_" + Guid.NewGuid().ToString("N")[..8];
        await RegisterAsync(client, user, "Tr0ubad0ur-x9");

        var change = await PostFormAsync(client, "/account/password", "/account/password", new()
        {
            ["CurrentPassword"] = "Tr0ubad0ur-x9",
            ["NewPassword"] = "newsecret456",
            ["ConfirmNewPassword"] = "newsecret456"
        });
        Assert.Equal(HttpStatusCode.Redirect, change.StatusCode);

        // Old password no longer works.
        var oldLogin = await PostFormAsync(NewClient(), "/account/login", "/account/login", new()
        {
            ["Username"] = user,
            ["Password"] = "Tr0ubad0ur-x9"
        });
        Assert.Equal(HttpStatusCode.OK, oldLogin.StatusCode);
        Assert.Contains("Invalid username or password", await oldLogin.Content.ReadAsStringAsync());

        // New password works.
        var newLogin = await PostFormAsync(NewClient(), "/account/login", "/account/login", new()
        {
            ["Username"] = user,
            ["Password"] = "newsecret456"
        });
        Assert.Equal(HttpStatusCode.Redirect, newLogin.StatusCode);
        Assert.Contains("/sources", newLogin.Headers.Location!.ToString());
    }

    [Fact]
    public async Task AccountDeletion_RemovesUser_AndDeauthenticates()
    {
        var client = NewClient();
        var user = "del_" + Guid.NewGuid().ToString("N")[..8];
        await RegisterAsync(client, user);

        // Give the account derived data, so deletion is shown to take it with it.
        int userId;
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            userId = (await db.Users.FirstAsync(u => u.Username == user)).Id;
            var raw = new FingerprintGenerator(128).GenerateRaw(new[] { "z:1", "z:2" });
            db.SourceFingerprints.Add(new SourceFingerprintRecord
            {
                UserId = userId, Source = "GitHub", FeatureCount = 2,
                RawSignatureJson = System.Text.Json.JsonSerializer.Serialize(raw)
            });
            db.Fingerprints.Add(new FingerprintRecord
            {
                UserId = userId,
                FingerprintJson = FingerprintGenerator.FromRaw(raw).ToJson(),
                SourcesJson = "[\"GitHub\"]"
            });
            await db.SaveChangesAsync();
        }

        var del = await PostFormAsync(client, "/sources/dashboard", "/account/delete", new()
        {
            ["password"] = "Tr0ubad0ur-x9"
        });
        Assert.Equal(HttpStatusCode.Redirect, del.StatusCode);

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            Assert.False(await db.Users.AnyAsync(u => u.Username == user));
            Assert.False(await db.Fingerprints.AnyAsync(f => f.UserId == userId));
            Assert.False(await db.SourceFingerprints.AnyAsync(s => s.UserId == userId));
        }

        var dash = await client.GetAsync("/sources/dashboard");
        Assert.Equal(HttpStatusCode.Redirect, dash.StatusCode);
        Assert.Contains("/account/login", dash.Headers.Location!.ToString());
    }

    [Fact]
    public async Task StaleCookie_ForDeletedUser_IsRejected()
    {
        var client = NewClient();
        var user = "ghost_" + Guid.NewGuid().ToString("N")[..8];
        await RegisterAsync(client, user);

        // Delete the row out-of-band (bypassing the app's sign-out) — the cookie is now stale.
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var row = await db.Users.FirstAsync(u => u.Username == user);
            db.Users.Remove(row);
            await db.SaveChangesAsync();
        }

        var dash = await client.GetAsync("/sources/dashboard");
        Assert.Equal(HttpStatusCode.Redirect, dash.StatusCode);
        Assert.Contains("/account/login", dash.Headers.Location!.ToString());
    }
}
