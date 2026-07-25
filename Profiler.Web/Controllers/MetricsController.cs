using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Profiler.Web.Data;

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
[ApiController]
[AllowAnonymous]
[Route("metrics")]
public class MetricsController : ControllerBase
{
    private readonly AppDbContext _db;
    private readonly IConfiguration _config;

    public MetricsController(AppDbContext db, IConfiguration config)
    {
        _db = db;
        _config = config;
    }

    public const string TokenKey = "Metrics:Token";

    /// <summary>Returns a non-null result (404/401) when the caller is not the token-bearing operator.</summary>
    private IActionResult? RequireOperatorToken()
    {
        var configured = _config[TokenKey];
        // No token configured means the feature is off; do not even reveal that the route exists.
        if (string.IsNullOrWhiteSpace(configured)) return NotFound();

        var presented = ExtractBearer(Request.Headers.Authorization.ToString());
        if (presented == null || !FixedTimeEquals(presented, configured))
            return Unauthorized();
        return null;
    }

    [HttpGet("")]
    public async Task<IActionResult> Index()
    {
        if (RequireOperatorToken() is { } denied) return denied;

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

        return Ok(new
        {
            GeneratedAtUtc = DateTime.UtcNow,
            TotalUsers = totalUsers,
            WithFingerprint = await _db.Fingerprints.CountAsync(),
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
        if (RequireOperatorToken() is { } denied) return denied;

        var reports = await _db.UserReports
            .Join(_db.Users, r => r.ReportedId, u => u.Id,
                (r, u) => new { u.Username, r.Reason, r.ReporterId, r.CreatedAt })
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

    private static string? ExtractBearer(string? header)
    {
        if (string.IsNullOrEmpty(header)) return null;
        const string prefix = "Bearer ";
        return header.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
            ? header[prefix.Length..].Trim()
            : null;
    }

    private static bool FixedTimeEquals(string a, string b) =>
        CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(a), Encoding.UTF8.GetBytes(b));
}
