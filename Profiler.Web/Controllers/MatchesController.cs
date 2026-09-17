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
    public const double MinMatchSimilarity = 0.05;

    /// <summary>How many people a match list holds, in either direction.</summary>
    private const int TopMatches = 20;

    private readonly AppDbContext _db;
    private readonly Security.FeatureFlags _flags;
    private readonly Security.CircleInvites _invites;

    public MatchesController(AppDbContext db, Security.FeatureFlags flags, Security.CircleInvites invites)
    {
        _db = db;
        _flags = flags;
        _invites = invites;
    }

    [HttpGet("")]
    public async Task<IActionResult> Index(string? source, string? sort)
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

        // The store holds the current user plus every *discoverable*, not-suspended other user; who is
        // hidden between two people is applied per query, so the same store can also answer "would
        // they see me?" for each match below. A user who turned off discoverability keeps seeing their
        // own matches but is excluded from others'.
        var matcher = new UserMatcher();
        foreach (var record in allFps)
        {
            // A suspended account is excluded from everyone's matches, on top of the discoverable rule.
            if (record.UserId != userId && (!record.User.IsDiscoverable || record.User.SuspendedAt != null)) continue;
            matcher.Add(record.UserId.ToString(), ProfileFingerprint.FromJson(record.FingerprintJson), record.User.Username);
        }
        bool HiddenFromMe(string uid) => hidden.Contains(int.Parse(uid));

        ViewBag.IsDiscoverable = allFps.FirstOrDefault(f => f.UserId == userId)?.User.IsDiscoverable ?? true;

        // The viewer's own intent, to spot a match who is here for the same thing.
        var myIntent = allFps.FirstOrDefault(f => f.UserId == userId)?.User.ConnectionIntent;
        // The viewer's own values bucket, to show coarse alignment on cards. When the values signal is
        // disabled at the deployment level, treat it as absent everywhere — this alone removes the card
        // alignment line (needs both sides set) and the "Similar outlook first" sort (offered only when
        // the viewer has values), with no other branching.
        var myValues = _flags.ValuesSignalEnabled
            ? ValuesProfile.FromJson(allFps.FirstOrDefault(f => f.UserId == userId)?.User.ValuesProfileJson)
            : null;
        ViewBag.ValuesEnabled = _flags.ValuesSignalEnabled;

        // The viewer's own showable interests, to surface the ones a match also chose to show as shared
        // conversation hooks.
        var myShowable = Profiler.Web.Profile.ShowableInterests.Deserialize(
            allFps.FirstOrDefault(f => f.UserId == userId)?.User.ShowableInterestsJson);

        var mySources = JsonSerializer.Deserialize<List<string>>(myFp.SourcesJson) ?? new();

        var matches = matcher.FindMatches(userId.ToString(), topK: TopMatches, minSimilarity: MinMatchSimilarity, exclude: HiddenFromMe);

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

        // Circles both sides are in: one query over the viewer's memberships joined against the people
        // who matched. Withheld while the viewer is hidden, like every other reciprocal field, and
        // absent entirely when circles are disabled at the deployment level.
        var viewerVisible = (bool)(ViewBag.IsDiscoverable ?? true);
        var myCircleIds = _flags.CirclesEnabled
            ? await _db.CircleMemberships.Where(m => m.UserId == userId).Select(m => m.CircleId).ToListAsync()
            : new List<int>();
        var sharedCircles = myCircleIds.Count > 0 && viewerVisible
            ? (await _db.CircleMemberships
                    .Where(m => myCircleIds.Contains(m.CircleId) && matchedIds.Contains(m.UserId))
                    .Join(_db.Circles, m => m.CircleId, c => c.Id, (m, c) => new { m.UserId, c.Name })
                    .ToListAsync())
                .GroupBy(x => x.UserId)
                .ToDictionary(g => g.Key, g => g.Select(x => x.Name).OrderBy(n => n, StringComparer.OrdinalIgnoreCase).ToList())
            : new Dictionary<int, List<string>>();
        ViewBag.HasOwnCircles = myCircleIds.Count > 0 && viewerVisible;

        // Would they see me? The list is cut at the top 20 in each direction, so a strong match here
        // can be a stranger to them. For each match, their own top list is computed from the same
        // store with the people hidden between *them* and others left out; the badge is shown only
        // when the viewer is in it, and never while the viewer is hidden (a hidden account is in
        // nobody's list). Request-time only, nothing stored.
        var ranksMeToo = new HashSet<int>();
        if (viewerVisible && matchedIds.Count > 0)
        {
            var theirBlocks = await _db.UserBlocks
                .Where(b => matchedIds.Contains(b.BlockerId) || matchedIds.Contains(b.BlockedId))
                .Select(b => new { b.BlockerId, b.BlockedId })
                .ToListAsync();
            // Similarity is symmetric, so the viewer's score in their list is the score already in hand:
            // the viewer is in their top list when fewer than TopMatches people rank strictly closer to
            // them (excluding the people hidden between them and others). A count with an early exit —
            // no list built, no sort, ties in the viewer's favour.
            foreach (var m in matches)
            {
                var matchedId = int.Parse(m.UserId);
                var theirHidden = theirBlocks
                    .Where(b => b.BlockerId == matchedId || b.BlockedId == matchedId)
                    .Select(b => b.BlockerId == matchedId ? b.BlockedId : b.BlockerId)
                    .ToHashSet();
                var closer = matcher.CountCloserThan(m.UserId, m.Similarity,
                    exclude: uid => theirHidden.Contains(int.Parse(uid)), stopAt: TopMatches);
                if (closer < TopMatches) ranksMeToo.Add(matchedId);
            }
        }

        var viewModels = matches.Select(m =>
        {
            var matchFp = allFps.FirstOrDefault(f => f.UserId.ToString() == m.UserId);
            var theirValues = _flags.ValuesSignalEnabled ? ValuesProfile.FromJson(matchFp?.User.ValuesProfileJson) : null;
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

            // Withheld while hidden, like bio/contact.
            var showableForCard = iAmVisible
                ? Profiler.Web.Profile.ShowableInterests.Deserialize(matchFp?.User.ShowableInterestsJson)
                : new List<string>();

            return new MatchViewModel
            {
                Username = m.Username,
                Similarity = m.Similarity,
                SharedSources = shared,
                SharedSourceOverlaps = overlaps,
                // Opt-in public interests, shown only when the viewer is visible (same reciprocity as
                // bio/contact). The ones the viewer also shows are the shared hooks, led with on the card.
                ShowableInterests = showableForCard,
                SharedShowableInterests = iAmVisible
                    ? Profiler.Web.Profile.ShowableInterests.Common(myShowable, showableForCard)
                    : new List<string>(),
                SharedCircles = sharedCircles.TryGetValue(int.Parse(m.UserId), out var circles) ? circles : new List<string>(),
                RanksYouToo = ranksMeToo.Contains(int.Parse(m.UserId)),
                Bio = iAmVisible ? matchFp?.User.Bio : null,
                Contact = iAmVisible ? matchFp?.User.Contact : null,
                // A separate, explainable signal shown alongside interests — never blended into the
                // similarity score. Withheld while hidden, like the other personal fields.
                ConnectionIntentLabel = iAmVisible ? ConnectionIntent.LabelFor(matchFp?.User.ConnectionIntent) : null,
                SharesViewerIntent = iAmVisible
                    && !string.IsNullOrEmpty(myIntent)
                    && matchFp?.User.ConnectionIntent == myIntent,
                // Coarse, explained values alignment, only when both sides took it and the viewer is visible.
                ValuesAlignmentLabel = iAmVisible ? ValuesQuestionnaire.AlignmentLabel(myValues, theirValues) : null,
                ValuesAlignmentReason = iAmVisible ? ValuesQuestionnaire.AlignmentReason(myValues, theirValues) : null,
                WorldAlignmentLabel = iAmVisible ? ValuesQuestionnaire.WorldLabel(myValues, theirValues) : null,
                ValuesAlignmentRank = iAmVisible ? ValuesQuestionnaire.AlignmentRank(myValues, theirValues) : int.MaxValue,
                UpdatedAt = matchFp?.UpdatedAt ?? DateTime.UtcNow
            };
        }).ToList();

        // Lets the empty state distinguish "you are the only user" from "others exist, none close yet".
        ViewBag.OthersExist = matcher.CandidateCount(userId.ToString(), HiddenFromMe) > 0;

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

            // Advance the "last visited" marker only on the plain view. Re-stamping it on every load
            // — a sort/filter click, a second tab, a link prefetch — would immediately zero the "new
            // since last visit" count the marker exists to produce.
            if (string.IsNullOrEmpty(source) && string.IsNullOrEmpty(sort))
            {
                me.LastMatchesViewedAt = DateTime.UtcNow;
                await _db.SaveChangesAsync();
            }
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

        // Only the empty state shows the invite box. Someone in a circle shares that circle's link
        // instead of the plain register link: the friend then lands in the circle and is marked on
        // the match list, which is what an invite from a person means. Looked up (and a token minted)
        // only when the box will actually render.
        if (viewModels.Count == 0 && _flags.CirclesEnabled)
        {
            var newest = await _db.CircleMemberships
                .Where(m => m.UserId == userId)
                .OrderByDescending(m => m.JoinedAt).ThenByDescending(m => m.Id)
                .Join(_db.Circles, m => m.CircleId, c => c.Id, (m, c) => new { c.Id, c.Name })
                .FirstOrDefaultAsync();
            if (newest != null)
            {
                ViewBag.InviteUrl = $"{Request.Scheme}://{Request.Host}/circles/join/{_invites.Issue(newest.Id)}";
                ViewBag.InviteCircleName = newest.Name;
                ViewBag.InviteCircleId = newest.Id;
            }
        }

        var shown = activeFilter == null
            ? viewModels
            : viewModels.Where(m => m.SharedSources.Contains(activeFilter)).ToList();

        // An explicit, user-chosen ordering — never a hidden blended score. Interest similarity is
        // still the default and the tiebreaker; "sort" only lifts matches that share the viewer's
        // intent, or read as a similar outlook, to the top. OrderByDescending is stable, so within
        // each group the interest ranking is preserved. Options are only offered (in the view) when
        // the viewer has the matching signal set, so the sort always means something.
        var viewerHasIntent = !string.IsNullOrEmpty(myIntent);
        var viewerHasValues = myValues != null && viewerVisible;
        var viewerHasCircles = myCircleIds.Count > 0 && viewerVisible;
        ViewBag.HasOwnValues = viewerHasValues;

        var sortMode = sort switch
        {
            "intent" when viewerHasIntent => "intent",
            "values" when viewerHasValues => "values",
            "circle" when viewerHasCircles => "circle",
            _ => null,
        };
        ViewBag.SortMode = sortMode;
        var ordered = sortMode switch
        {
            "intent" => shown.OrderByDescending(m => m.SharesViewerIntent).ToList(),
            "values" => shown.OrderBy(m => m.ValuesAlignmentRank).ToList(),
            // People from a circle you share first; interest order preserved within each group.
            "circle" => shown.OrderByDescending(m => m.SharedCircles.Count > 0).ToList(),
            _ => shown,
        };

        return View(ordered);
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

    // Reporting records operator-facing moderation data AND hides the reported user from the reporter,
    // so reporting is also immediate self-protection. The reason is a closed-set key, never free text.
    [HttpPost("report")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Report(string username, string reason)
    {
        var me = User.GetUserId();
        var target = await _db.Users.FirstOrDefaultAsync(u => u.Username == username);
        if (target == null || target.Id == me) return RedirectToAction(nameof(Index));

        // Off-list reason (only possible from a tampered form) falls back to "other" rather than failing.
        var reasonKey = Profiler.Web.Profile.ReportReason.IsValid(reason) ? reason : "other";

        if (!await _db.UserReports.AnyAsync(r => r.ReporterId == me && r.ReportedId == target.Id))
            _db.UserReports.Add(new Data.Models.UserReport { ReporterId = me, ReportedId = target.Id, Reason = reasonKey });

        // Reporting also hides them from you — you should not have to keep seeing someone you reported.
        if (!await _db.UserBlocks.AnyAsync(b => b.BlockerId == me && b.BlockedId == target.Id))
            _db.UserBlocks.Add(new Data.Models.UserBlock { BlockerId = me, BlockedId = target.Id });

        try
        {
            await _db.SaveChangesAsync();
        }
        catch (DbUpdateException)
        {
            // A concurrent double-submit (double-click) races past the existence checks and hits the
            // unique reporter→reported / blocker→blocked index. That is exactly the idempotent outcome
            // intended — the report and block already exist — so treat it as success, not a 500.
        }
        TempData["Success"] = "Thanks — we've recorded your report and hidden them from your matches.";
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
