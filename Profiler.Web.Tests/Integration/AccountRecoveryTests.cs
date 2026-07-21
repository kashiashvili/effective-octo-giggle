using System.Net;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Profiler.Web.Data;
using Xunit;

namespace Profiler.Web.Tests.Integration;

/// <summary>
/// Recovery is the whole answer to "I forgot my password" for a product that deliberately holds no
/// email address. Without it a locked-out account can never be deleted either, since deletion is
/// password-confirmed — so the fingerprint would sit in everyone else's match pool forever.
/// </summary>
public class AccountRecoveryTests : IClassFixture<ProfilerWebFactory>
{
    private readonly ProfilerWebFactory _factory;

    public AccountRecoveryTests(ProfilerWebFactory factory) => _factory = factory;

    private HttpClient NewClient() => _factory.CreateClient(new WebApplicationFactoryClientOptions
    {
        AllowAutoRedirect = false
    });

    private static async Task<string> TokenAsync(HttpResponseMessage resp)
    {
        var m = Regex.Match(await resp.Content.ReadAsStringAsync(),
            "name=\"__RequestVerificationToken\"[^>]*value=\"([^\"]+)\"");
        Assert.True(m.Success, "Antiforgery token not found on page");
        return m.Groups[1].Value;
    }

    private static async Task<HttpResponseMessage> PostFormAsync(
        HttpClient client, string getUrl, string postUrl, Dictionary<string, string> fields)
    {
        var page = await client.GetAsync(getUrl);
        page.EnsureSuccessStatusCode();
        fields["__RequestVerificationToken"] = await TokenAsync(page);
        return await client.PostAsync(postUrl, new FormUrlEncodedContent(fields));
    }

    private static string NewUsername() => "rec_" + Guid.NewGuid().ToString("N")[..8];

    /// <summary>Registers, follows the redirect to the one-time page, and returns the code shown.</summary>
    private async Task<string> RegisterAndReadCodeAsync(HttpClient client, string username, string password)
    {
        var registered = await PostFormAsync(client, "/account/register", "/account/register", new()
        {
            ["Username"] = username,
            ["Password"] = password,
            ["ConfirmPassword"] = password
        });
        Assert.Equal(HttpStatusCode.Redirect, registered.StatusCode);
        Assert.Equal("/account/recovery-code", registered.Headers.Location!.OriginalString);

        var page = await client.GetAsync("/account/recovery-code");
        page.EnsureSuccessStatusCode();
        var html = await page.Content.ReadAsStringAsync();
        var code = Regex.Match(html, @"<div class=""recovery-code"">([A-Z0-9-]+)</div>").Groups[1].Value;
        Assert.False(string.IsNullOrWhiteSpace(code), "No recovery code was shown after registering");
        return code;
    }

    [Fact]
    public async Task RegisteringIssuesACode_ThatIsShownOnceAndNeverAgain()
    {
        var client = NewClient();
        var code = await RegisterAndReadCodeAsync(client, NewUsername(), "Tr0ubad0ur-x9");

        // A reload must not show it again: only a hash was kept, so pretending otherwise would be
        // a lie the user might rely on.
        var again = await client.GetAsync("/account/recovery-code");
        Assert.Equal(HttpStatusCode.Redirect, again.StatusCode);
        Assert.Contains("/sources/dashboard", again.Headers.Location!.OriginalString);

        // And the code itself is not what is stored.
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var stored = await db.Users.AsNoTracking().Select(u => u.RecoveryCodeHash).ToListAsync();
        Assert.DoesNotContain(stored, h => h != null && h.Contains(code.Replace("-", "")));
    }

    [Fact]
    public async Task TheCodeResetsThePassword_AndTheOldPasswordStopsWorking()
    {
        var username = NewUsername();
        const string oldPassword = "Tr0ubad0ur-x9";
        const string newPassword = "Zephyr-Quilt-42";

        var code = await RegisterAndReadCodeAsync(NewClient(), username, oldPassword);

        var recovering = NewClient();
        var reset = await PostFormAsync(recovering, "/account/recover", "/account/recover", new()
        {
            ["Username"] = username,
            ["Code"] = code,
            ["NewPassword"] = newPassword,
            ["ConfirmNewPassword"] = newPassword
        });
        Assert.Equal(HttpStatusCode.Redirect, reset.StatusCode);

        var loggingIn = NewClient();
        var withOld = await PostFormAsync(loggingIn, "/account/login", "/account/login", new()
        {
            ["Username"] = username,
            ["Password"] = oldPassword
        });
        Assert.Equal(HttpStatusCode.OK, withOld.StatusCode); // form redisplayed = rejected

        var withNew = await PostFormAsync(NewClient(), "/account/login", "/account/login", new()
        {
            ["Username"] = username,
            ["Password"] = newPassword
        });
        Assert.Equal(HttpStatusCode.Redirect, withNew.StatusCode);
    }

