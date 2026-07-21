namespace Profiler.Web.ViewModels;

public class SourceStatusViewModel
{
    /// <summary>A snapshot older than this matches people on out-of-date interests.</summary>
    public const int StaleAfterDays = 90;

    public string Source { get; set; } = "";
    public int FeatureCount { get; set; }
    public DateTime UpdatedAt { get; set; }

    public bool IsStale => (DateTime.UtcNow - UpdatedAt).TotalDays > StaleAfterDays;
}
