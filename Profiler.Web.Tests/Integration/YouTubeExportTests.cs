using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace Profiler.Web.Tests.Integration;

/// <summary>
/// The no-token YouTube path end to end: two people upload their Takeout subscriptions, share
/// channels, and find each other with "YouTube" as the shared source — no token entered anywhere.
/// </summary>
public class YouTubeExportTests : IClassFixture<ProfilerWebFactory>
{
    private readonly ProfilerWebFactory _factory;
    public YouTubeExportTests(ProfilerWebFactory factory) => _factory = factory;

    private HttpClient NewClient() => _factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

    private static string TokenIn(string html) =>
        Regex.Match(html, "name=\"__RequestVerificationToken\"[^>]*value=\"([^\"]+)\"").Groups[1].Value;

    private static async Task RegisterAsync(HttpClient client, string username)
    {
        var page = await client.GetAsync("/account/register");
        var token = TokenIn(await page.Content.ReadAsStringAsync());
        var resp = await client.PostAsync("/account/register", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["Username"] = username,
            ["Password"] = "Tr0ubad0ur-x9",
            ["ConfirmPassword"] = "Tr0ubad0ur-x9",
            ["__RequestVerificationToken"] = token
        }));
        Assert.Equal(HttpStatusCode.Redirect, resp.StatusCode);
    }

    private static async Task<HttpResponseMessage> UploadSubscriptionsAsync(HttpClient client, string csv)
    {
        var page = await client.GetAsync("/sources/connect");
        var token = TokenIn(await page.Content.ReadAsStringAsync());
        using var form = new MultipartFormDataContent();
        form.Add(new StringContent(token), "__RequestVerificationToken");
        var file = new ByteArrayContent(Encoding.UTF8.GetPreamble().Concat(Encoding.UTF8.GetBytes(csv)).ToArray());
        file.Headers.ContentType = new MediaTypeHeaderValue("text/csv");
        form.Add(file, "YouTubeSubscriptionsCsv", "subscriptions.csv");
        return await client.PostAsync("/sources/connect", form);
    }

    private const string Header = "Channel Id,Channel Url,Channel Title\n";

    [Fact]
    public async Task TwoTakeoutExports_WithSharedChannels_MatchOnYouTube_WithoutAnyToken()
    {
        var tag = Guid.NewGuid().ToString("N")[..6];
        var alice = "yt_a_" + tag;
        var bob = "yt_b_" + tag;
        // Same eight channels, so the pair is a strong match; the point is the path, not the tier.
        var channels = Enumerable.Range(1, 8).Select(i => $"UC{tag}{i},https://www.youtube.com/channel/UC{i},Channel {tag} {i}");
        var csv = Header + string.Join("\n", channels) + "\n";

        var a = NewClient();
        await RegisterAsync(a, alice);
        var uploaded = await UploadSubscriptionsAsync(a, csv);
        Assert.True(uploaded.StatusCode is HttpStatusCode.Redirect or HttpStatusCode.OK, uploaded.StatusCode.ToString());
        var dashboard = await a.GetStringAsync("/sources/dashboard");
        Assert.Contains("YouTube", dashboard);

        var b = NewClient();
        await RegisterAsync(b, bob);
        await UploadSubscriptionsAsync(b, csv);

        var matches = await a.GetStringAsync("/matches");
        Assert.Contains(bob, matches);
        Assert.Contains("YouTube", matches);
        // The raw channel titles never reach a page: only the derived signature exists.
        Assert.DoesNotContain($"Channel {tag} 1", matches);
        Assert.DoesNotContain($"Channel {tag} 1", dashboard);
    }
}
