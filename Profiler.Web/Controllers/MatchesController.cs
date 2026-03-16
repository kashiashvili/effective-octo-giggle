using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Profiler.Web.Data;
using Profiler.Web.Matching;
using Profiler.Web.Profile;
using Profiler.Web.ViewModels;
using System.Text.Json;

namespace Profiler.Web.Controllers;

[Route("matches")]
public class MatchesController : Controller
{
    private readonly AppDbContext _db;

    public MatchesController(AppDbContext db) => _db = db;

    [HttpGet("")]
    public async Task<IActionResult> Index()
    {
        if (HttpContext.Session.GetInt32("UserId") is null)
            return RedirectToAction("Login", "Account");

        var userId = HttpContext.Session.GetInt32("UserId")!.Value;

        var myFp = await _db.Fingerprints.FirstOrDefaultAsync(f => f.UserId == userId);
        if (myFp == null)
        {
            ViewBag.NoFingerprint = true;
            return View(new List<MatchViewModel>());
        }

        var allFps = await _db.Fingerprints
            .Include(f => f.User)
            .ToListAsync();

        var matcher = new UserMatcher();
        foreach (var record in allFps)
            matcher.Add(record.UserId.ToString(), ProfileFingerprint.FromJson(record.FingerprintJson), record.User.Username);

        var mySources = JsonSerializer.Deserialize<List<string>>(myFp.SourcesJson) ?? new();

        var matches = matcher.FindMatches(userId.ToString());
        var viewModels = matches.Select(m =>
        {
            var matchFp = allFps.FirstOrDefault(f => f.UserId.ToString() == m.UserId);
            var matchSources = matchFp != null
                ? JsonSerializer.Deserialize<List<string>>(matchFp.SourcesJson) ?? new()
                : new List<string>();
            return new MatchViewModel
            {
                Username = m.Username,
                Similarity = m.Similarity,
                SharedSources = mySources.Intersect(matchSources).ToList()
            };
        }).ToList();

        return View(viewModels);
    }
}
