using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace Profiler.Web.Data;

/// <summary>
/// Takes a scheduled snapshot every <see cref="SnapshotOptions.Interval"/> and deletes snapshots past
/// the retention window. The schedule is anchored to the newest snapshot already on disk rather than
/// to process start: a host that recycles the app often (App Service F1 sleeps when idle) neither
/// piles up snapshots on every wake nor skips a day, and a host that was down past the interval
/// catches up on the next start. Every failure — an unreadable or unwritable directory, a full disk,
/// a locked database — is logged and retried; nothing here can stop the host.
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

        var source = new SqliteConnectionStringBuilder(connectionString);
        if (source.Mode == SqliteOpenMode.Memory || source.DataSource == ":memory:")
        {
            _logger.LogInformation("Database snapshots are configured but the database is in-memory; nothing to snapshot.");
            return;
        }

        var directory = _options.Directory!;
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                DatabaseSnapshots.PruneExpired(directory, _options, DateTime.UtcNow);

                var newest = DatabaseSnapshots.Newest(directory, DatabaseSnapshots.ScheduledPrefix);
                var due = newest.HasValue ? newest.Value + _options.Interval : DateTime.UtcNow;
                var wait = due - DateTime.UtcNow;
                if (wait > TimeSpan.Zero)
                {
                    await Task.Delay(wait > MaxWait ? MaxWait : wait, stoppingToken);
                    continue; // re-evaluate against the disk, which may have changed meanwhile
                }

                if (DatabaseSnapshots.Take(connectionString, _options, DatabaseSnapshots.ScheduledPrefix, _logger) == null)
                {
                    // Nothing on disk to copy yet (the database file has not been created). Not an
                    // error, but without a wait this would spin.
                    _logger.LogWarning("Database snapshot skipped: no database file at {Path}; trying again in {Delay}",
                        source.DataSource, RetryDelay);
                    await Task.Delay(RetryDelay, stoppingToken);
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogError(ex, "Database snapshot failed; retrying in {Delay}", RetryDelay);
                await Task.Delay(RetryDelay, stoppingToken);
            }
        }
    }
}
