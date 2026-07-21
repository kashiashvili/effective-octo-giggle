using System.Net;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Profiler.Web.Data;
using Xunit;

namespace Profiler.Web.Tests.Integration;

/// <summary>
/// Boots the real app with only the "register" limiter left tight (everything else stays
/// generously raised, same as <see cref="ProfilerWebFactory"/>), so a test can actually trip
/// the registration policy without waiting on the hourly window it uses in production.
/// </summary>
public class LowRegisterLimitWebFactory : WebApplicationFactory<Program>
{
    private readonly string _dbPath = Path.Combine(Path.GetTempPath(), $"profiler-test-{Guid.NewGuid():N}.db");

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment(Environments.Development);
        builder.UseSetting("RateLimiting:LoginPermitLimit", "100000");
        builder.UseSetting("RateLimiting:RegisterPermitLimit", "2");
        builder.UseSetting("RateLimiting:ConnectPermitLimit", "100000");

        builder.ConfigureServices(services =>
        {
            var descriptor = services.SingleOrDefault(d => d.ServiceType == typeof(DbContextOptions<AppDbContext>));
            if (descriptor != null) services.Remove(descriptor);

            services.AddDbContext<AppDbContext>(o => o.UseSqlite($"Data Source={_dbPath}"));
        });
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        if (disposing)
        {
            try { if (File.Exists(_dbPath)) File.Delete(_dbPath); } catch { /* best effort */ }
        }
    }
}

public class RateLimitingTests : IClassFixture<LowRegisterLimitWebFactory>
{
    private readonly LowRegisterLimitWebFactory _factory;

    public RateLimitingTests(LowRegisterLimitWebFactory factory) => _factory = factory;

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

    [Fact]
    public async Task Register_RateLimited_AllowsEarlyAttempt_ThenRejectsBurst()
    {
        // The limiter partitions by IP, not by cookie/session, so a burst from freshly-created
        // (unauthenticated) clients on the same test-server IP still trips it — the same shape
        // a sybil-account flood would take. A fresh client per attempt is required here: reusing
        // one would auto-authenticate after the first success, and the "register" GET redirects
        // an authenticated caller away before it ever reaches the POST.
        var first = await RegisterAsync(NewClient(), "burst_" + Guid.NewGuid().ToString("N")[..8]);
        Assert.Equal(HttpStatusCode.Redirect, first.StatusCode);

        HttpResponseMessage? limited = null;
        for (var i = 0; i < 5; i++)
        {
            limited = await RegisterAsync(NewClient(), "burst_" + Guid.NewGuid().ToString("N")[..8]);
            if (limited.StatusCode == HttpStatusCode.TooManyRequests) break;
        }

        Assert.Equal(HttpStatusCode.TooManyRequests, limited!.StatusCode);
        Assert.Equal("text/html", limited.Content.Headers.ContentType!.MediaType);
        var html = await limited.Content.ReadAsStringAsync();
        // The page is shared with login but the wording is not: someone who was rate-limited while
        // signing up must not be told their sign-in attempts were paused.
        Assert.Contains("Too many sign-up attempts", html);
        Assert.Contains("/account/register", html);
        Assert.Contains("/css/style.css", html); // still the same styled 429 page
    }
}
