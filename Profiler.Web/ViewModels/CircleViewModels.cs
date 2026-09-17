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

    /// <summary>Discoverable members who have built a fingerprint — the people whose pairs can be counted.</summary>
    public int WithFingerprint { get; set; }

    /// <summary>
    /// Pairs among those members whose overlap reaches the "Good match" tier — the organiser's "did it
    /// work?" number. Null below <see cref="PairStatsMinEligible"/> such members: a viewer already sees
    /// their own tiers on the circle page, so the count must span at least ten pairs they cannot see
    /// before it stops saying who overlaps with whom. Null above <see cref="PairStatsMaxEligible"/>.
    /// Members watching the count move as people join or leave can still infer a joiner's overlap with
    /// the group in aggregate; recorded as accepted in docs/DESIGN_CIRCLES.md.
    /// </summary>
    public int? GoodPairs { get; set; }

    public const int PairStatsMinEligible = 6;
    public const int PairStatsMaxEligible = 60;
}
