using Microsoft.Extensions.Configuration;
using Profiler.Web.Security;
using Xunit;

namespace Profiler.Web.Tests;

/// <summary>
/// Forwarded headers decide what address the rate limiters partition on, so the wrong answer here is
/// either a site-wide throttle (every visitor sharing the proxy's bucket) or no throttle at all
/// (anyone forging a fresh client address per request).
/// </summary>
public class ProxyTrustTests
{
    private static IConfiguration Config(params (string Key, string Value)[] settings) =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(settings.Select(s => new KeyValuePair<string, string?>(s.Key, s.Value)))
            .Build();

    [Fact]
    public void Disabled_ByDefault()
    {
        Assert.Null(ProxyTrust.Build(Config()));
    }

    [Fact]
    public void Disabled_EvenWhenProxiesAreNamed_UnlessSwitchedOn()
    {
        Assert.Null(ProxyTrust.Build(Config((ProxyTrust.ProxiesKey, "10.0.0.7"))));
    }

    [Fact]
    public void EnabledWithoutAnythingTrusted_IsRefusedAtStartup()
    {
        var error = Assert.Throws<InvalidOperationException>(
            () => ProxyTrust.Build(Config((ProxyTrust.EnabledKey, "true"))));

        // The message has to explain the consequence, or an operator will just delete the setting.
        Assert.Contains("forge their client address", error.Message);
    }

    [Fact]
    public void TrustsExactlyTheNamedProxies_AndNotTheLoopbackDefaults()
    {
        var options = ProxyTrust.Build(Config(
            (ProxyTrust.EnabledKey, "true"),
            (ProxyTrust.ProxiesKey, "10.0.0.7, 10.0.0.8")));

        Assert.NotNull(options);
        Assert.Equal(2, options!.KnownProxies.Count);
        Assert.Contains(options.KnownProxies, p => p.ToString() == "10.0.0.7");
        Assert.Empty(options.KnownNetworks);
    }

    [Fact]
    public void AcceptsNetworksInCidrNotation()
    {
        var options = ProxyTrust.Build(Config(
            (ProxyTrust.EnabledKey, "true"),
            (ProxyTrust.NetworksKey, "10.0.0.0/8")));

        var network = Assert.Single(options!.KnownNetworks);
        Assert.Equal(8, network.PrefixLength);
    }

    [Theory]
    [InlineData(ProxyTrust.ProxiesKey, "not-an-ip")]
    [InlineData(ProxyTrust.NetworksKey, "10.0.0.0")]      // missing prefix length
    [InlineData(ProxyTrust.NetworksKey, "10.0.0.0/wide")]
    public void MalformedTrustList_FailsLoudlyRatherThanSilentlyTrustingNothing(string key, string value)
    {
        Assert.Throws<InvalidOperationException>(
            () => ProxyTrust.Build(Config((ProxyTrust.EnabledKey, "true"), (key, value))));
    }

    [Fact]
    public void ReadsBothTheForwardedForAndForwardedProtoHeaders()
    {
        var options = ProxyTrust.Build(Config(
            (ProxyTrust.EnabledKey, "true"),
            (ProxyTrust.ProxiesKey, "10.0.0.7")));

        Assert.True(options!.ForwardedHeaders.HasFlag(Microsoft.AspNetCore.HttpOverrides.ForwardedHeaders.XForwardedFor));
        Assert.True(options.ForwardedHeaders.HasFlag(Microsoft.AspNetCore.HttpOverrides.ForwardedHeaders.XForwardedProto));
    }
}
