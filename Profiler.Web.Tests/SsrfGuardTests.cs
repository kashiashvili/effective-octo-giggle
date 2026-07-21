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
}
