using System.Net;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Profiler.Web.Data;
using Profiler.Web.Data.Models;
using Profiler.Web.Profile;
using Xunit;

namespace Profiler.Web.Tests.Integration;

/// <summary>Until a fingerprint exists the no-accounts path leads the quick actions; afterwards sources do.</summary>
public class DashboardQuickActionsTests : IClassFixture<ProfilerWebFactory>
{
    private readonly ProfilerWebFactory _factory;
    public DashboardQuickActionsTests(ProfilerWebFactory factory) => _factory = factory;

    [Fact]
    public async Task InterestsLead_UntilAFingerprintExists_ThenSourcesLead()
    {
        var client = _factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        var name = "dash_" + Guid.NewGuid().ToString("N")[..6];
        var page = await client.GetAsync("/account/register");
        var token = Regex.Match(await page.Content.ReadAsStringAsync(), "name=\"__RequestVerificationToken\"[^>]*value=\"([^\"]+)\"").Groups[1].Value;
        var reg = await client.PostAsync("/account/register", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["Username"] = name, ["Password"] = "Tr0ubad0ur-x9", ["ConfirmPassword"] = "Tr0ubad0ur-x9", ["__RequestVerificationToken"] = token
        }));
        Assert.Equal(HttpStatusCode.Redirect, reg.StatusCode);

        static (int Interests, int Connect) Positions(string html)
        {
            var actions = html[html.IndexOf("quick-actions", StringComparison.Ordinal)..];
            return (actions.IndexOf("/sources/interests", StringComparison.Ordinal), actions.IndexOf("/sources/connect", StringComparison.Ordinal));
        }

        var before = Positions(await client.GetStringAsync("/sources/dashboard"));
        Assert.True(before.Interests >= 0 && before.Connect >= 0);
        Assert.True(before.Interests < before.Connect, "without a fingerprint, describing interests comes first");

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var me = await db.Users.SingleAsync(u => u.Username == name);
            db.Fingerprints.Add(new FingerprintRecord { UserId = me.Id, FingerprintJson = new FingerprintGenerator(128).Generate(new[] { "x:1" }).ToJson(), SourcesJson = "[\"GitHub\"]" });
            await db.SaveChangesAsync();
        }

        var after = Positions(await client.GetStringAsync("/sources/dashboard"));
        Assert.True(after.Connect < after.Interests, "with a fingerprint, updating sources comes first");
    }
}
