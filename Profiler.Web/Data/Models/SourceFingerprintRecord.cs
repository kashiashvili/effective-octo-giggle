namespace Profiler.Web.Data.Models;

/// <summary>
/// Raw (full-width) MinHash signature for a single connected source.
/// Stored per source so users can add or remove sources without re-supplying the others;
/// the matching fingerprint is the element-wise minimum across a user's records.
/// Contains no raw feature data.
/// </summary>
public class SourceFingerprintRecord
{
    public int Id { get; set; }
    public int UserId { get; set; }
    public AppUser User { get; set; } = null!;
    public string Source { get; set; } = "";
    public string RawSignatureJson { get; set; } = "[]";

    /// <summary>How many distinct interest signals this source contributed. A count only —
    /// not the signals themselves, which are never stored.</summary>
    public int FeatureCount { get; set; }

    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
