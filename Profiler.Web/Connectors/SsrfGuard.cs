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
    public static async Task<bool> IsAllowedAsync(Uri uri, CancellationToken cancellationToken = default)
    {
        IPAddress[] addresses;
        if (IPAddress.TryParse(uri.Host, out var literal))
        {
            addresses = new[] { literal };
        }
        else
        {
            // A hostile name server can stall a lookup, so this has to be cancellable too — it is
            // the one step of a feed fetch that happens before any HttpClient timeout applies.
            try { addresses = await Dns.GetHostAddressesAsync(uri.Host, cancellationToken); }
            catch (OperationCanceledException) { throw; }
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
            return IsPubliclyRoutableV4(ip.GetAddressBytes());

        if (ip.AddressFamily == AddressFamily.InterNetworkV6)
        {
            if (ip.IsIPv6LinkLocal || ip.IsIPv6Multicast || ip.IsIPv6SiteLocal) return false;
            if (ip.Equals(IPAddress.IPv6Any)) return false;
            var b = ip.GetAddressBytes();
            if ((b[0] & 0xFE) == 0xFC) return false;                       // fc00::/7 unique local

            // Transition addresses carry an IPv4 destination inside them, and on a host with a
            // 6to4/NAT64/Teredo egress path that inner address is where the packet actually goes.
            // The outer IPv6 is globally-scoped, so the checks above wave it through — the inner
            // IPv4 has to be judged on its own, or "2002:7f00:1::" (6to4 for 127.0.0.1) is an open door.
            if (TryEmbeddedV4(b, out var embedded))
                return IsPubliclyRoutableV4(embedded);

            return true;
        }

        return false;
    }

    private static bool IsPubliclyRoutableV4(byte[] b)
    {
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

    /// <summary>
    /// Extracts the IPv4 address embedded in an IPv6 transition address, if any. 6to4 and NAT64
    /// carry the target address in the clear; Teredo carries the client address obfuscated by a
    /// bitwise NOT.
    /// </summary>
    private static bool TryEmbeddedV4(byte[] b, out byte[] v4)
    {
        // 6to4: 2002::/16 — target IPv4 in bytes 2..5.
        if (b[0] == 0x20 && b[1] == 0x02)
        {
            v4 = new[] { b[2], b[3], b[4], b[5] };
            return true;
        }

        // NAT64 well-known prefix: 64:ff9b::/96 — target IPv4 in the low 32 bits.
        if (b[0] == 0x00 && b[1] == 0x64 && b[2] == 0xff && b[3] == 0x9b
            && b[4] == 0 && b[5] == 0 && b[6] == 0 && b[7] == 0
            && b[8] == 0 && b[9] == 0 && b[10] == 0 && b[11] == 0)
        {
            v4 = new[] { b[12], b[13], b[14], b[15] };
            return true;
        }

        // Teredo: 2001:0000::/32 — client IPv4 in the last 32 bits, stored as its bitwise NOT.
        if (b[0] == 0x20 && b[1] == 0x01 && b[2] == 0x00 && b[3] == 0x00)
        {
            v4 = new[] { (byte)~b[12], (byte)~b[13], (byte)~b[14], (byte)~b[15] };
            return true;
        }

        v4 = Array.Empty<byte>();
        return false;
    }
}
