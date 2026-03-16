namespace Profiler.Web.ViewModels;

public class MatchViewModel
{
    public string Username { get; set; } = "";
    public double Similarity { get; set; }
    public int SimilarityPercent => (int)Math.Round(Similarity * 100);
    public List<string> SharedSources { get; set; } = new();
}
