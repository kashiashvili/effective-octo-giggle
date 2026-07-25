namespace Profiler.Web.Security;

/// <summary>
/// Deployment-level product switches, read from configuration. These let the owner make a shipped
/// signal opt-in-at-deploy without a code change — the mechanism behind the recorded owner decisions
/// (see docs/OWNER_DECISIONS.md).
/// </summary>
public sealed class FeatureFlags
{
    private readonly IConfiguration _config;
    public FeatureFlags(IConfiguration config) => _config = config;

    /// <summary>
    /// The optional values/outlook signal (questionnaire, the coarse alignment line on match cards, and
    /// the "Similar outlook first" sort). Default ON — current behaviour. Set <c>Signals:ValuesEnabled=false</c>
    /// to hide all three: it collects the most sensitive data for the least-validated signal (evidence:
    /// the axis clumps 95% of people into −1..+1 and the sort reorders heavily on that weak signal — see
    /// ValuesSignalResolutionTests / ValuesSortImpactTests), so hiding it by default is the data-minimizing
    /// choice until the signal is strengthened. Stored buckets are kept, so it re-enables cleanly.
    /// </summary>
    public bool ValuesSignalEnabled => _config.GetValue("Signals:ValuesEnabled", true);
}
