using Microsoft.ApplicationInsights.DataContracts;
using Microsoft.Extensions.Configuration;
using Microsoft.ApplicationInsights.Extensibility;
using Profiler.Web.Security;
using Xunit;

namespace Profiler.Web.Tests;

/// <summary>
/// Telemetry must not carry credentials off this server. A circle invite rides in a URL — in the path
/// of /circles/join/&lt;token&gt; and in the ?circle= parameter that carries it through registration and
/// sign-in — and anyone holding one can join that circle, so it is stripped before a telemetry item
/// leaves the process.
/// </summary>
public class TelemetryRedactionTests
{
    private const string Token = "CfDJ8AJNaRV1DoRJjFFKWpVsIvv11ZluiMm0aOTWeLokV7nTxTHleSOv_ujwOACuDyov";

    [Theory]
    [InlineData("/circles/join/" + Token, "/circles/join/redacted")]
    [InlineData("https://profiler.example/circles/join/" + Token, "https://profiler.example/circles/join/redacted")]
    [InlineData("GET /circles/join/" + Token, "GET /circles/join/redacted")]
    [InlineData("/account/register?circle=" + Token, "/account/register?circle=redacted")]
    [InlineData("/account/login?circle=" + Token + "&next=1", "/account/login?circle=redacted&next=1")]
    [InlineData("/CIRCLES/JOIN/" + Token, "/CIRCLES/JOIN/redacted")]
    public void Redact_RemovesInviteTokens(string input, string expected) =>
        Assert.Equal(expected, RedactSecretsInTelemetry.Redact(input));

    [Theory]
    [InlineData("/matches?sort=values", "/matches?sort=values")]
    [InlineData("/circles/7", "/circles/7")]
    [InlineData("/sources/dashboard", "/sources/dashboard")]
    [InlineData("", "")]
    public void Redact_LeavesOrdinaryUrlsAlone(string input, string expected) =>
        Assert.Equal(expected, RedactSecretsInTelemetry.Redact(input));

    [Fact]
    public void Redact_HandlesNull() => Assert.Equal("", RedactSecretsInTelemetry.Redact(null));

    [Fact]
    public void Initialize_ScrubsTheRequestUrl_TheNameAndAnyProperty()
    {
        var request = new RequestTelemetry
        {
            Name = "GET /circles/join/" + Token,
            Url = new Uri("https://profiler.example/circles/join/" + Token + "?circle=" + Token),
        };
        request.Properties["referrer"] = "https://profiler.example/account/register?circle=" + Token;
        request.Context.Operation.Name = "GET /circles/join/" + Token;

        new RedactSecretsInTelemetry().Initialize(request);

        Assert.DoesNotContain(Token, request.Name);
        Assert.DoesNotContain(Token, request.Url!.ToString());
        Assert.DoesNotContain(Token, request.Properties["referrer"]);
        Assert.DoesNotContain(Token, request.Context.Operation.Name);
        Assert.Contains("redacted", request.Url!.ToString());
    }

    [Fact]
    public void Initialize_ScrubsExceptionTelemetryProperties_Too()
    {
        var failure = new ExceptionTelemetry(new InvalidOperationException("boom"));
        failure.Properties["path"] = "/circles/join/" + Token;

        new RedactSecretsInTelemetry().Initialize(failure);

        Assert.DoesNotContain(Token, failure.Properties["path"]);
    }

    [Fact]
    public void TelemetryIsOff_UnlessAConnectionStringIsConfigured()
    {
        var empty = new ConfigurationBuilder().Build();
        Assert.Null(TelemetryOptions.ConnectionString(empty));

        var blank = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { [TelemetryOptions.ConnectionStringKey] = "   " }).Build();
        Assert.Null(TelemetryOptions.ConnectionString(blank));

        var set = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { [TelemetryOptions.ConnectionStringKey] = "InstrumentationKey=abc" }).Build();
        Assert.Equal("InstrumentationKey=abc", TelemetryOptions.ConnectionString(set));
    }
}
