using Microsoft.EntityFrameworkCore;
using Profiler.Web.Data;
using Xunit;

namespace Profiler.Web.Tests;

/// <summary>
/// The privacy page's "what we store" list is generated from DataInventory; this holds that list to
/// the EF model in both directions, so a table nobody described — or a description of a table that
/// no longer exists — fails the build rather than the promise.
/// </summary>
public class DataInventoryTests
{
    [Fact]
    public void EveryStoredEntity_IsDescribed_AndEveryDescription_NamesAStoredEntity()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>().UseSqlite("Data Source=:memory:").Options;
        using var db = new AppDbContext(options);
        var stored = db.Model.GetEntityTypes().Select(e => e.ClrType).Distinct().ToHashSet();
        var described = DataInventory.Kinds.Select(k => k.Entity).ToHashSet();

        Assert.True(stored.SetEquals(described),
            "undescribed: " + string.Join(", ", stored.Except(described).Select(t => t.Name)) +
            "; stale: " + string.Join(", ", described.Except(stored).Select(t => t.Name)));
        Assert.All(DataInventory.Kinds, k =>
        {
            Assert.False(string.IsNullOrWhiteSpace(k.Name));
            Assert.False(string.IsNullOrWhiteSpace(k.What));
            Assert.False(string.IsNullOrWhiteSpace(k.Control));

            // Column by column: a column nobody described, or a described column that is gone, fails here.
            var actual = db.Model.FindEntityType(k.Entity)!.GetProperties().Select(p => p.Name).OrderBy(n => n, StringComparer.Ordinal).ToArray();
            var described = k.Columns.OrderBy(n => n, StringComparer.Ordinal).ToArray();
            Assert.True(actual.SequenceEqual(described),
                $"{k.Entity.Name}: undescribed [{string.Join(", ", actual.Except(described))}], stale [{string.Join(", ", described.Except(actual))}]");
        });
    }
}
