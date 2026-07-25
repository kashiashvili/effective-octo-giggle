namespace Profiler.Web.Data.Models;

/// <summary>
/// One user reporting another for abuse. Operator-facing moderation data: the product pushes contact
/// off-platform, so without a report path the only recourse is a silent one-sided hide and a bad actor
/// stays in everyone else's pool. The reason is a key from a closed set (see
/// <see cref="Profile.ReportReason"/>) — never free text about another person.
/// </summary>
public class UserReport
{
    public int Id { get; set; }
    public int ReporterId { get; set; }
    public int ReportedId { get; set; }
    public string Reason { get; set; } = "other";
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
