using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Profiler.Web.Data;
using Profiler.Web.Data.Models;
using Profiler.Web.Profile;
using Xunit;

namespace Profiler.Web.Tests.Integration;

/// <summary>
/// The pepper is what keeps a stolen database from being tested against guessed interests, and
/// changing it makes every stored signature meaningless — they would compare against nothing,
/// silently, forever. The startup safeguard clears them so that failure is loud and recoverable
/// instead. That safeguard <em>deletes data</em>, so it gets a regression test rather than a one-off
/// manual check: it must fire on a real change, and must not fire when nothing changed.
/// </summary>
public class PepperRotationTests
{
    /// <summary>Boots the real app against a caller-chosen database file and pepper.</summary>
    private sealed class PepperedFactory : WebApplicationFactory<Program>
    {
        private readonly string _dbPath;
        private readonly string _pepper;

        public PepperedFactory(string dbPath, string pepper)
        {
            _dbPath = dbPath;
            _pepper = pepper;
        }

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment(Environments.Development);
            builder.UseSetting("Fingerprint:Pepper", _pepper);
            builder.ConfigureServices(services =>
            {
                var descriptor = services.SingleOrDefault(d => d.ServiceType == typeof(DbContextOptions<AppDbContext>));
                if (descriptor != null) services.Remove(descriptor);
                services.AddDbContext<AppDbContext>(o => o.UseSqlite($"Data Source={_dbPath}"));
            });
        }
    }

    /// <summary>Boots a factory (running startup, including the purge) and seeds a signature under it.</summary>
    private static async Task SeedSignatureAsync(WebApplicationFactory<Program> factory, string username)
    {
        _ = factory.CreateClient(); // forces startup — records the scheme verifier for this pepper
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var generator = scope.ServiceProvider.GetRequiredService<FingerprintGenerator>();

        var user = new AppUser { Username = username, PasswordHash = "x" };
        db.Users.Add(user);
        await db.SaveChangesAsync();

        var raw = generator.GenerateRaw(new[] { "language:python", "genre:scifi" });
        db.SourceFingerprints.Add(new SourceFingerprintRecord
        {
            UserId = user.Id, Source = "GitHub", FeatureCount = 2,
            RawSignatureJson = System.Text.Json.JsonSerializer.Serialize(raw)
        });
        db.Fingerprints.Add(new FingerprintRecord
        {
            UserId = user.Id,
            FingerprintJson = FingerprintGenerator.FromRaw(raw).ToJson(),
            SourcesJson = "[\"GitHub\"]"
        });
        await db.SaveChangesAsync();
    }

    private static async Task<(int Fingerprints, int Sources, int Users)> CountsAsync(WebApplicationFactory<Program> factory)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        return (
            await db.Fingerprints.CountAsync(),
            await db.SourceFingerprints.CountAsync(),
            await db.Users.CountAsync());
    }

    [Fact]
    public async Task ChangingThePepper_ClearsTheNowMeaninglessSignatures_ButKeepsTheAccounts()
    {
        var dbPath = Path.Combine(Path.GetTempPath(), $"profiler-pepper-{Guid.NewGuid():N}.db");
        try
        {
            using (var first = new PepperedFactory(dbPath, "pepper-one"))
                await SeedSignatureAsync(first, "rotate_" + Guid.NewGuid().ToString("N")[..8]);

            // A different pepper: every stored signature was built under the old hash family.
            using var second = new PepperedFactory(dbPath, "pepper-two");
            var counts = await CountsAsync(second);

            Assert.Equal(0, counts.Fingerprints);
            Assert.Equal(0, counts.Sources);
            // The account itself is not signal data and must survive — the person just reconnects.
            Assert.Equal(1, counts.Users);
        }
        finally
        {
            foreach (var f in Directory.GetFiles(Path.GetDirectoryName(dbPath)!, Path.GetFileName(dbPath) + "*"))
                try { File.Delete(f); } catch { /* best effort */ }
        }
    }

    [Fact]
    public async Task RebootingWithTheSamePepper_LeavesTheSignaturesAlone()
    {
        var dbPath = Path.Combine(Path.GetTempPath(), $"profiler-pepper-{Guid.NewGuid():N}.db");
        try
        {
            using (var first = new PepperedFactory(dbPath, "steady-pepper"))
                await SeedSignatureAsync(first, "steady_" + Guid.NewGuid().ToString("N")[..8]);

            using var second = new PepperedFactory(dbPath, "steady-pepper");
            var counts = await CountsAsync(second);

            // No change means no churn: a restart must not throw away everyone's fingerprints.
            Assert.Equal(1, counts.Fingerprints);
            Assert.Equal(1, counts.Sources);
        }
        finally
        {
            foreach (var f in Directory.GetFiles(Path.GetDirectoryName(dbPath)!, Path.GetFileName(dbPath) + "*"))
                try { File.Delete(f); } catch { /* best effort */ }
        }
    }
}
