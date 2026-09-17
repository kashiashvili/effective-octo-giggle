using System.Text.RegularExpressions;
using Microsoft.ApplicationInsights.Channel;
using Microsoft.ApplicationInsights.DataContracts;
using Microsoft.ApplicationInsights.Extensibility;

namespace Profiler.Web.Security;

/// <summary>
/// Whether this deployment sends operational telemetry to the hosting provider's monitoring service.
/// Registered always so the privacy page can state the truth either way, and true only when an
/// <c>APPLICATIONINSIGHTS_CONNECTION_STRING</c> is configured — so development and the test suite
/// send nothing at all.
/// </summary>
public sealed record TelemetryOptions(bool Enabled)
{
    public const string ConnectionStringKey = "APPLICATIONINSIGHTS_CONNECTION_STRING";

    public static string? ConnectionString(IConfiguration config)
    {
        var value = config[ConnectionStringKey];
        return string.IsNullOrWhiteSpace(value) ? null : value;
    }
}

/// <summary>
/// Keeps credentials out of telemetry. Request URLs are recorded by the monitoring SDK, and two of
/// this app's URLs carry a signed circle invite — in the path of <c>/circles/join/&lt;token&gt;</c> and in
/// the <c>?circle=</c> parameter that carries it through registration and sign-in. Anyone holding one
/// can join that circle, so it must not leave this server inside a telemetry payload. Everything else
/// in a URL here is a route, never a person: usernames and answers travel in request bodies, which the
/// SDK does not collect.
/// </summary>
public sealed class RedactSecretsInTelemetry : ITelemetryInitializer
{
    private static readonly Regex InvitePath = new(@"(/circles/join/)[^/?#]+", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex InviteQuery = new(@"([?&]circle=)[^&#]*", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private const string Placeholder = "redacted";

    /// <summary>Replaces any invite token in a URL or operation name with a placeholder.</summary>
    public static string Redact(string? value)
    {
        if (string.IsNullOrEmpty(value)) return value ?? "";
        var redacted = InvitePath.Replace(value, $"$1{Placeholder}");
        return InviteQuery.Replace(redacted, $"$1{Placeholder}");
    }

    public void Initialize(ITelemetry telemetry)
    {
        if (telemetry is RequestTelemetry request)
        {
            request.Name = Redact(request.Name);
            if (request.Url is { } url && Uri.TryCreate(Redact(url.ToString()), UriKind.Absolute, out var clean))
                request.Url = clean;
        }

        if (telemetry is ISupportProperties withProperties)
        {
            foreach (var key in withProperties.Properties.Keys.ToList())
                withProperties.Properties[key] = Redact(withProperties.Properties[key]);
        }

        telemetry.Context.Operation.Name = Redact(telemetry.Context.Operation.Name);
    }
}
