using System.Net;
using System.Net.Sockets;

namespace Profiler.Web.Connectors;

/// <summary>
/// Guards against server-side request forgery when fetching user-supplied URLs. A host is allowed
/// only if every address it resolves to is publicly routable — loopback, private, link-local
/// (including the 169.254.169.254 cloud-metadata endpoint), CGNAT, unique-local, multicast and
/// reserved ranges are all rejected. Fails closed (unresolvable or mixed = denied).
/// </summary>
public static class SsrfGuard
{
    public static async Task<bool> IsAllowedAsync(Uri uri)
    {
        IPAddress[] addresses;
        if (IPAddress.TryParse(uri.Host, out var literal))
        {
            addresses = new[] { literal };
        }
        else
        {
            try { addresses = await Dns.GetHostAddressesAsync(uri.Host); }
            catch { return false; }
        }

        return addresses.Length > 0 && addresses.All(IsPubliclyRoutable);
    }

    /// <summary>
    /// Resolves a host and returns its addresses only if every one of them is publicly routable.
    /// Callers connect to exactly these addresses, which is what closes the DNS-rebinding window:
    /// validating a name and then letting the socket layer resolve it again would leave room for
    /// the answer to change in between.
    /// </summary>
    public static async Task<IPAddress[]> ResolveAllowedAsync(string host, CancellationToken cancellationToken = default)
    {
        IPAddress[] addresses;
        if (IPAddress.TryParse(host, out var literal))
        {
            addresses = new[] { literal };
        }
        else
        {
            try { addresses = await Dns.GetHostAddressesAsync(host, cancellationToken); }
            catch (Exception ex)
            {
                throw new IOException($"Refusing to connect to '{host}': the host could not be resolved.", ex);
            }
        }

        if (addresses.Length == 0 || !addresses.All(IsPubliclyRoutable))
            throw new IOException($"Refusing to connect to '{host}': it resolves to a non-public address.");

        return addresses;
    }

    public static bool IsPubliclyRoutable(IPAddress ip)
    {
        if (ip.IsIPv4MappedToIPv6) ip = ip.MapToIPv4();

        if (IPAddress.IsLoopback(ip)) return false;

        if (ip.AddressFamily == AddressFamily.InterNetwork)
        {
            var b = ip.GetAddressBytes();
            if (b[0] == 0) return false;                                   // 0.0.0.0/8
            if (b[0] == 10) return false;                                  // 10.0.0.0/8
            if (b[0] == 100 && b[1] >= 64 && b[1] <= 127) return false;    // 100.64.0.0/10 CGNAT
            if (b[0] == 127) return false;                                 // 127.0.0.0/8
            if (b[0] == 169 && b[1] == 254) return false;                  // 169.254.0.0/16 link-local (cloud metadata)
            if (b[0] == 172 && b[1] >= 16 && b[1] <= 31) return false;     // 172.16.0.0/12
            if (b[0] == 192 && b[1] == 0 && b[2] == 0) return false;       // 192.0.0.0/24
            if (b[0] == 192 && b[1] == 168) return false;                  // 192.168.0.0/16
            if (b[0] == 198 && (b[1] == 18 || b[1] == 19)) return false;   // 198.18.0.0/15 benchmarking
            if (b[0] >= 224) return false;                                 // 224.0.0.0/4 multicast + 240.0.0.0/4 reserved
            return true;
        }

        if (ip.AddressFamily == AddressFamily.InterNetworkV6)
        {
            if (ip.IsIPv6LinkLocal || ip.IsIPv6Multicast || ip.IsIPv6SiteLocal) return false;
            if (ip.Equals(IPAddress.IPv6Any)) return false;
            var b = ip.GetAddressBytes();
            if ((b[0] & 0xFE) == 0xFC) return false;                       // fc00::/7 unique local
            return true;
        }

        return false;
    }
}
