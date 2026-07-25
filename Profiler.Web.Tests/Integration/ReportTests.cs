using System.Net;
using System.Net.Http.Headers;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Profiler.Web.Controllers;
using Profiler.Web.Data;
using Profiler.Web.Data.Models;
using Xunit;

namespace Profiler.Web.Tests.Integration;

/// <summary>
/// Reporting is the safety recourse the product lacked: the only prior option was a silent one-sided
/// hide, so a bad actor stayed in everyone else's pool. A report records operator-facing moderation
/// data (closed-set reason, never free text), also hides the reported user from the reporter, and is
/// visible to the operator behind the same token as /metrics.
/// </summary>
public class ReportTests : IClassFixture<ProfilerWebFactory>
{
    private readonly ProfilerWebFactory _factory;
    public ReportTests(ProfilerWebFactory factory) => _factory = factory;

    private const string Token = "metrics-secret-zzqx";

    private HttpClient NewClient() => _factory.CreateClient(new WebApplicationFactoryClientOptions
    {
        AllowAutoRedirect = false
    });

    private static async Task<string> TokenAsync(HttpResponseMessage resp) =>
        Regex.Match(await resp.Content.ReadAsStringAsync(),
            "name=\"__RequestVerificationToken\"[^>]*value=\"([^\"]+)\"").Groups[1].Value;

    private static async Task RegisterAsync(HttpClient client, string username)
    {
        var page = await client.GetAsync("/account/register");
        await client.PostAsync("/account/register", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["Username"] = username,
            ["Password"] = "Tr0ubad0ur-x9",
            ["ConfirmPassword"] = "Tr0ubad0ur-x9",
            ["__RequestVerificationToken"] = await TokenAsync(page)
        }));
    }

    private static async Task<int> SeedUserAsync(ProfilerWebFactory factory, string username)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var u = new AppUser { Username = username, PasswordHash = "x", IsDiscoverable = true };
        db.Users.Add(u);
        await db.SaveChangesAsync();
        return u.Id;
    }

    private async Task<HttpResponseMessage> ReportAsync(HttpClient client, string username, string reason)
    {
        var page = await client.GetAsync("/matches");
        return await client.PostAsync("/matches/report", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["username"] = username,
            ["reason"] = reason,
            ["__RequestVerificationToken"] = await TokenAsync(page)
        }));
    }

    [Fact]
    public async Task Reporting_RecordsTheReport_AndHidesThemFromTheReporter_Idempotently()
    {
        var reporter = "rep_" + Guid.NewGuid().ToString("N")[..8];
        var target = "tgt_" + Guid.NewGuid().ToString("N")[..8];
        var client = NewClient();
        await RegisterAsync(client, reporter);
        await SeedUserAsync(_factory, target);

        var resp = await ReportAsync(client, target, "harassment");
        Assert.Equal(HttpStatusCode.Redirect, resp.StatusCode);
        // Reporting again is idempotent — no duplicate rows, no error.
        await ReportAsync(client, target, "harassment");

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var me = await db.Users.FirstAsync(u => u.Username == reporter);
        var them = await db.Users.FirstAsync(u => u.Username == target);
        Assert.Equal(1, await db.UserReports.CountAsync(r => r.ReporterId == me.Id && r.ReportedId == them.Id));
        Assert.Equal("harassment", (await db.UserReports.FirstAsync(r => r.ReporterId == me.Id)).Reason);
        // Also hidden from the reporter.
        Assert.True(await db.UserBlocks.AnyAsync(b => b.BlockerId == me.Id && b.BlockedId == them.Id));
    }

    [Fact]
    public async Task AnOffListReason_IsStoredAsOther()
    {
        var reporter = "repx_" + Guid.NewGuid().ToString("N")[..8];
        var target = "tgtx_" + Guid.NewGuid().ToString("N")[..8];
        var client = NewClient();
        await RegisterAsync(client, reporter);
        await SeedUserAsync(_factory, target);

        await ReportAsync(client, target, "made-up-reason");

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var me = await db.Users.FirstAsync(u => u.Username == reporter);
        Assert.Equal("other", (await db.UserReports.FirstAsync(r => r.ReporterId == me.Id)).Reason);
    }

    [Fact]
    public async Task OperatorReportsView_IsTokenGated_AndSurfacesTheReportedUser()
    {
        var reporter = "repv_" + Guid.NewGuid().ToString("N")[..8];
        var target = "tgtv_" + Guid.NewGuid().ToString("N")[..8];
        var client = NewClient();
        await RegisterAsync(client, reporter);
        await SeedUserAsync(_factory, target);
        await ReportAsync(client, target, "spam");

        // An ordinary session (no token, feature off) cannot reach it.
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync("/metrics/reports")).StatusCode);

        // With the operator token configured + presented, it lists the reported user and reason.
        var opClient = _factory.WithWebHostBuilder(b => b.UseSetting(MetricsController.TokenKey, Token))
            .CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        var req = new HttpRequestMessage(HttpMethod.Get, "/metrics/reports");
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", Token);
        var resp = await opClient.SendAsync(req);
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        var body = await resp.Content.ReadAsStringAsync();
        Assert.Contains(target, body);
        Assert.Contains("spam", body);
        Assert.Contains("distinctReporters", body, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task DeletingAnAccount_RemovesReports_ItFiledOrReceived()
    {
        var reporter = "repd_" + Guid.NewGuid().ToString("N")[..8];
        var target = "tgtd_" + Guid.NewGuid().ToString("N")[..8];
        var client = NewClient();
        await RegisterAsync(client, reporter);
        await SeedUserAsync(_factory, target);
        await ReportAsync(client, target, "impersonation");

        // The reporter deletes their account.
        var page = await client.GetAsync("/sources/dashboard");
        await client.PostAsync("/account/delete", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["password"] = "Tr0ubad0ur-x9",
            ["__RequestVerificationToken"] = await TokenAsync(page)
        }));

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var them = await db.Users.FirstAsync(u => u.Username == target);
        Assert.False(await db.UserReports.AnyAsync(r => r.ReportedId == them.Id),
            "the report filed by the deleted account should be gone");
    }
}
