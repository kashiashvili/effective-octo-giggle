namespace Profiler.Web.Data.Models;

/// <summary>
/// One user hiding another from matches. Hiding is symmetric in effect: if either party has a
/// block against the other, neither appears in the other's match results.
/// </summary>
public class UserBlock
{
    public int Id { get; set; }
    public int BlockerId { get; set; }
    public int BlockedId { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
