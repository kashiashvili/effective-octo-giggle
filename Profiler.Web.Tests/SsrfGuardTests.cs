using System.Net;
using Profiler.Web.Connectors;
using Xunit;

namespace Profiler.Web.Tests;

public class SsrfGuardTests
{
    /// <summary>
    /// IPv6 transition addresses are globally scoped, so the ordinary IPv6 checks wave them through —
    /// but on a host with a 6to4/NAT64/Teredo egress path the packet reaches the IPv4 address buried
    /// inside them. An attacker controls the RSS URL, so a name resolving to one of these is the
    /// same open door as submitting the internal IPv4 directly.
    /// </summary>
    [Theory]
    [InlineData("2002:7f00:0001::")]                 // 6to4 wrapping 127.0.0.1
    [InlineData("2002:a00:0005::")]                  // 6to4 wrapping 10.0.0.5
    [InlineData("2002:a9fe:a9fe::")]                 // 6to4 wrapping 169.254.169.254 (cloud metadata)
    [InlineData("64:ff9b::7f00:1")]                  // NAT64 wrapping 127.0.0.1
    [InlineData("64:ff9b::a9fe:a9fe")]               // NAT64 wrapping 169.254.169.254
    public void Blocks_InternalIPv4_HiddenInsideTransitionAddresses(string ipv6)
    {
        Assert.False(SsrfGuard.IsPubliclyRoutable(IPAddress.Parse(ipv6)));
    }

    [Fact]
    public void Blocks_Teredo_WrappingAnInternalClient()
    {
        // Teredo stores the client IPv4 as its bitwise NOT in the last 32 bits. ~127.0.0.1 = 128.255.255.254.
        var bytes = IPAddress.Parse("2001:0::").GetAddressBytes();
        bytes[12] = unchecked((byte)~127); bytes[13] = unchecked((byte)~0);
        bytes[14] = unchecked((byte)~0);   bytes[15] = unchecked((byte)~1);
        Assert.False(SsrfGuard.IsPubliclyRoutable(new IPAddress(bytes)));
    }

    [Theory]
    [InlineData("2002:0808:0808::")]                 // 6to4 wrapping 8.8.8.8 — a real public target
    [InlineData("64:ff9b::0808:0808")]               // NAT64 wrapping 8.8.8.8
    public void Allows_PublicIPv4_InsideTransitionAddresses(string ipv6)
    {
        // Extract-and-recheck must not over-block: a transition address to a genuinely public host is fine.
        Assert.True(SsrfGuard.IsPubliclyRoutable(IPAddress.Parse(ipv6)));
    }

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
