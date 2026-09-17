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

        // The hero names the self-described path and no platform; step 1 leads with it too.
        var heroSection = html[..html.IndexOf("</section>", StringComparison.Ordinal)];
        Assert.Contains("Pick your interests from a list", heroSection);
        Assert.DoesNotContain("GitHub", heroSection);
        var step1 = html[html.IndexOf("1. Describe or connect", StringComparison.Ordinal)..];
        step1 = step1[..step1.IndexOf("</p>", StringComparison.Ordinal)];
        Assert.True(step1.IndexOf("Pick what", StringComparison.Ordinal) < step1.IndexOf("GitHub", StringComparison.Ordinal),
            "step 1 must name the interests list before any platform");

        // Honest about what needs a token, and no unbacked "coming soon" promises.
        Assert.Contains("No account or token needed", html);
        Assert.Contains("API token you create yourself", html);
        Assert.DoesNotContain("coming soon", html, StringComparison.OrdinalIgnoreCase);
    }
}
