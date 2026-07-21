using Microsoft.Extensions.Configuration;
using Profiler.Web.Security;
using Xunit;

namespace Profiler.Web.Tests;

/// <summary>
/// Getting this wrong is silent: the app would run perfectly while the promise it is built on was
/// untrue, so the failure has to happen at startup rather than never.
/// </summary>
public class FingerprintPepperTests
{
    private static IConfiguration Config(string? pepper) =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(pepper == null
                ? Array.Empty<KeyValuePair<string, string?>>()
                : new[] { new KeyValuePair<string, string?>(FingerprintPepper.ConfigKey, pepper) })
            .Build();

    [Fact]
    public void Deployment_WithoutAPepper_RefusesToStart()
    {
        var error = Assert.Throws<InvalidOperationException>(
            () => FingerprintPepper.Resolve(Config(null), isDevelopment: false));

        // The message has to say what goes wrong, or an operator will treat it as a nuisance.
        Assert.Contains("recover real ones", error.Message);
    }

    [Fact]
    public void Deployment_ReusingThePublishedDevelopmentValue_RefusesToStart()
    {
        Assert.Throws<InvalidOperationException>(
            () => FingerprintPepper.Resolve(Config(FingerprintPepper.DevelopmentPepper), isDevelopment: false));
    }

    [Fact]
    public void Deployment_WithAPepper_UsesIt()
    {
        Assert.Equal("s3cret", FingerprintPepper.Resolve(Config("s3cret"), isDevelopment: false));
    }

    [Fact]
    public void Development_FallsBackToAFixedValue_SoLocalFingerprintsSurviveRestarts()
    {
        Assert.Equal(FingerprintPepper.DevelopmentPepper, FingerprintPepper.Resolve(Config(null), isDevelopment: true));
        Assert.Equal(FingerprintPepper.DevelopmentPepper,
            FingerprintPepper.Resolve(Config(FingerprintPepper.DevelopmentPepper), isDevelopment: true));
    }

    [Fact]
    public void Blank_IsTreatedAsUnset_RatherThanAsAnEmptySecret()
    {
        Assert.Throws<InvalidOperationException>(
            () => FingerprintPepper.Resolve(Config("   "), isDevelopment: false));
    }
}
