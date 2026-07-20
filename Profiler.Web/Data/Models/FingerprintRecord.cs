namespace Profiler.Web.Data.Models;

public class FingerprintRecord
{
    public int UserId { get; set; }
    public AppUser User { get; set; } = null!;
    public string FingerprintJson { get; set; } = "";
    public string SourcesJson { get; set; } = "[]";
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
