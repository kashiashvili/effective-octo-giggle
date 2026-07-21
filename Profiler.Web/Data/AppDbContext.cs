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
    public DbSet<FingerprintScheme> FingerprintSchemes => Set<FingerprintScheme>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<AppUser>(entity =>
        {
            // Usernames are treated case-insensitively for both uniqueness and login.
            // NOCASE collation makes "=" comparisons and the unique index case-insensitive.
            entity.Property(u => u.Username).UseCollation("NOCASE");
            entity.HasIndex(u => u.Username).IsUnique();
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
    }
}
