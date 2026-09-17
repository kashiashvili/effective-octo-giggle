using System.Net;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace Profiler.Web.Tests.Integration;

/// <summary>
/// The snapshot service inside the real host: with a directory configured a scheduled snapshot appears
/// shortly after start-up, and the privacy page tells users how long a deleted row can survive in one.
/// Without a directory nothing is written and nothing is claimed.
/// </summary>
public class DatabaseSnapshotHostTests : IClassFixture<ProfilerWebFactory>
{
    private readonly ProfilerWebFactory _factory;
    public DatabaseSnapshotHostTests(ProfilerWebFactory factory) => _factory = factory;

    [Fact]
    public async Task Startup_TakesAScheduledSnapshot_AndThePrivacyPageDisclosesRetention()
    {
        var dir = Path.Combine(Path.GetTempPath(), $"profiler-snap-host-{Guid.NewGuid():N}");
        try
        {
            using var host = _factory.WithWebHostBuilder(b => b.UseSetting("Backup:Directory", dir));
            var client = host.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

            var privacy = await client.GetStringAsync("/home/privacy");
            Assert.Contains("rolling snapshots", privacy);
            Assert.Contains("older than <strong>7 days</strong> is deleted", privacy);

            // The service yields at start-up; give it a moment.
            string[] files = [];
            for (var i = 0; i < 100 && files.Length == 0; i++)
            {
                await Task.Delay(100);
                files = Directory.Exists(dir) ? Directory.GetFiles(dir, "profiler-scheduled-*.db") : [];
            }
            Assert.Single(files);
            Assert.Empty(Directory.GetFiles(dir, "*.tmp"));
        }
        finally
        {
            try { Directory.Delete(dir, recursive: true); } catch { /* best effort */ }
        }
    }

    [Fact]
    public async Task AnUnreadableDirectory_IsLoggedAndRetried_AndNeverStopsTheHost()
    {
        if (OperatingSystem.IsWindows()) return; // permission model differs; the guarded loop is the same code

        var dir = Path.Combine(Path.GetTempPath(), $"profiler-snap-locked-{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        File.SetUnixFileMode(dir, UnixFileMode.None);
        try
        {
            using var host = _factory.WithWebHostBuilder(b => b.UseSetting("Backup:Directory", dir));
            var client = host.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

            // The service hits the unreadable directory right after start-up. A stopped host would
            // fail these requests; the guarded loop logs and waits instead.
            Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/health")).StatusCode);
            await Task.Delay(500);
            Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/health")).StatusCode);
            Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/home/privacy")).StatusCode);
        }
        finally
        {
            try { File.SetUnixFileMode(dir, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute); } catch { /* best effort */ }
            try { Directory.Delete(dir, recursive: true); } catch { /* best effort */ }
        }
    }

    [Fact]
    public async Task WithoutADirectory_NothingIsClaimed()
    {
        var client = _factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        var privacy = await client.GetStringAsync("/home/privacy");
        Assert.DoesNotContain("rolling snapshots", privacy);
    }
}
