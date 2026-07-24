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
    public async Task<IActionResult> Index(string? source)
    {
        var userId = User.GetUserId();

        // A themed summary of what a just-connected source contained, carried once via TempData.
        // Surfaced here so a brand-new user with no matches yet still lands on something about them.
        if (TempData["InterestLens"] is string lensJson)
        {
            ViewBag.InterestLens = JsonSerializer.Deserialize<List<InterestTheme>>(lensJson);
            ViewBag.InterestLensTotal = ViewBag.InterestLens is List<InterestTheme> themes
                ? themes.Sum(t => t.Count) : 0;
        }

        // Where a friend would sign up. Built from the current request so it is correct behind a
        // proxy (forwarded headers, when configured, have already rewritten scheme/host by here).
        ViewBag.InviteUrl = $"{Request.Scheme}://{Request.Host}/account/register";

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

        // The viewer's own intent, to spot a match who is here for the same thing.
        var myIntent = allFps.FirstOrDefault(f => f.UserId == userId)?.User.ConnectionIntent;
        // The viewer's own values bucket, to show coarse alignment on cards.
        var myValues = allFps.FirstOrDefault(f => f.UserId == userId)?.User.ValuesOpenness;

        var mySources = JsonSerializer.Deserialize<List<string>>(myFp.SourcesJson) ?? new();

        var matches = matcher.FindMatches(userId.ToString(), minSimilarity: MinMatchSimilarity);

        // Per-source signatures are only needed for the handful of people who actually matched, so
        // they are loaded after ranking rather than for everyone with a fingerprint.
        var matchedIds = matches.Select(m => int.Parse(m.UserId)).ToList();
        var perSource = (await _db.SourceFingerprints
                .Where(s => s.UserId == userId || matchedIds.Contains(s.UserId))
                .Select(s => new { s.UserId, s.Source, s.RawSignatureJson })
                .ToListAsync())
            .GroupBy(s => s.UserId)
            .ToDictionary(
                g => g.Key,
                g => g.ToDictionary(
                    s => s.Source,
                    s => JsonSerializer.Deserialize<ulong[]>(s.RawSignatureJson) ?? Array.Empty<ulong>()));

        perSource.TryGetValue(userId, out var mySignatures);

        var viewModels = matches.Select(m =>
        {
            var matchFp = allFps.FirstOrDefault(f => f.UserId.ToString() == m.UserId);
            var matchSources = matchFp != null
                ? JsonSerializer.Deserialize<List<string>>(matchFp.SourcesJson) ?? new()
                : new List<string>();
            var shared = mySources.Intersect(matchSources).ToList();
            var overlaps = new List<SharedSourceOverlap>();
            if (mySignatures != null && perSource.TryGetValue(int.Parse(m.UserId), out var theirSignatures))
            {
                foreach (var source in shared)
                {
                    if (!mySignatures.TryGetValue(source, out var mine)) continue;
                    if (!theirSignatures.TryGetValue(source, out var theirs)) continue;
                    overlaps.Add(new SharedSourceOverlap
                    {
                        Source = source,
                        Similarity = FingerprintGenerator.RawSimilarity(mine, theirs)
                    });
                }
                overlaps.Sort((a, b) => b.Similarity.CompareTo(a.Similarity));
            }

            // Discoverability was enforced in one direction only: an account could stay permanently
            // invisible while reading everyone's contact line on every refresh, and could never be
            // hidden by the people it read, since hiding someone requires seeing their card first.
            // The contact line is the only genuinely personal thing stored here, so the toggle is
            // reciprocal for it.
            var iAmVisible = ViewBag.IsDiscoverable ?? true;

            return new MatchViewModel
            {
                Username = m.Username,
                Similarity = m.Similarity,
                SharedSources = shared,
                SharedSourceOverlaps = overlaps,
                Bio = iAmVisible ? matchFp?.User.Bio : null,
                Contact = iAmVisible ? matchFp?.User.Contact : null,
                // A separate, explainable signal shown alongside interests — never blended into the
                // similarity score. Withheld while hidden, like the other personal fields.
                ConnectionIntentLabel = iAmVisible ? ConnectionIntent.LabelFor(matchFp?.User.ConnectionIntent) : null,
                SharesViewerIntent = iAmVisible
                    && !string.IsNullOrEmpty(myIntent)
                    && matchFp?.User.ConnectionIntent == myIntent,
                // Coarse values alignment, only when both sides took it and the viewer is visible.
                ValuesAlignmentLabel = iAmVisible
                    ? ValuesQuestionnaire.AlignmentLabel(myValues, matchFp?.User.ValuesOpenness)
                    : null,
                UpdatedAt = matchFp?.UpdatedAt ?? DateTime.UtcNow
            };
        }).ToList();

        // Lets the empty state distinguish "you are the only user" from "others exist, none close yet".
        ViewBag.OthersExist = matcher.CandidateCount(userId.ToString()) > 0;

        var me = allFps.FirstOrDefault(f => f.UserId == userId)?.User;

        // Reaching out is the point of the whole product, and it only works if at least one side
        // published a way to be reached. Someone who left their own contact blank sees a list of
        // people who cannot answer them, with nothing explaining why.
        ViewBag.HasOwnContact = !string.IsNullOrWhiteSpace(me?.Contact);

        // Adoption nudge for the new signal: a viewer who hasn't said what they're here for is told
        // once, so matches can see it and the mutual highlight has something to work with.
        ViewBag.HasOwnIntent = !string.IsNullOrWhiteSpace(me?.ConnectionIntent);

        // A reason to come back: how many of these matches have refreshed their interests since the
        // last time this person looked. Suppressed on the very first visit (nothing to compare to)
        // and when there is nothing new. The read updates the marker, so "since last visit" always
        // means since the previous load.
        if (me != null)
        {
            if (me.LastMatchesViewedAt is { } since)
                ViewBag.NewSinceLastVisit = viewModels.Count(m => m.UpdatedAt > since);
            me.LastMatchesViewedAt = DateTime.UtcNow;
            await _db.SaveChangesAsync();
        }

        // Let the person narrow to the interest area they actually came for — the vision is niche
        // interests, so "show me the people I share Music with" is the core move. Filtering is a
        // view concern only: the retention count and empty-state distinction above are computed on
        // the full set, so a filter never changes what "new since last visit" means.
        var available = viewModels
            .SelectMany(m => m.SharedSources)
            .Distinct()
            .OrderBy(s => s, StringComparer.Ordinal)
            .ToList();
        ViewBag.AvailableSources = available;

        // Ignore an unknown value rather than showing an empty page for a source nobody shares.
        var activeFilter = !string.IsNullOrWhiteSpace(source) && available.Contains(source) ? source : null;
        ViewBag.SourceFilter = activeFilter;
        ViewBag.HasAnyMatch = viewModels.Count > 0;

        var shown = activeFilter == null
            ? viewModels
            : viewModels.Where(m => m.SharedSources.Contains(activeFilter)).ToList();

        return View(shown);
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
