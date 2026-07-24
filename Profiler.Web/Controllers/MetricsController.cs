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

    [HttpGet("")]
    public async Task<IActionResult> Index()
    {
        var configured = _config[TokenKey];
        // No token configured means the feature is off; do not even reveal that the route exists.
        if (string.IsNullOrWhiteSpace(configured)) return NotFound();

        var presented = ExtractBearer(Request.Headers.Authorization.ToString());
        if (presented == null || !FixedTimeEquals(presented, configured))
            return Unauthorized();

        var users = _db.Users;

        var intentBreakdown = await users
            .Where(u => u.ConnectionIntent != null)
            .GroupBy(u => u.ConnectionIntent!)
            .Select(g => new { Key = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.Key, x => x.Count);

        var valuesDistribution = await users
            .Where(u => u.ValuesOpenness != null)
            .GroupBy(u => u.ValuesOpenness!.Value)
            .Select(g => new { Bucket = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.Bucket.ToString(), x => x.Count);

        return Ok(new
        {
            GeneratedAtUtc = DateTime.UtcNow,
            TotalUsers = await users.CountAsync(),
            WithFingerprint = await _db.Fingerprints.CountAsync(),
            Discoverable = await users.CountAsync(u => u.IsDiscoverable),
            WithBio = await users.CountAsync(u => u.Bio != null),
            WithContact = await users.CountAsync(u => u.Contact != null),
            WithConnectionIntent = await users.CountAsync(u => u.ConnectionIntent != null),
            ConnectionIntentBreakdown = intentBreakdown,
            WithValuesProfile = await users.CountAsync(u => u.ValuesOpenness != null),
            ValuesOpennessDistribution = valuesDistribution,
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
