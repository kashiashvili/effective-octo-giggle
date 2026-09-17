using System.Globalization;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace Profiler.Web.Data;

/// <summary>
/// Settings for rolling SQLite snapshots, bound from the <c>Backup</c> configuration section.
/// Off unless <see cref="Directory"/> is set: dev and the test suite never write snapshots, while the
/// container image and the Azure bootstrap point it at the persistent data volume. The snapshots are
/// the only recovery path on hosts without platform backups (App Service F1/B1, a bare Docker host):
/// losing the database loses every account, and the pepper makes the signatures unrebuildable.
/// </summary>
public sealed class SnapshotOptions
{
    public const string Section = "Backup";

    /// <summary>Folder that receives snapshots. Unset = snapshots off.</summary>
    public string? Directory { get; set; }

    /// <summary>How many snapshots to keep per kind (scheduled / pre-migration). Older ones are deleted.</summary>
    public int Keep { get; set; } = 7;

    /// <summary>Hours between scheduled snapshots. The cadence is anchored to the newest file on disk, so restarts neither skip nor duplicate.</summary>
    public int IntervalHours { get; set; } = 24;

    public bool Enabled => !string.IsNullOrWhiteSpace(Directory);

    public TimeSpan Interval => TimeSpan.FromHours(Math.Max(1, IntervalHours));

    /// <summary>
    /// Longest a deleted row can survive in a scheduled snapshot: shown to users on the privacy page and
    /// beside account deletion, because "permanently deleted" must stay true including backups.
    /// </summary>
    public int RetentionDays => (int)Math.Ceiling(Math.Max(1, Keep) * Interval.TotalDays);
}

/// <summary>
/// Consistent online copies of the SQLite database via SQLite's backup API (a plain file copy of a
/// database that is being written can be torn). Snapshots are ordinary SQLite files next to the live
/// database's data folder; restoring one is stop app, replace file, start app (see README).
/// </summary>
public static class DatabaseSnapshots
{
    public const string ScheduledPrefix = "scheduled";
    public const string PreMigrationPrefix = "premigrate";

    private const string TimestampFormat = "yyyyMMdd-HHmmss";

    /// <summary>
    /// Takes one snapshot and prunes older ones of the same kind. Returns the file written, or null when
    /// there is nothing to snapshot (snapshots off, in-memory database, database file not created yet).
    /// </summary>
    public static string? Take(string connectionString, SnapshotOptions options, string prefix, ILogger logger)
    {
        if (!options.Enabled) return null;

        var source = new SqliteConnectionStringBuilder(connectionString);
        if (source.Mode == SqliteOpenMode.Memory || source.DataSource == ":memory:" || !File.Exists(source.DataSource))
            return null;

        var directory = options.Directory!;
        System.IO.Directory.CreateDirectory(directory);

        var stamp = DateTime.UtcNow.ToString(TimestampFormat, CultureInfo.InvariantCulture);
        var final = Path.Combine(directory, $"profiler-{prefix}-{stamp}.db");
        for (var n = 1; File.Exists(final); n++)
            final = Path.Combine(directory, $"profiler-{prefix}-{stamp}-{n}.db");
        var temp = final + ".tmp";

        try
        {
            using (var src = new SqliteConnection(connectionString))
            using (var dst = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = temp, Pooling = false }.ConnectionString))
            {
                src.Open();
                dst.Open();
                src.BackupDatabase(dst);
            }
            File.Move(temp, final, overwrite: true);
        }
        catch
        {
            try { File.Delete(temp); } catch { /* best effort */ }
            throw;
        }

        var pruned = Prune(directory, prefix, options.Keep);
        logger.LogInformation("Database snapshot written: {Path} ({Pruned} older {Prefix} snapshot(s) removed)", final, pruned, prefix);
        return final;
    }

    /// <summary>UTC time of the newest snapshot of a kind, from its file name; null when there is none.</summary>
    public static DateTime? Newest(string directory, string prefix)
    {
        var names = List(directory, prefix);
        return names.Count == 0 ? null : StampOf(names[0]);
    }

    /// <summary>Deletes all but the newest <paramref name="keep"/> snapshots of a kind. Returns how many were removed.</summary>
    public static int Prune(string directory, string prefix, int keep)
    {
        keep = Math.Max(1, keep);
        var removed = 0;
        foreach (var path in List(directory, prefix).Skip(keep))
        {
            File.Delete(path);
            removed++;
        }
        return removed;
    }

    /// <summary>
    /// A snapshot right before migrations change an established database — the one moment a bad migration
    /// could destroy data with nothing to fall back on. Fresh databases (nothing applied yet) and databases
    /// that are already current are skipped. Returns the file written, or null.
    /// </summary>
    public static string? BeforeMigrate(DbContext db, SnapshotOptions options, ILogger logger)
    {
        if (!options.Enabled) return null;
        if (!db.Database.GetPendingMigrations().Any() || !db.Database.GetAppliedMigrations().Any()) return null;

        var connectionString = db.Database.GetConnectionString();
        return connectionString == null ? null : Take(connectionString, options, PreMigrationPrefix, logger);
    }

    // Newest first. The timestamp is zero-padded UTC, so name order is time order.
    private static List<string> List(string directory, string prefix)
    {
        if (!System.IO.Directory.Exists(directory)) return [];
        return System.IO.Directory.EnumerateFiles(directory, $"profiler-{prefix}-*.db")
            .OrderByDescending(Path.GetFileName, StringComparer.Ordinal)
            .ToList();
    }

    private static DateTime StampOf(string path)
    {
        var name = Path.GetFileNameWithoutExtension(path);
        var parts = name.Split('-');
        // profiler-<prefix>-<yyyyMMdd>-<HHmmss>[-n]
        if (parts.Length >= 4
            && DateTime.TryParseExact(parts[2] + "-" + parts[3], TimestampFormat, CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var stamp))
            return stamp;
        return File.GetLastWriteTimeUtc(path);
    }
}
