using Microsoft.EntityFrameworkCore;
using Profiler.Web.Data.Models;

namespace Profiler.Web.Data;

public class AppDbContext : DbContext
{
    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options) { }

    public DbSet<AppUser> Users => Set<AppUser>();
    public DbSet<FingerprintRecord> Fingerprints => Set<FingerprintRecord>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<FingerprintRecord>(entity =>
        {
            entity.HasKey(e => e.UserId);
            entity.HasOne(e => e.User)
                  .WithOne(u => u.Fingerprint)
                  .HasForeignKey<FingerprintRecord>(e => e.UserId);
        });
    }
}
