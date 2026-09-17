using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace Profiler.Web.Tests.Integration;

/// <summary>
/// The landing page is the first thing the target user reads. It must lead with the path that needs
/// no accounts and no tokens (the funnel that actually converts), say honestly which sources need a
/// self-made token, and promise nothing that does not exist.
/// </summary>
public class LandingPageTests : IClassFixture<ProfilerWebFactory>
{
    private readonly ProfilerWebFactory _factory;
    public LandingPageTests(ProfilerWebFactory factory) => _factory = factory;

    [Fact]
    public async Task Landing_LeadsWithTheNoAccountsPath_AndPromisesNothingUnbuilt()
    {
        var client = _factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        var html = await client.GetStringAsync("/");

        // The hero and step 1 name the self-described path before any platform.
        var hero = html.IndexOf("Pick your interests from a list", StringComparison.Ordinal);
        var firstPlatform = html.IndexOf("GitHub", StringComparison.Ordinal);
        Assert.True(hero >= 0, "hero must offer the interests list");
        Assert.True(hero < firstPlatform, "the interests path must come before any platform is named");
        Assert.Contains("1. Describe or connect", html);

        // Honest about what needs a token, and no unbacked "coming soon" promises.
        Assert.Contains("No account or token needed", html);
        Assert.Contains("API token you create yourself", html);
        Assert.DoesNotContain("coming soon", html, StringComparison.OrdinalIgnoreCase);
    }
}
