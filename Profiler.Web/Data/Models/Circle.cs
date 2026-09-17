namespace Profiler.Web.Data.Models;

/// <summary>
/// A named group people join through a shareable invite link, so a meetup, a course cohort or a
/// team can find each other first. Membership only ever adds a "Same circle" chip and a sort on
/// the match list; it is never a filter and never part of any score (docs/DESIGN_CIRCLES.md).
/// No owner, no admin: any member can share the current invite link. A circle with no members
/// left is deleted with the last membership.
/// </summary>
public class Circle
{
    public int Id { get; set; }

    /// <summary>User-authored, validated by TextPolicy, always rendered as plain text.</summary>
    public string Name { get; set; } = "";

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

/// <summary>
/// One person's membership of one circle — the only fact stored: no inviter, no source, no activity.
/// Created only by an explicit "Join" on a page that names the circle; removed by "Leave" or with
/// the account (cascade on both ends, and explicitly on delete, like blocks).
/// </summary>
public class CircleMembership
{
    public int Id { get; set; }
    public int CircleId { get; set; }
    public int UserId { get; set; }
    public DateTime JoinedAt { get; set; } = DateTime.UtcNow;
}