    [Fact]
    public async Task AUsedCodeIsDead_AndAReplacementIsIssuedInItsPlace()
    {
        var username = NewUsername();
        var code = await RegisterAndReadCodeAsync(NewClient(), username, "Tr0ubad0ur-x9");

        var first = NewClient();
        var reset = await PostFormAsync(first, "/account/recover", "/account/recover", new()
        {
            ["Username"] = username,
            ["Code"] = code,
            ["NewPassword"] = "Zephyr-Quilt-42",
            ["ConfirmNewPassword"] = "Zephyr-Quilt-42"
        });
        Assert.Equal("/account/recovery-code", reset.Headers.Location!.OriginalString);

        // Recovering must not leave the account with no way back next time.
        var replacementPage = await first.GetAsync("/account/recovery-code");
        var replacement = Regex.Match(await replacementPage.Content.ReadAsStringAsync(),
            @"<div class=""recovery-code"">([A-Z0-9-]+)</div>").Groups[1].Value;
        Assert.False(string.IsNullOrWhiteSpace(replacement));
        Assert.NotEqual(code, replacement);

        // The original is spent.
        var replay = await PostFormAsync(NewClient(), "/account/recover", "/account/recover", new()
        {
            ["Username"] = username,
            ["Code"] = code,
            ["NewPassword"] = "Another-Password-77",
            ["ConfirmNewPassword"] = "Another-Password-77"
        });
        Assert.Equal(HttpStatusCode.OK, replay.StatusCode);
        Assert.Contains("don&#x27;t match an account", await replay.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task AWrongCode_AndAnUnknownUsername_AreIndistinguishable()
    {
        var username = NewUsername();
        await RegisterAndReadCodeAsync(NewClient(), username, "Tr0ubad0ur-x9");

        async Task<string> AttemptAsync(string user) =>
            await (await PostFormAsync(NewClient(), "/account/recover", "/account/recover", new()
            {
                ["Username"] = user,
                ["Code"] = "4KM7Q-8ZTX2-4KM7Q-8ZTX2",
                ["NewPassword"] = "Zephyr-Quilt-42",
                ["ConfirmNewPassword"] = "Zephyr-Quilt-42"
            })).Content.ReadAsStringAsync();

        var wrongCode = await AttemptAsync(username);
        var noSuchUser = await AttemptAsync("nobody_" + Guid.NewGuid().ToString("N")[..8]);

        const string message = "don&#x27;t match an account";
        Assert.Contains(message, wrongCode);
        Assert.Contains(message, noSuchUser);
    }

    [Fact]
    public async Task RecoveryRefusesAWeakNewPassword()
    {
        var username = NewUsername();
        var code = await RegisterAndReadCodeAsync(NewClient(), username, "Tr0ubad0ur-x9");

        var resp = await PostFormAsync(NewClient(), "/account/recover", "/account/recover", new()
        {
            ["Username"] = username,
            ["Code"] = code,
            ["NewPassword"] = "password123",
            ["ConfirmNewPassword"] = "password123"
        });

        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        // Rejected on strength, not on the code — the code was valid.
        var html = await resp.Content.ReadAsStringAsync();
        Assert.DoesNotContain("match an account", html);
    }

    [Fact]
    public async Task RegeneratingRequiresTheCurrentPassword()
    {
        var username = NewUsername();
        const string password = "Tr0ubad0ur-x9";
        var client = NewClient();
        var original = await RegisterAndReadCodeAsync(client, username, password);

        var refused = await PostFormAsync(client, "/sources/dashboard", "/account/recovery-code", new()
        {
            ["password"] = "not-the-password"
        });
        Assert.Equal(HttpStatusCode.Redirect, refused.StatusCode);
        var afterRefusal = await client.GetAsync("/account/recovery-code");
        Assert.Equal(HttpStatusCode.Redirect, afterRefusal.StatusCode); // nothing was issued

        var accepted = await PostFormAsync(client, "/sources/dashboard", "/account/recovery-code", new()
        {
            ["password"] = password
        });
        Assert.Equal("/account/recovery-code", accepted.Headers.Location!.OriginalString);

        var page = await client.GetAsync("/account/recovery-code");
        var replacement = Regex.Match(await page.Content.ReadAsStringAsync(),
            @"<div class=""recovery-code"">([A-Z0-9-]+)</div>").Groups[1].Value;
        Assert.NotEqual(original, replacement);
    }

    [Fact]
    public async Task RecoveryIsReachableFromTheSignInPage()
    {
        var html = await (await NewClient().GetAsync("/account/login")).Content.ReadAsStringAsync();
        Assert.Contains("/account/recover", html);
    }
}
