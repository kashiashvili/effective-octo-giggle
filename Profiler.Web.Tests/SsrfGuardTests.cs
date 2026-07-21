using Profiler.Web.Connectors;
using Xunit;

namespace Profiler.Web.Tests;

public class SsrfGuardTests
{
    [Theory]
    [InlineData("http://127.0.0.1/feed")]        // loopback
    [InlineData("http://169.254.169.254/latest")] // cloud metadata
    [InlineData("http://10.0.0.5/feed")]          // private
    [InlineData("http://192.168.1.1/feed")]       // private
    [InlineData("http://172.16.0.1/feed")]        // private
    [InlineData("http://100.64.0.1/feed")]        // CGNAT
    [InlineData("http://[::1]/feed")]             // IPv6 loopback
    public async Task Blocks_InternalAddresses(string url)
    {
        Assert.False(await SsrfGuard.IsAllowedAsync(new Uri(url)));
    }

    [Theory]
    [InlineData("http://8.8.8.8/feed")]
    [InlineData("https://1.1.1.1/rss")]
    public async Task Allows_PublicAddresses(string url)
    {
        Assert.True(await SsrfGuard.IsAllowedAsync(new Uri(url)));
    }

    [Theory]
    [InlineData("127.0.0.1")]
    [InlineData("169.254.169.254")]
    [InlineData("10.1.2.3")]
    [InlineData("::1")]
    public async Task ResolveAllowed_Throws_ForInternalHosts(string host)
    {
        var ex = await Assert.ThrowsAsync<IOException>(() => SsrfGuard.ResolveAllowedAsync(host));
        Assert.Contains("non-public", ex.Message);
    }

    [Fact]
    public async Task ResolveAllowed_ReturnsTheAddressesThatWillBeConnectedTo()
    {
        var addresses = await SsrfGuard.ResolveAllowedAsync("8.8.8.8");

        // Callers connect to exactly these, which is what removes the rebinding window.
        Assert.Equal(new[] { System.Net.IPAddress.Parse("8.8.8.8") }, addresses);
    }
}
