using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Profiler.Web.Data;
using Profiler.Web.Data.Models;
using Profiler.Web.Profile;
using Xunit;

namespace Profiler.Web.Tests.Integration;

/// <summary>
/// The product's central promise is that interests are turned into a fingerprint and then thrown
/// away. This asserts it against the database rather than against the code that was meant to do it:
/// distinctive interests go through the real fingerprinting and persistence path, then every row of
/// every table is searched for any trace of them.
/// </summary>
public class PrivacyGuaranteeTests : IClassFixture<ProfilerWebFactory>
{
    private readonly ProfilerWebFactory _factory;

    public PrivacyGuaranteeTests(ProfilerWebFactory factory) => _factory = factory;

    [Fact]
    public async Task RawInterests_NeverReachStorage()
    {
        // Strings that could not plausibly occur anywhere else in the database.
        var interests = new[]
        {
            "language:zzqx-marker-alpha",
            "topic:zzqx-marker-beta",
            "rss-keyword:zzqx-marker-gamma"
        };

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var user = new AppUser { Username = "privacy_" + Guid.NewGuid().ToString("N")[..8], PasswordHash = "x" };
        db.Users.Add(user);
        await db.SaveChangesAsync();

        // Exactly what SourcesController does with a connector's features.
        var generator = new FingerprintGenerator();
        var raw = generator.GenerateRaw(interests);
        db.SourceFingerprints.Add(new SourceFingerprintRecord
        {
            UserId = user.Id,
            Source = "RSS/Blogs",
            RawSignatureJson = JsonSerializer.Serialize(raw),
            FeatureCount = interests.Length
        });
        db.Fingerprints.Add(new FingerprintRecord
        {
            UserId = user.Id,
            FingerprintJson = FingerprintGenerator.FromRaw(raw).ToJson(),
            SourcesJson = JsonSerializer.Serialize(new[] { "RSS/Blogs" })
        });
        await db.SaveChangesAsync();

        // Everything the database holds, as text. Navigation properties make the object graph
        // cyclic, hence IgnoreCycles rather than a plain serialize.
        var json = new JsonSerializerOptions { ReferenceHandler = System.Text.Json.Serialization.ReferenceHandler.IgnoreCycles };
        var stored = string.Join("\n", new[]
        {
            JsonSerializer.Serialize(await db.Users.AsNoTracking().ToListAsync(), json),
            JsonSerializer.Serialize(await db.Fingerprints.AsNoTracking().ToListAsync(), json),
            JsonSerializer.Serialize(await db.SourceFingerprints.AsNoTracking().ToListAsync(), json),
            JsonSerializer.Serialize(await db.UserBlocks.AsNoTracking().ToListAsync(), json)
        });

        Assert.DoesNotContain("zzqx-marker", stored);
        foreach (var interest in interests)
            Assert.DoesNotContain(interest, stored);

        // The signature itself must still have been stored, or the assertion above proves nothing.
        Assert.Contains("RawSignatureJson", stored);
        Assert.Contains("\"FeatureCount\":3", stored);
    }
}
