using Microsoft.EntityFrameworkCore;

namespace Profiler.Web.Data;

/// <summary>
/// Takes a scheduled snapshot every <see cref="SnapshotOptions.Interval"/>. The schedule is anchored to
/// the newest snapshot already on disk rather than to process start: a host that recycles the app often
/// (App Service F1 sleeps when idle) neither piles up snapshots on every wake nor skips a day, and a
/// host that was down past the interval catches up on the next start. Failures are logged and retried;
/// they never take the app down.
/// </summary>
public sealed class DatabaseSnapshotService : BackgroundService
{
    private static readonly TimeSpan RetryDelay = TimeSpan.FromMinutes(15);
    private static readonly TimeSpan MaxWait = TimeSpan.FromHours(24);

    private readonly IServiceScopeFactory _scopes;
    private readonly SnapshotOptions _options;
    private readonly ILogger<DatabaseSnapshotService> _logger;

    public DatabaseSnapshotService(IServiceScopeFactory scopes, SnapshotOptions options, ILogger<DatabaseSnapshotService> logger)
    {
        _scopes = scopes;
        _options = options;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_options.Enabled) return;

        // Do not hold up host start-up on the first snapshot.
        await Task.Yield();

        // The connection string the app actually uses (tests and hosts may configure it in code).
        string? connectionString;
        using (var scope = _scopes.CreateScope())
            connectionString = scope.ServiceProvider.GetRequiredService<AppDbContext>().Database.GetConnectionString();
        if (connectionString == null) return;

        while (!stoppingToken.IsCancellationRequested)
        {
            var newest = DatabaseSnapshots.Newest(_options.Directory!, DatabaseSnapshots.ScheduledPrefix);
            var due = newest.HasValue ? newest.Value + _options.Interval : DateTime.UtcNow;
            var wait = due - DateTime.UtcNow;
            if (wait > TimeSpan.Zero)
            {
                await Task.Delay(wait > MaxWait ? MaxWait : wait, stoppingToken);
                continue; // re-evaluate against the disk, which may have changed meanwhile
            }

            try
            {
                DatabaseSnapshots.Take(connectionString, _options, DatabaseSnapshots.ScheduledPrefix, _logger);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogError(ex, "Database snapshot failed; retrying in {Delay}", RetryDelay);
                await Task.Delay(RetryDelay, stoppingToken);
            }
        }
    }
}
