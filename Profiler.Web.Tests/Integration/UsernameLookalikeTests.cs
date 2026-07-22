using System.Net;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Profiler.Web.Data;
using Xunit;

namespace Profiler.Web.Tests.Integration;

/// <summary>
/// The username is the only identity a stranger has before deciding to make contact. Invisible and
/// mixed-alphabet lookalikes are already refused; this covers the last easy one — a name that reads
/// as an existing account once case and compatibility differences (which SQLite's NOCASE does not
/// fold beyond ASCII) are set aside.
/// </summary>
public class UsernameLookalikeTests : IClassFixture<ProfilerWebFactory>
{
    private readonly ProfilerWebFactory _factory;

    public UsernameLookalikeTests(ProfilerWebFactory factory) => _factory = factory;

    private HttpClient NewClient() => _factory.CreateClient(new WebApplicationFactoryClientOptions
    {
        AllowAutoRedirect = false
    });

    private static async Task<HttpResponseMessage> RegisterAsync(HttpClient client, string username)
    {
        var page = await client.GetAsync("/account/register");
        page.EnsureSuccessStatusCode();
        var token = Regex.Match(await page.Content.ReadAsStringAsync(),
            "name=\"__RequestVerificationToken\"[^>]*value=\"([^\"]+)\"").Groups[1].Value;
        return await client.PostAsync("/account/register", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["Username"] = username,
            ["Password"] = "Tr0ubad0ur-x9",
            ["ConfirmPassword"] = "Tr0ubad0ur-x9",
            ["__RequestVerificationToken"] = token
        }));
    }

    [Fact]
    public async Task ANameThatFoldsToAnExistingOne_IsRefused()
    {
        var stem = Guid.NewGuid().ToString("N")[..6];
        var first = "André" + stem;
        var lookalike = "ANDRÉ" + stem.ToUpperInvariant();

        var registered = await RegisterAsync(NewClient(), first);
        Assert.Equal(HttpStatusCode.Redirect, registered.StatusCode); // succeeded

        var refused = await RegisterAsync(NewClient(), lookalike);
        Assert.Equal(HttpStatusCode.OK, refused.StatusCode); // form redisplayed, not a redirect
        Assert.Contains("too close to an existing one", await refused.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task AGenuinelyDifferentName_IsAccepted()
    {
        var stem = Guid.NewGuid().ToString("N")[..6];
        Assert.Equal(HttpStatusCode.Redirect, (await RegisterAsync(NewClient(), "André" + stem)).StatusCode);
        // Same stem, but no accent — a different name, and allowed.
        Assert.Equal(HttpStatusCode.Redirect, (await RegisterAsync(NewClient(), "Andre" + stem)).StatusCode);
    }

    [Fact]
    public async Task RegistrationStoresTheNormalizedForm()
    {
        var username = "Zoë" + Guid.NewGuid().ToString("N")[..6];
        Assert.Equal(HttpStatusCode.Redirect, (await RegisterAsync(NewClient(), username)).StatusCode);

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var stored = await db.Users.AsNoTracking().FirstAsync(u => u.Username == username);

        Assert.Equal(Profiler.Web.Security.TextPolicy.NormalizeForUniqueness(username), stored.NormalizedUsername);
        Assert.NotEqual(username, stored.NormalizedUsername); // display casing is preserved separately
    }
}
