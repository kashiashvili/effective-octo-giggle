using System.Net;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace Profiler.Web.Tests.Integration;

/// <summary>
/// Cookies are persistent for 30 days, so the remediation offered to someone whose password may be
/// known — change it — is worthless unless it also ends the sessions already out there.
/// </summary>
public class SessionInvalidationTests : IClassFixture<ProfilerWebFactory>
{
    private readonly ProfilerWebFactory _factory;

    public SessionInvalidationTests(ProfilerWebFactory factory) => _factory = factory;

    private HttpClient NewClient() => _factory.CreateClient(new WebApplicationFactoryClientOptions
    {
        AllowAutoRedirect = false
    });

    private static async Task<HttpResponseMessage> PostFormAsync(
        HttpClient client, string getUrl, string postUrl, Dictionary<string, string> fields)
    {
        var page = await client.GetAsync(getUrl);
        page.EnsureSuccessStatusCode();
        fields["__RequestVerificationToken"] = Regex.Match(
            await page.Content.ReadAsStringAsync(),
            "name=\"__RequestVerificationToken\"[^>]*value=\"([^\"]+)\"").Groups[1].Value;
        return await client.PostAsync(postUrl, new FormUrlEncodedContent(fields));
    }

    private static string NewUsername() => "sess_" + Guid.NewGuid().ToString("N")[..8];

    /// <summary>Registers on one client and signs the same account in on a second one.</summary>
    private async Task<(HttpClient Owner, HttpClient Other, string Username)> TwoSignedInSessionsAsync(string password)
    {
        var username = NewUsername();

        var owner = NewClient();
        await PostFormAsync(owner, "/account/register", "/account/register", new()
        {
            ["Username"] = username,
            ["Password"] = password,
            ["ConfirmPassword"] = password
        });

        var other = NewClient();
        var signIn = await PostFormAsync(other, "/account/login", "/account/login", new()
        {
            ["Username"] = username,
            ["Password"] = password
        });
        Assert.Equal(HttpStatusCode.Redirect, signIn.StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await other.GetAsync("/sources/dashboard")).StatusCode);

        return (owner, other, username);
    }

    private static async Task AssertSignedOutAsync(HttpClient client)
    {
        var resp = await client.GetAsync("/sources/dashboard");
        Assert.Equal(HttpStatusCode.Redirect, resp.StatusCode);
        Assert.Contains("/account/login", resp.Headers.Location!.ToString());
    }

    [Fact]
    public async Task ChangingThePassword_EndsOtherSessions_ButNotTheOneDoingIt()
    {
        const string password = "Tr0ubad0ur-x9";
        var (owner, other, _) = await TwoSignedInSessionsAsync(password);

        var changed = await PostFormAsync(owner, "/account/password", "/account/password", new()
        {
            ["CurrentPassword"] = password,
            ["NewPassword"] = "Zephyr-Quilt-42",
            ["ConfirmNewPassword"] = "Zephyr-Quilt-42"
        });
        Assert.Equal(HttpStatusCode.Redirect, changed.StatusCode);

        await AssertSignedOutAsync(other);
        Assert.Equal(HttpStatusCode.OK, (await owner.GetAsync("/sources/dashboard")).StatusCode);
    }

    [Fact]
    public async Task RecoveringTheAccount_EndsOtherSessions()
    {
        const string password = "Tr0ubad0ur-x9";
        var (owner, other, username) = await TwoSignedInSessionsAsync(password);

        var codePage = await owner.GetAsync("/account/recovery-code");
        var code = Regex.Match(await codePage.Content.ReadAsStringAsync(),
            @"<div class=""recovery-code"">([A-Z0-9-]+)</div>").Groups[1].Value;
        Assert.False(string.IsNullOrWhiteSpace(code));

        var recovering = NewClient();
        await PostFormAsync(recovering, "/account/recover", "/account/recover", new()
        {
            ["Username"] = username,
            ["Code"] = code,
            ["NewPassword"] = "Zephyr-Quilt-42",
            ["ConfirmNewPassword"] = "Zephyr-Quilt-42"
        });

        await AssertSignedOutAsync(other);
        await AssertSignedOutAsync(owner);
        Assert.Equal(HttpStatusCode.OK, (await recovering.GetAsync("/sources/dashboard")).StatusCode);
    }

    [Fact]
    public async Task SignOutEverywhere_EndsOtherSessions_WithoutChangingThePassword()
    {
        const string password = "Tr0ubad0ur-x9";
        var (owner, other, username) = await TwoSignedInSessionsAsync(password);

        var resp = await PostFormAsync(owner, "/sources/dashboard", "/account/sign-out-everywhere", new());
        Assert.Equal(HttpStatusCode.Redirect, resp.StatusCode);

        await AssertSignedOutAsync(other);
        Assert.Equal(HttpStatusCode.OK, (await owner.GetAsync("/sources/dashboard")).StatusCode);

        // The password was not the problem, so it still works.
        var signIn = await PostFormAsync(NewClient(), "/account/login", "/account/login", new()
        {
            ["Username"] = username,
            ["Password"] = password
        });
        Assert.Equal(HttpStatusCode.Redirect, signIn.StatusCode);
    }
}
