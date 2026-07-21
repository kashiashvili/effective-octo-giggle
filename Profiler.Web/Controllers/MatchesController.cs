using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Profiler.Web.Data;
using Profiler.Web.Security;
using Profiler.Web.Matching;
using Profiler.Web.Profile;
using Profiler.Web.ViewModels;
using System.Text.Json;

namespace Profiler.Web.Controllers;

[Route("matches")]
[Authorize]
public class MatchesController : Controller
{
    // Below this estimated Jaccard similarity a "match" is within MinHash sampling noise of
    // having nothing in common, so we don't present it as a match.
    private const double MinMatchSimilarity = 0.05;

    private readonly AppDbContext _db;

    public MatchesController(AppDbContext db) => _db = db;

    [HttpGet("")]
    public async Task<IActionResult> Index()
    {
        var userId = User.GetUserId();

        var myFp = await _db.Fingerprints.FirstOrDefaultAsync(f => f.UserId == userId);
        if (myFp == null)
        {
            ViewBag.NoFingerprint = true;
            return View(new List<MatchViewModel>());
        }

        var allFps = await _db.Fingerprints
            .Include(f => f.User)
            .ToListAsync();

        // People hidden either way: I hid them, or they hid me. Neither appears to the other.
        var hiddenUserIds = await _db.UserBlocks
            .Where(b => b.BlockerId == userId || b.BlockedId == userId)
            .Select(b => b.BlockerId == userId ? b.BlockedId : b.BlockerId)
            .ToListAsync();
        var hidden = hiddenUserIds.ToHashSet();

        // Candidates are the current user plus every *discoverable*, not-hidden other user. A user
        // who turned off discoverability keeps seeing their own matches but is excluded from others'.
        var matcher = new UserMatcher();
        foreach (var record in allFps)
        {
            if (record.UserId != userId && (!record.User.IsDiscoverable || hidden.Contains(record.UserId))) continue;
            matcher.Add(record.UserId.ToString(), ProfileFingerprint.FromJson(record.FingerprintJson), record.User.Username);
        }

        ViewBag.IsDiscoverable = allFps.FirstOrDefault(f => f.UserId == userId)?.User.IsDiscoverable ?? true;

        var mySources = JsonSerializer.Deserialize<List<string>>(myFp.SourcesJson) ?? new();

        var matches = matcher.FindMatches(userId.ToString(), minSimilarity: MinMatchSimilarity);
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
                SharedSources = mySources.Intersect(matchSources).ToList(),
                Bio = matchFp?.User.Bio,
                Contact = matchFp?.User.Contact
            };
        }).ToList();

        // Lets the empty state distinguish "you are the only user" from "others exist, none close yet".
        ViewBag.OthersExist = matcher.CandidateCount(userId.ToString()) > 0;
        return View(viewModels);
    }

    // Hiding is addressed by username rather than by row id: the username is already visible on the
    // match card, whereas putting the internal id in the page would hand out a sequential identifier
    // and, with it, a rough count of everyone registered.
    [HttpPost("hide")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Hide(string username)
    {
        var me = User.GetUserId();
        var target = await _db.Users.FirstOrDefaultAsync(u => u.Username == username);
        if (target == null || target.Id == me) return RedirectToAction(nameof(Index));

        if (!await _db.UserBlocks.AnyAsync(b => b.BlockerId == me && b.BlockedId == target.Id))
        {
            _db.UserBlocks.Add(new Data.Models.UserBlock { BlockerId = me, BlockedId = target.Id });
            await _db.SaveChangesAsync();
            TempData["Success"] = "Hidden. You won't see each other in matches anymore.";
        }
        return RedirectToAction(nameof(Index));
    }

    [HttpGet("hidden")]
    public async Task<IActionResult> Hidden()
    {
        var me = User.GetUserId();
        var blocked = await _db.UserBlocks
            .Where(b => b.BlockerId == me)
            .Join(_db.Users, b => b.BlockedId, u => u.Id, (b, u) => new MatchViewModel { Username = u.Username })
            .ToListAsync();
        return View(blocked);
    }

    [HttpPost("unhide")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Unhide(string username)
    {
        var me = User.GetUserId();
        var target = await _db.Users.FirstOrDefaultAsync(u => u.Username == username);
        if (target == null) return RedirectToAction(nameof(Hidden));

        var block = await _db.UserBlocks.FirstOrDefaultAsync(b => b.BlockerId == me && b.BlockedId == target.Id);
        if (block != null)
        {
            _db.UserBlocks.Remove(block);
            await _db.SaveChangesAsync();
            TempData["Success"] = "Unhidden. They can appear in your matches again.";
        }
        return RedirectToAction(nameof(Hidden));
    }
}
