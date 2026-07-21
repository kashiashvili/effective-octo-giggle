namespace Profiler.Web.Data.Models;

/// <summary>
/// Records which fingerprint scheme the stored signatures were built under. There is exactly one
/// row. If the pepper changes, every signature in the database was produced by a different hash
/// family and can no longer be compared with anything — so the app notices and clears them, rather
/// than leaving everyone quietly matching nobody.
/// </summary>
public class FingerprintScheme
{
    public int Id { get; set; }

    /// <summary>Derived from the pepper and reveals nothing about it.</summary>
    public string Verifier { get; set; } = "";

    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
