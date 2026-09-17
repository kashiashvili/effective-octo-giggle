using Microsoft.EntityFrameworkCore;
using Profiler.Web.Data.Models;

namespace Profiler.Web.Data;

public class AppDbContext : DbContext
{
    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options) { }

    public DbSet<AppUser> Users => Set<AppUser>();
    public DbSet<FingerprintRecord> Fingerprints => Set<FingerprintRecord>();
    public DbSet<SourceFingerprintRecord> SourceFingerprints => Set<SourceFingerprintRecord>();
    public DbSet<UserBlock> UserBlocks => Set<UserBlock>();
    public DbSet<UserReport> UserReports => Set<UserReport>();
    public DbSet<FingerprintScheme> FingerprintSchemes => Set<FingerprintScheme>();
    public DbSet<Circle> Circles => Set<Circle>();
    public DbSet<CircleMembership> CircleMemberships => Set<CircleMembership>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<AppUser>(entity =>
        {
            // Usernames are treated case-insensitively for both uniqueness and login.
            // NOCASE collation makes "=" comparisons and the unique index case-insensitive.
            entity.Property(u => u.Username).UseCollation("NOCASE");
            entity.HasIndex(u => u.Username).IsUnique();

            // Non-unique on purpose. NOCASE already hard-blocks the ASCII-identical case; this index
            // just makes the registration-time lookalike check (which folds beyond ASCII) an indexed
            // lookup rather than a scan. It is not unique because a UNIQUE constraint would fail to
            // create on any existing database that already holds an accidental lookalike pair.
            entity.HasIndex(u => u.NormalizedUsername);
        });

        modelBuilder.Entity<FingerprintRecord>(entity =>
        {
            entity.HasKey(e => e.UserId);
            entity.HasOne(e => e.User)
                  .WithOne(u => u.Fingerprint)
                  .HasForeignKey<FingerprintRecord>(e => e.UserId);
        });

        modelBuilder.Entity<SourceFingerprintRecord>(entity =>
        {
            entity.HasIndex(e => new { e.UserId, e.Source }).IsUnique();
            entity.HasOne(e => e.User)
                  .WithMany()
                  .HasForeignKey(e => e.UserId);
        });

        modelBuilder.Entity<UserBlock>(entity =>
        {
            entity.HasIndex(e => new { e.BlockerId, e.BlockedId }).IsUnique();

            // A block is a fact about two people, so it must not outlive either of them: deleting an
            // account promises to remove all of that person's data. Cascades on both ends make that
            // structural rather than something the delete path has to remember.
            entity.HasOne<AppUser>()
                  .WithMany()
                  .HasForeignKey(e => e.BlockerId)
                  .OnDelete(DeleteBehavior.Cascade);
            entity.HasOne<AppUser>()
                  .WithMany()
                  .HasForeignKey(e => e.BlockedId)
                  .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<CircleMembership>(entity =>
        {
            entity.HasIndex(e => new { e.CircleId, e.UserId }).IsUnique();

            // Membership is a fact about a person and a circle and must not outlive either: deleting
            // an account removes all of that person's data, deleting a circle removes its memberships.
            entity.HasOne<AppUser>()
                  .WithMany()
                  .HasForeignKey(e => e.UserId)
                  .OnDelete(DeleteBehavior.Cascade);
            entity.HasOne<Circle>()
                  .WithMany()
                  .HasForeignKey(e => e.CircleId)
                  .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<UserReport>(entity =>
        {
            // At most one standing report per reporter→reported pair (re-reporting is idempotent).
            entity.HasIndex(e => new { e.ReporterId, e.ReportedId }).IsUnique();

            // Like a block, a report is a fact about two people and must not outlive either — deleting
            // an account removes every report it filed or received.
            entity.HasOne<AppUser>()
                  .WithMany()
                  .HasForeignKey(e => e.ReporterId)
                  .OnDelete(DeleteBehavior.Cascade);
            entity.HasOne<AppUser>()
                  .WithMany()
                  .HasForeignKey(e => e.ReportedId)
                  .OnDelete(DeleteBehavior.Cascade);
        });
    }
}
