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
        });
    }
}
