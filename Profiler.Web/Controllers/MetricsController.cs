using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Profiler.Web.Data;
using Profiler.Web.Security;

namespace Profiler.Web.Controllers;

/// <summary>
/// Aggregate, non-identifying adoption metrics, so the owner can see whether the compatibility
/// signals are actually used before investing in more of them. It reports only counts and
/// distributions computed from columns already stored — never a user id, username, or any per-user
/// row — so it cannot rebuild a behaviour profile and adds no new retention.
///
/// Off unless a secret is configured (`Metrics:Token`), and then gated by a bearer token compared in
/// constant time. Counts were deliberately hidden from ordinary users elsewhere, so this must never
/// be reachable with a normal session — only with the operator's token.
/// </summary>
// Every action is operator-only. The token is checked by a resource filter that runs BEFORE model
// binding, so an [ApiController]'s automatic 400 on a bad body can never fire first and reveal that a
// route exists while the feature is off.
[ApiController]
[AllowAnonymous]
[OperatorToken]
[Route("metrics")]
public class MetricsController : ControllerBase
{
    private readonly AppDbContext _db;

    public MetricsController(AppDbContext db) => _db = db;

    // Kept for tests/config references; the actual gate is OperatorTokenAttribute.
    public const string TokenKey = OperatorTokenAttribute.TokenKey;

    [HttpGet("")]
    public async Task<IActionResult> Index()
    {
        var users = _db.Users;
        var totalUsers = await users.CountAsync();

        // Below this many users the per-category breakdowns can pin an individual's intent/values
        // (e.g. one user, one bucket), so they are withheld until the cohort is large enough for the
        // distribution to be genuinely aggregate. The plain totals stay.
        const int MinCohortForBreakdown = 10;
        var breakdownsShown = totalUsers >= MinCohortForBreakdown;

        var intentBreakdown = breakdownsShown
            ? await users.Where(u => u.ConnectionIntent != null)
                .GroupBy(u => u.ConnectionIntent!)
                .Select(g => new { Key = g.Key, Count = g.Count() })
                .ToDictionaryAsync(x => x.Key, x => x.Count)
            : null;

        var valuesDistribution = breakdownsShown
            ? await users.Where(u => u.ValuesOpenness != null)
                .GroupBy(u => u.ValuesOpenness!.Value)
                .Select(g => new { Bucket = g.Key, Count = g.Count() })
                .ToDictionaryAsync(x => x.Bucket.ToString(), x => x.Count)
            : null;

        // The funnel and the return signal — the questions a live cohort has to answer before any
        // further signal investment: did people get as far as a fingerprint, did they ever open the
        // match list, and did they come back to it after the first day? Computed from the two
        // timestamps already stored per account (registration, last plain match-list visit), so this
        // adds no tracking. The return check runs in memory over just the two dates of users who
        // viewed matches, rather than trusting date arithmetic translation to SQLite.
        var now = DateTime.UtcNow;
        var weekAgo = now.AddDays(-7);
        var viewers = await users.Where(u => u.LastMatchesViewedAt != null)
            .Select(u => new { u.CreatedAt, LastViewed = u.LastMatchesViewedAt!.Value })
            .ToListAsync();
        var returnedAfterFirstDay = viewers.Count(v => v.LastViewed - v.CreatedAt >= TimeSpan.FromDays(1));

        // How people fingerprint: self-described vs each connector, as a count of accounts per source.
        // Source types are already shown to every match ("shared source types"), so unlike intent and
        // values this is not sensitive and is not withheld below the cohort.
        var fingerprintsBySource = await _db.SourceFingerprints
            .GroupBy(s => s.Source)
            .Select(g => new { Source = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.Source, x => x.Count);

        return Ok(new
        {
            GeneratedAtUtc = now,
            TotalUsers = totalUsers,
            RegisteredLast7Days = await users.CountAsync(u => u.CreatedAt >= weekAgo),
            WithFingerprint = await _db.Fingerprints.CountAsync(),
            FingerprintsBySource = fingerprintsBySource,
            ViewedMatches = viewers.Count,
            ReturnedAfterFirstDay = returnedAfterFirstDay,
            ActiveLast7Days = await users.CountAsync(u => u.LastMatchesViewedAt >= weekAgo),
            Discoverable = await users.CountAsync(u => u.IsDiscoverable),
            WithBio = await users.CountAsync(u => u.Bio != null),
            WithContact = await users.CountAsync(u => u.Contact != null),
            WithConnectionIntent = await users.CountAsync(u => u.ConnectionIntent != null),
            WithValuesProfile = await users.CountAsync(u => u.ValuesOpenness != null),
            // Withheld (null) below the small-cohort threshold to prevent re-identification.
            BreakdownsWithheldBelowCohort = breakdownsShown ? (int?)null : MinCohortForBreakdown,
            ConnectionIntentBreakdown = intentBreakdown,
            ValuesOpennessDistribution = valuesDistribution,
        });
    }

    /// <summary>
    /// Operator-only moderation view: who has been reported, by how many distinct people, why, and when
    /// — enough for the operator to act. Unlike the aggregate metrics above this necessarily names users,
    /// because moderation is about specific accounts; it is behind the same operator token and never
    /// reachable with an ordinary session.
    /// </summary>
    [HttpGet("reports")]
    public async Task<IActionResult> Reports()
    {
        var reports = await _db.UserReports
            .Join(_db.Users, r => r.ReportedId, u => u.Id,
                (r, u) => new { u.Username, u.SuspendedAt, r.Reason, r.ReporterId, r.CreatedAt })
            .ToListAsync();

        var byUser = reports
            .GroupBy(r => r.Username)
            .Select(g => new
            {
                Username = g.Key,
                Reports = g.Count(),
                // Many reports from one person is weaker signal than a few from many different people.
                DistinctReporters = g.Select(x => x.ReporterId).Distinct().Count(),
                Reasons = g.GroupBy(x => x.Reason).ToDictionary(x => x.Key, x => x.Count()),
                LatestUtc = g.Max(x => x.CreatedAt),
                // So the operator sees at a glance which reported accounts are already actioned.
                Suspended = g.First().SuspendedAt != null,
            })
            .OrderByDescending(x => x.DistinctReporters)
            .ThenByDescending(x => x.Reports)
            .ToList();

        return Ok(new
        {
            GeneratedAtUtc = DateTime.UtcNow,
            TotalReports = reports.Count,
            ReportedUsers = byUser.Count,
            Users = byUser,
        });
    }

    /// <summary>
    /// Operator moderation action: suspend or reinstate an account by username, behind the operator
    /// token. A reversible flag, not a delete — a leaked token can suspend, but cannot destroy data.
    /// Suspending excludes the account from everyone's matches and refuses its sessions and logins.
    /// This is a bearer-token API call, not a browser form, so it carries no antiforgery token.
    /// </summary>
    public record SuspendRequest(string Username, bool Suspend);

    [HttpPost("suspend")]
    public async Task<IActionResult> Suspend([FromBody] SuspendRequest body)
    {
        if (body == null || string.IsNullOrWhiteSpace(body.Username)) return BadRequest();

        var user = await _db.Users.FirstOrDefaultAsync(u => u.Username == body.Username);
        if (user == null) return NotFound();

        user.SuspendedAt = body.Suspend ? DateTime.UtcNow : null;
        await _db.SaveChangesAsync();

        return Ok(new { user.Username, Suspended = user.SuspendedAt != null, user.SuspendedAt });
    }
}
