using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Profiler.Web.Data;

namespace Profiler.Web.Tests.Integration;

/// <summary>
/// Boots the real app against an isolated per-instance SQLite file and relaxed rate limits,
/// so integration tests exercise the true middleware/controller pipeline without touching dev data.
/// </summary>
public class ProfilerWebFactory : WebApplicationFactory<Program>
{
    private readonly string _dbPath = Path.Combine(Path.GetTempPath(), $"profiler-test-{Guid.NewGuid():N}.db");

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment(Environments.Development);
        builder.UseSetting("RateLimiting:LoginPermitLimit", "100000");
        builder.UseSetting("RateLimiting:RegisterPermitLimit", "100000");
        builder.UseSetting("RateLimiting:ConnectPermitLimit", "100000");
        builder.UseSetting("RateLimiting:CirclesPermitLimit", "100000");

        builder.ConfigureServices(services =>
        {
            var descriptor = services.SingleOrDefault(d => d.ServiceType == typeof(DbContextOptions<AppDbContext>));
            if (descriptor != null) services.Remove(descriptor);

            services.AddDbContext<AppDbContext>(o => o.UseSqlite($"Data Source={_dbPath}"));
        });
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        if (disposing)
        {
            try { if (File.Exists(_dbPath)) File.Delete(_dbPath); } catch { /* best effort */ }
        }
    }
}
