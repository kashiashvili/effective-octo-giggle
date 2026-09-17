namespace Profiler.Web.ViewModels;

/// <summary>The invite landing page: what you were invited to, and what to do next.</summary>
public class CircleJoinViewModel
{
    public string CircleName { get; set; } = "";
    public string Token { get; set; } = "";
    public int MemberCount { get; set; }
    public bool IsSignedIn { get; set; }
    public bool AlreadyMember { get; set; }
}

/// <summary>One of the viewer's circles on the dashboard, with its current invite link.</summary>
public class CircleSummaryViewModel
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public int MemberCount { get; set; }
    public string InviteUrl { get; set; } = "";
}
