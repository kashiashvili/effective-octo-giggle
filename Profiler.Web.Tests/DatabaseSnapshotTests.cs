using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.Logging.Abstractions;
using Profiler.Web.Data;
using Profiler.Web.Data.Models;
using Xunit;

namespace Profiler.Web.Tests;

/// <summary>
/// Rolling SQLite snapshots: the only recovery path on hosts without platform backups. A snapshot
/// must be a consistent, independently openable database; retention must be bounded; and an
/// established database must be snapshotted before a migration changes it.
/// </summary>
public class DatabaseSnapshotTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"profiler-snap-{Guid.NewGuid():N}");
    private readonly string _dbPath;
    private readonly string _connectionString;
    private readonly SnapshotOptions _options;

    public DatabaseSnapshotTests()
    {
        Directory.CreateDirectory(_root);
        _dbPath = Path.Combine(_root, "live.db");
        _connectionString = new SqliteConnectionStringBuilder { DataSource = _dbPath, Pooling = false }.ConnectionString;
        _options = new SnapshotOptions { Directory = Path.Combine(_root, "backups"), Keep = 2 };
    }

    private AppDbContext NewContext() => new(new DbContextOptionsBuilder<AppDbContext>().UseSqlite(_connectionString).Options);

    private static AppDbContext OpenFile(string path) =>
        new(new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite(new SqliteConnectionStringBuilder { DataSource = path, Pooling = false }.ConnectionString).Options);

    [Fact]
    public void Take_WritesAnIndependentlyOpenableCopy_WithTheSameRows()
    {
        using (var db = NewContext())
        {
            db.Database.Migrate();
            db.Users.Add(new AppUser { Username = "alice", NormalizedUsername = "alice", PasswordHash = "x" });
            db.Users.Add(new AppUser { Username = "bob", NormalizedUsername = "bob", PasswordHash = "x" });
            db.SaveChanges();
        }

        var path = DatabaseSnapshots.Take(_connectionString, _options, DatabaseSnapshots.ScheduledPrefix, NullLogger.Instance);

        Assert.NotNull(path);
        Assert.StartsWith("profiler-scheduled-", Path.GetFileName(path));
        Assert.EndsWith(".db", path);
        Assert.Empty(Directory.GetFiles(_options.Directory!, "*.tmp"));
        using var copy = OpenFile(path!);
        Assert.Equal(2, copy.Users.Count());
        Assert.False(copy.Database.GetPendingMigrations().Any(), "snapshot carries the full schema");
    }

    [Fact]
    public void Take_IsANoOp_WhenSnapshotsAreOff_OrThereIsNoDatabaseFileYet()
    {
        var off = new SnapshotOptions();
        Assert.False(off.Enabled);
        Assert.Null(DatabaseSnapshots.Take(_connectionString, off, DatabaseSnapshots.ScheduledPrefix, NullLogger.Instance));

        // Directory set, but the database has never been created: nothing to copy, nothing written.
        Assert.Null(DatabaseSnapshots.Take(_connectionString, _options, DatabaseSnapshots.ScheduledPrefix, NullLogger.Instance));
        Assert.False(Directory.Exists(_options.Directory!) && Directory.GetFiles(_options.Directory!).Length > 0);

        Assert.Null(DatabaseSnapshots.Take("Data Source=:memory:", _options, DatabaseSnapshots.ScheduledPrefix, NullLogger.Instance));
    }

    [Fact]
    public void Prune_KeepsTheNewestPerKind_AndIgnoresOtherKinds()
    {
        var dir = _options.Directory!;
        Directory.CreateDirectory(dir);
        foreach (var stamp in new[] { "20260101-000000", "20260102-000000", "20260103-000000", "20260104-000000" })
            File.WriteAllText(Path.Combine(dir, $"profiler-scheduled-{stamp}.db"), "");
        File.WriteAllText(Path.Combine(dir, "profiler-premigrate-20250101-000000.db"), "");

        var removed = DatabaseSnapshots.Prune(dir, DatabaseSnapshots.ScheduledPrefix, keep: 2);

        Assert.Equal(2, removed);
        var left = Directory.GetFiles(dir).Select(Path.GetFileName).OrderBy(n => n, StringComparer.Ordinal).ToArray();
        Assert.Equal(new[]
        {
            "profiler-premigrate-20250101-000000.db",
            "profiler-scheduled-20260103-000000.db",
            "profiler-scheduled-20260104-000000.db",
        }, left);
    }

    [Fact]
    public void Newest_ReadsTheUtcStampFromTheFileName()
    {
        var dir = _options.Directory!;
        Directory.CreateDirectory(dir);
        Assert.Null(DatabaseSnapshots.Newest(dir, DatabaseSnapshots.ScheduledPrefix));

        File.WriteAllText(Path.Combine(dir, "profiler-scheduled-20260301-120000.db"), "");
        File.WriteAllText(Path.Combine(dir, "profiler-scheduled-20260302-053000.db"), "");

        var newest = DatabaseSnapshots.Newest(dir, DatabaseSnapshots.ScheduledPrefix);
        Assert.Equal(new DateTime(2026, 3, 2, 5, 30, 0, DateTimeKind.Utc), newest);
        Assert.Equal(DateTimeKind.Utc, newest!.Value.Kind);
    }

    [Fact]
    public void BeforeMigrate_SnapshotsAnEstablishedDatabase_OnlyWhenMigrationsArePending()
    {
        using var db = NewContext();

        // Fresh database: nothing applied yet, nothing worth copying.
        Assert.Null(DatabaseSnapshots.BeforeMigrate(db, _options, NullLogger.Instance));

        // Established database at an old schema version with data in it.
        db.GetService<IMigrator>().Migrate("20260721055937_InitialCreate");
        db.Database.ExecuteSqlRaw("INSERT INTO Users (Username, PasswordHash, CreatedAt) VALUES ('old', 'x', '2026-01-01')");
        Assert.True(db.Database.GetPendingMigrations().Any());

        var path = DatabaseSnapshots.BeforeMigrate(db, _options, NullLogger.Instance);

        Assert.NotNull(path);
        Assert.StartsWith("profiler-premigrate-", Path.GetFileName(path));
        using (var conn = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = path!, Pooling = false }.ConnectionString))
        {
            conn.Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT COUNT(*) FROM Users";
            Assert.Equal(1L, (long)cmd.ExecuteScalar()!);
        }

        // Current database: no pending migrations, no second snapshot.
        db.Database.Migrate();
        Assert.Null(DatabaseSnapshots.BeforeMigrate(db, _options, NullLogger.Instance));
        Assert.Single(Directory.GetFiles(_options.Directory!, "profiler-premigrate-*.db"));
    }

    [Fact]
    public void PruneExpired_RemovesEveryKindPastRetention_AndStaleTempFiles_OnlyKeepingRecentOnes()
    {
        var dir = _options.Directory!;
        Directory.CreateDirectory(dir);
        var now = new DateTime(2026, 9, 17, 12, 0, 0, DateTimeKind.Utc);
        // Keep = 2, IntervalHours = 24 → retention 2 days. Files from 10 days ago of both kinds must go;
        // yesterday's and today's stay, whatever their count.
        foreach (var name in new[]
        {
            "profiler-scheduled-20260907-120000.db", "profiler-premigrate-20260901-120000.db",
            "profiler-scheduled-20260916-120000.db", "profiler-scheduled-20260917-090000.db",
            "profiler-premigrate-20260917-100000.db",
        })
            File.WriteAllText(Path.Combine(dir, name), "");
        var staleTemp = Path.Combine(dir, "profiler-scheduled-20260910-120000.db.tmp");
        File.WriteAllText(staleTemp, "");
        File.SetLastWriteTimeUtc(staleTemp, now.AddHours(-3));
        var liveTemp = Path.Combine(dir, "profiler-scheduled-20260917-115900.db.tmp");
        File.WriteAllText(liveTemp, "");
        File.SetLastWriteTimeUtc(liveTemp, now.AddMinutes(-1));

        var removed = DatabaseSnapshots.PruneExpired(dir, _options, now);

        Assert.Equal(3, removed);
        var left = Directory.GetFiles(dir).Select(Path.GetFileName).OrderBy(n => n, StringComparer.Ordinal).ToArray();
        Assert.Equal(new[]
        {
            "profiler-premigrate-20260917-100000.db",
            "profiler-scheduled-20260916-120000.db",
            "profiler-scheduled-20260917-090000.db",
            "profiler-scheduled-20260917-115900.db.tmp",
        }, left);

        Assert.Equal(0, DatabaseSnapshots.PruneExpired(Path.Combine(_root, "missing"), _options, now));
    }

    [Fact]
    public void RetentionDays_IsTheLongestADeletedRowCanSurvive()
    {
        Assert.Equal(7, new SnapshotOptions { Directory = "x" }.RetentionDays);
        Assert.Equal(3, new SnapshotOptions { Directory = "x", Keep = 6, IntervalHours = 12 }.RetentionDays);
        Assert.Equal(1, new SnapshotOptions { Directory = "x", Keep = 0, IntervalHours = 0 }.RetentionDays);
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch { /* best effort */ }
    }
}
