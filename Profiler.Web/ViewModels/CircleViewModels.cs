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

/// <summary>A circle's page for a member: everyone in it, against the viewer.</summary>
public class CircleViewModel
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public int MemberCount { get; set; }
    public string InviteUrl { get; set; } = "";
    public bool ViewerVisible { get; set; }
    public bool ViewerHasFingerprint { get; set; }
    public List<CircleMemberViewModel> Members { get; set; } = new();
}

/// <summary>One circle-mate as the viewer sees them: overlap where there is one, personal lines by reciprocity.</summary>
public class CircleMemberViewModel
{
    public string Username { get; set; } = "";
    public bool HasFingerprint { get; set; }
    public double Similarity { get; set; }
    public List<string> SharedSources { get; set; } = new();
    public string? Bio { get; set; }
    public string? Contact { get; set; }

    /// <summary>At or above the match list's floor the usual tier applies; below it there is nothing honest to say but "not yet".</summary>
    public bool AboveFloor => Similarity >= Controllers.MatchesController.MinMatchSimilarity;

    // The tier thresholds live on the match card; one instance reuses them.
    private MatchViewModel? _asMatch;
    private MatchViewModel AsMatch => _asMatch ??= new MatchViewModel { Similarity = Similarity };
    public string TierLabel => AsMatch.TierLabel;
    public string Tier => AsMatch.Tier;
    public int SimilarityPercent => AsMatch.SimilarityPercent;
}

/// <summary>One of the viewer's circles on the dashboard, with its current invite link.</summary>
public class CircleSummaryViewModel
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public int MemberCount { get; set; }
    public string InviteUrl { get; set; } = "";
}
