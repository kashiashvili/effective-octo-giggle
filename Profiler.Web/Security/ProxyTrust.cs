using System.Net;
using Microsoft.AspNetCore.HttpOverrides;

namespace Profiler.Web.Security;

/// <summary>
/// Decides whether to believe the <c>X-Forwarded-*</c> headers on incoming requests.
///
/// This matters more than it looks. Every rate limiter here partitions on the connecting IP, so
/// behind a TLS-terminating proxy — the deployment the docs describe — every visitor arrives as the
/// proxy's address and shares one bucket: the login limit becomes five attempts per minute for the
/// entire site, not per person. Reading the forwarded address fixes that, and also lets HTTPS
/// redirection see the original scheme.
///
/// The headers are attacker-supplied, though: believing them from just anyone hands out a limiter
/// bypass (spoof a different X-Forwarded-For per request and the partition key changes every time).
/// So this is off unless switched on, and switching it on without naming the proxies you trust is a
/// startup error rather than a silently insecure default.
/// </summary>
public static class ProxyTrust
{
    public const string EnabledKey = "ForwardedHeaders:Enabled";
    public const string ProxiesKey = "ForwardedHeaders:KnownProxies";
    public const string NetworksKey = "ForwardedHeaders:KnownNetworks";

    /// <summary>
    /// Builds the options to register, or null when forwarded headers are switched off.
    /// </summary>
    /// <exception cref="InvalidOperationException">
    /// Thrown when the feature is enabled but no proxy or network is trusted, because that
    /// configuration would accept a forged client address from any caller on the internet.
    /// </exception>
    public static ForwardedHeadersOptions? Build(IConfiguration configuration)
    {
        if (configuration.GetValue<bool?>(EnabledKey) != true) return null;

        var proxies = Read(configuration, ProxiesKey);
        var networks = Read(configuration, NetworksKey);

        var options = new ForwardedHeadersOptions
        {
            ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto,
            // One proxy hop by default; a longer chain has to be described by trusting each hop.
            ForwardedForHeaderName = ForwardedHeadersDefaults.XForwardedForHeaderName
        };

        // The defaults trust loopback, which is not what is wanted once this is deliberately
        // configured: the trusted set should be exactly what the operator named.
        options.KnownProxies.Clear();
        options.KnownNetworks.Clear();

        foreach (var proxy in proxies)
        {
            if (!IPAddress.TryParse(proxy, out var address))
                throw new InvalidOperationException($"{ProxiesKey} contains '{proxy}', which is not an IP address.");
            options.KnownProxies.Add(address);
        }

        foreach (var network in networks)
        {
            var parts = network.Split('/', 2);
            if (parts.Length != 2
                || !IPAddress.TryParse(parts[0], out var prefix)
                || !int.TryParse(parts[1], out var length))
            {
                throw new InvalidOperationException(
                    $"{NetworksKey} contains '{network}', which is not CIDR notation such as 10.0.0.0/8.");
            }
            options.KnownNetworks.Add(new Microsoft.AspNetCore.HttpOverrides.IPNetwork(prefix, length));
        }

        if (options.KnownProxies.Count == 0 && options.KnownNetworks.Count == 0)
        {
            throw new InvalidOperationException(
                $"{EnabledKey} is true but neither {ProxiesKey} nor {NetworksKey} names anything to trust. " +
                "Forwarded headers would then be accepted from any caller, letting anyone forge their " +
                "client address and walk past the rate limiters. Name your proxy, or set " +
                $"{EnabledKey} to false.");
        }

        return options;
    }

    private static string[] Read(IConfiguration configuration, string key)
    {
        // Accepts both a JSON array and a comma-separated environment variable, since the deployment
        // notes tell operators they can use either for any setting.
        var section = configuration.GetSection(key);
        var list = section.Get<string[]>();
        if (list is { Length: > 0 }) return list.Select(v => v.Trim()).Where(v => v.Length > 0).ToArray();

        var flat = configuration.GetValue<string?>(key);
        return string.IsNullOrWhiteSpace(flat)
            ? Array.Empty<string>()
            : flat.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
    }
}
