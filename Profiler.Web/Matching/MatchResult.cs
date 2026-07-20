namespace Profiler.Web.Matching;

public class MatchResult
{
    public string UserId { get; set; } = "";
    public string Username { get; set; } = "";
    public double Similarity { get; set; }
    public List<string> SharedSources { get; set; } = new();
}
