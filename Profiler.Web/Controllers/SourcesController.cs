using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Profiler.Web.Connectors;
using Profiler.Web.Data;
using Profiler.Web.Security;
using Profiler.Web.Data.Models;
using Profiler.Web.Profile;
using Profiler.Web.ViewModels;

namespace Profiler.Web.Controllers;

[Route("sources")]
[Authorize]
public class SourcesController : Controller
{
    private const long MaxCsvUploadBytes = 10 * 1024 * 1024;

    // The source name for interests a user picked themselves rather than deriving from a connector.
    // Stored like any other source so export, deletion, disconnect, and matching treat it uniformly.
    public const string SelfDescribedSource = "Self-described";

    private readonly AppDbContext _db;
    private readonly IHttpClientFactory _httpFactory;
    // Injected rather than constructed here: it carries the deployment's pepper, and a generator
    // built without it would write signatures nothing else could compare.
    private readonly FingerprintGenerator _generator;
    private readonly Security.FeatureFlags _flags;

    private readonly Security.CircleInvites _invites;

    public SourcesController(AppDbContext db, IHttpClientFactory httpFactory, FingerprintGenerator generator, Security.FeatureFlags flags, Security.CircleInvites invites)
    {
        _db = db;
        _httpFactory = httpFactory;
        _generator = generator;
        _flags = flags;
        _invites = invites;
    }

    private int CurrentUserId => User.GetUserId();

    [HttpGet("dashboard")]
    public async Task<IActionResult> Dashboard()
    {
        var userId = CurrentUserId;

        ViewBag.HasFingerprint = await _db.Fingerprints.AnyAsync(f => f.UserId == userId);
        ViewBag.Sources = await _db.SourceFingerprints
            .Where(s => s.UserId == userId)
            .OrderByDescending(s => s.FeatureCount)
            .ThenBy(s => s.Source)
            .Select(s => new SourceStatusViewModel { Source = s.Source, FeatureCount = s.FeatureCount, UpdatedAt = s.UpdatedAt })
            .ToListAsync();

        var user = await _db.Users.FirstAsync(u => u.Id == userId);
        ViewBag.Bio = user.Bio;
        ViewBag.Contact = user.Contact;
        ViewBag.ConnectionIntentLabel = Profiler.Web.Profile.ConnectionIntent.LabelFor(user.ConnectionIntent);
        ViewBag.ShowableInterests = Profiler.Web.Profile.ShowableInterests.Deserialize(user.ShowableInterestsJson);
        ViewBag.HasValues = user.ValuesOpenness.HasValue;
        ViewBag.ValuesEnabled = _flags.ValuesSignalEnabled;
        ViewBag.IsDiscoverable = user.IsDiscoverable;
        // Accounts predating recovery have no code on file, and a used code is not replaced if the
        // replacement was never saved. Either way the account has no way back from a lost password.
        ViewBag.HasRecoveryCode = user.RecoveryCodeHash != null;
        ViewBag.CirclesEnabled = _flags.CirclesEnabled;
        ViewBag.MaxCircleNameLength = CirclesController.MaxNameLength;
        // The viewer's circles with a current invite link each. The link is minted on render and never
        // stored, so there is no record of who shared it with whom.
        var circleRows = _flags.CirclesEnabled
            ? await _db.CircleMemberships
                .Where(m => m.UserId == userId)
                .Join(_db.Circles, m => m.CircleId, c => c.Id, (m, c) => c)
                .OrderBy(c => c.Name)
                // Same definition as CirclesController.MemberCountAsync (memberships whose account is not suspended), inlined so it translates inside this projection.
                .Select(c => new { c.Id, c.Name, MemberCount = _db.CircleMemberships.Where(m => m.CircleId == c.Id).Join(_db.Users, m => m.UserId, u => u.Id, (m, u) => u).Count(u => u.SuspendedAt == null) })
                .ToListAsync()
            : new();
        var circles = new List<CircleSummaryViewModel>();
        foreach (var c in circleRows)
        {
            // Organiser's read on whether the circle is working: how many built a fingerprint, and how
            // many discoverable pairs already overlap at the Good tier. Counts only, computed here and
            // discarded; the pair count is withheld for small circles, where it would say who overlaps
            // with whom, and skipped for very large ones.
            // "Eligible" = discoverable, not suspended, with a fingerprint: the people whose pairs the
            // circle page would show. Only their count is needed unless the pair count will be shown.
            var eligible = _db.CircleMemberships
                .Where(m => m.CircleId == c.Id)
                .Join(_db.Users.Where(u => u.SuspendedAt == null && u.IsDiscoverable && u.Fingerprint != null),
                      m => m.UserId, u => u.Id, (m, u) => u);
            var eligibleCount = await eligible.CountAsync();
            int? goodPairs = null;
            if (eligibleCount >= CircleSummaryViewModel.PairStatsMinEligible && eligibleCount <= CircleSummaryViewModel.PairStatsMaxEligible)
            {
                var signatures = (await eligible.Select(u => u.Fingerprint!.FingerprintJson).ToListAsync())
                    .Select(ProfileFingerprint.FromJson).Where(f => !f.IsEmpty).ToList();
                var good = 0;
                for (var i = 0; i < signatures.Count; i++)
                    for (var j = i + 1; j < signatures.Count; j++)
                        if (new MatchViewModel { Similarity = signatures[i].Similarity(signatures[j]) }.Tier != "low") good++;
                goodPairs = good;
            }
            circles.Add(new CircleSummaryViewModel
            {
                Id = c.Id, Name = c.Name, MemberCount = c.MemberCount,
                InviteUrl = $"{Request.Scheme}://{Request.Host}/circles/join/{_invites.Issue(c.Id)}",
                WithFingerprint = eligibleCount,
                GoodPairs = goodPairs,
            });
        }
        ViewBag.Circles = circles;
        return View();
    }

    [HttpGet("connect")]
    public async Task<IActionResult> Connect()
    {
        ViewBag.ConnectedSources = await ConnectedSourceNamesAsync(CurrentUserId);
        return View(new ConnectSourcesViewModel());
    }

    [HttpPost("connect")]
    [ValidateAntiForgeryToken]
    // Three CSVs of up to 10 MB each (a Takeout subscriptions file is a few KB) plus form fields, so
    // three maximum-size files still reach the per-file check below with its precise message. Rejects
    // anything bigger at the pipeline level, before ASP.NET buffers up to its 128 MB default.
    [RequestSizeLimit(35 * 1024 * 1024)]
    // Every submit fans out to third-party APIs (and user-supplied RSS URLs), so this endpoint
    // is metered the same as login/register rather than left to make unlimited outbound calls.
    [EnableRateLimiting("connect")]
    public async Task<IActionResult> Connect(ConnectSourcesViewModel vm)
    {
        var userId = CurrentUserId;

        foreach (var (file, label) in new[] { (vm.GoodreadsCsv, "Goodreads"), (vm.NetflixCsv, "Netflix"), (vm.YouTubeSubscriptionsCsv, "YouTube subscriptions") })
        {
            if (file is { } f && f.Length > MaxCsvUploadBytes)
            {
                ModelState.AddModelError("", $"{label} CSV is too large (max 10 MB).");
                ViewBag.ConnectedSources = await ConnectedSourceNamesAsync(userId);
                return View(vm);
            }
        }

        var connectors = new List<IConnector>();
        var http = _httpFactory.CreateClient("connectors");

        if (!string.IsNullOrWhiteSpace(vm.GitHubUser))
            connectors.Add(new GitHubConnector(http, vm.GitHubUser, vm.GitHubToken));

        if (vm.GoodreadsCsv is { Length: > 0 })
        {
            using var sr = new StreamReader(vm.GoodreadsCsv.OpenReadStream());
            var csv = await sr.ReadToEndAsync();
            connectors.Add(new GoodreadsConnector(csv));
        }

        if (vm.NetflixCsv is { Length: > 0 })
        {
            using var sr = new StreamReader(vm.NetflixCsv.OpenReadStream());
            var csv = await sr.ReadToEndAsync();
            connectors.Add(new NetflixConnector(csv));
        }

        if (vm.YouTubeSubscriptionsCsv is { Length: > 0 })
        {
            using var sr = new StreamReader(vm.YouTubeSubscriptionsCsv.OpenReadStream());
            var csv = await sr.ReadToEndAsync();
            connectors.Add(new YouTubeSubscriptionsConnector(csv));
        }

        if (!string.IsNullOrWhiteSpace(vm.GoogleToken))
            connectors.Add(new GoogleConnector(http, vm.GoogleToken));

        if (!string.IsNullOrWhiteSpace(vm.FacebookToken))
            connectors.Add(new FacebookConnector(http, vm.FacebookToken));

        if (!string.IsNullOrWhiteSpace(vm.PinterestToken))
            connectors.Add(new PinterestConnector(http, vm.PinterestToken));

        if (!string.IsNullOrWhiteSpace(vm.SpotifyToken))
            connectors.Add(new SpotifyConnector(http, vm.SpotifyToken));

        if (!string.IsNullOrWhiteSpace(vm.TwitterToken))
            connectors.Add(new TwitterConnector(http, vm.TwitterToken));

        if (!string.IsNullOrWhiteSpace(vm.LinkedInToken))
            connectors.Add(new LinkedInConnector(http, vm.LinkedInToken));

        if (!string.IsNullOrWhiteSpace(vm.RedditToken))
            connectors.Add(new RedditConnector(http, vm.RedditToken));

        if (!string.IsNullOrWhiteSpace(vm.LastFmApiKey) && !string.IsNullOrWhiteSpace(vm.LastFmUsername))
            connectors.Add(new LastFmConnector(http, vm.LastFmApiKey, vm.LastFmUsername));

        if (!string.IsNullOrWhiteSpace(vm.SteamApiKey) && !string.IsNullOrWhiteSpace(vm.SteamId))
            connectors.Add(new SteamConnector(http, vm.SteamApiKey, vm.SteamId));

        if (!string.IsNullOrWhiteSpace(vm.TikTokToken))
            connectors.Add(new TikTokConnector(http, vm.TikTokToken));

        if (!string.IsNullOrWhiteSpace(vm.InstagramToken))
            connectors.Add(new InstagramConnector(http, vm.InstagramToken));

        if (!string.IsNullOrWhiteSpace(vm.TwitchToken) && !string.IsNullOrWhiteSpace(vm.TwitchClientId))
            connectors.Add(new TwitchConnector(http, vm.TwitchToken, vm.TwitchClientId));

        if (!string.IsNullOrWhiteSpace(vm.RssFeedUrls))
            connectors.Add(new RssFeedsConnector(_httpFactory.CreateClient("rss-connector"), vm.RssFeedUrls));

        if (!string.IsNullOrWhiteSpace(vm.SoundCloudToken))
            connectors.Add(new SoundCloudConnector(http, vm.SoundCloudToken));

        if (!string.IsNullOrWhiteSpace(vm.YouTubeToken))
            connectors.Add(new YouTubeConnector(http, vm.YouTubeToken));

        if (connectors.Count == 0)
        {
            ModelState.AddModelError("", "Please connect at least one source.");
            ViewBag.ConnectedSources = await ConnectedSourceNamesAsync(userId);
            return View(vm);
        }

        var aggregator = new ProfileAggregator(connectors);
        var result = await aggregator.AggregateAsync(HttpContext.RequestAborted);

        if (result.Results.Count == 0)
        {
            foreach (var failure in result.Failures)
                ModelState.AddModelError("", $"{failure.Source}: {failure.Message}");
            ModelState.AddModelError("", "No interest data could be collected, so nothing was updated.");
            ViewBag.ConnectedSources = await ConnectedSourceNamesAsync(userId);
            return View(vm);
        }

        var generator = _generator;
        var now = DateTime.UtcNow;

        // The per-source rows and the combined fingerprint are two saves. Committed separately, a
        // failure between them leaves the dashboard showing a freshly connected source while
        // matching keeps using the old combined signature — and nothing ever recomputes it, so the
        // discrepancy would be permanent.
        await using var transaction = await _db.Database.BeginTransactionAsync();

        // Same source twice in one submit (token + export) becomes one row with the union of features.
        var merged = result.MergedBySource();
        foreach (var source in merged)
        {
            var distinctFeatures = source.Features.Distinct().ToList();
            var raw = generator.GenerateRaw(distinctFeatures);
            var record = await _db.SourceFingerprints
                .FirstOrDefaultAsync(s => s.UserId == userId && s.Source == source.Source);
            if (record == null)
            {
                _db.SourceFingerprints.Add(new SourceFingerprintRecord
                {
                    UserId = userId,
                    Source = source.Source,
                    RawSignatureJson = JsonSerializer.Serialize(raw),
                    FeatureCount = distinctFeatures.Count,
                    UpdatedAt = now
                });
            }
            else
            {
                record.RawSignatureJson = JsonSerializer.Serialize(raw);
                record.FeatureCount = distinctFeatures.Count;
                record.UpdatedAt = now;
            }
        }
        await _db.SaveChangesAsync();

        var totalSources = await RecomputeCombinedFingerprintAsync(userId);
        await _db.SaveChangesAsync();
        await transaction.CommitAsync();

        TempData["Success"] = $"{string.Join(", ", merged.Select(m => m.Source))} " +
            $"{(merged.Count == 1 ? "was" : "were")} updated. " +
            $"Your fingerprint now covers {totalSources} source{(totalSources == 1 ? "" : "s")}.";
        if (result.Failures.Count > 0)
            TempData["Error"] = "Some sources could not be fetched and were left out — " +
                string.Join("; ", result.Failures.Select(f => $"{f.Source}: {f.Message}"));

        // A themed summary of what was just found, so the person sees something the instant they
        // connect — even before anyone has matched them. Only theme names and counts are carried, in
        // TempData (a cookie), and shown once; the raw features themselves are discarded with the
        // rest of this request, never stored, and never placed in the cookie.
        var lens = InterestLens.Summarize(result.Features);
        TempData["InterestLens"] = JsonSerializer.Serialize(lens);

        return RedirectToAction("Index", "Matches");
    }

    [HttpGet("interests")]
    public async Task<IActionResult> Interests()
    {
        // The chosen tags are discarded once the signature is built (raw interests are never stored),
        // so the page cannot pre-tick a previous selection — the same trade every source makes. We only
        // know *whether* a self-described source exists, to word the page as add vs. replace.
        ViewBag.HasSelfDescribed = await _db.SourceFingerprints
            .AnyAsync(s => s.UserId == CurrentUserId && s.Source == SelfDescribedSource);
        return View();
    }

    // Generous cap on interest text: a real person types a handful of lines, so this only stops a giant
    // paste from forcing a huge string allocation. Well above any legitimate use.
    private const int MaxCustomLinesProcessed = 200;

    [HttpPost("interests")]
    [ValidateAntiForgeryToken]
    // Metered like Connect: the work is bounded and local (no outbound calls), but rate-limiting the
    // write is cheap defense-in-depth against a flood of fingerprint rebuilds. Shares the /sources
    // rejection page.
    [EnableRateLimiting("connect")]
    // Interest text is tiny; cap the body so a multi-megabyte paste cannot be buffered or split into a
    // huge line array (the connectors endpoint has its own, larger, limit for CSV uploads).
    [RequestSizeLimit(256 * 1024)]
    public async Task<IActionResult> Interests(List<string>? features, string? custom)
    {
        var userId = CurrentUserId;

        // Keep only real catalog features: a crafted form can never inject an arbitrary feature into
        // the fingerprint, and duplicates collapse (the same tag ticked twice is one signal).
        var chosen = (features ?? new List<string>())
            .Where(InterestCatalog.IsValidFeature)
            .Distinct()
            .ToList();

        // Bridge the unambiguous picked concepts (e.g. Python) into the shared connector vocabulary so a
        // self-describer can match a connector user, not just other self-describers.
        var canonical = InterestCatalog.Canonicalize(chosen);

        // Free-text interests the catalog doesn't cover — normalized so people who type the same thing
        // match, and mapped onto a catalog concept when one fits. This is where the rarest (highest-
        // signal) interests come from.
        var customLines = (custom ?? "")
            .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Take(MaxCustomLinesProcessed);
        var customFeatures = InterestCatalog.CustomFeatures(customLines);

        // One de-duplicated interest set: a picked concept and the same concept typed collapse to one.
        var allFeatures = canonical.Concat(customFeatures).Distinct().ToList();

        if (allFeatures.Count == 0)
        {
            ModelState.AddModelError("", "Pick or type at least one interest so we can build your fingerprint.");
            ViewBag.HasSelfDescribed = await _db.SourceFingerprints
                .AnyAsync(s => s.UserId == userId && s.Source == SelfDescribedSource);
            return View();
        }

        // Weight by rarity (feature replication) so sharing a niche counts for more than sharing a
        // popular thing. FeatureCount is the number of distinct interests, not the expanded count.
        var raw = _generator.GenerateRaw(InterestCatalog.Expand(allFeatures));
        var featureCount = allFeatures.Count;
        var now = DateTime.UtcNow;

        // Same two-save-in-a-transaction shape as Connect: per-source row, then the recomputed
        // combined signature, so matching never reads a half-updated state.
        await using var transaction = await _db.Database.BeginTransactionAsync();

        var record = await _db.SourceFingerprints
            .FirstOrDefaultAsync(s => s.UserId == userId && s.Source == SelfDescribedSource);
        if (record == null)
        {
            _db.SourceFingerprints.Add(new SourceFingerprintRecord
            {
                UserId = userId,
                Source = SelfDescribedSource,
                RawSignatureJson = JsonSerializer.Serialize(raw),
                FeatureCount = featureCount,
                UpdatedAt = now
            });
        }
        else
        {
            // Re-picking replaces the whole self-described set — there is no stored prior selection to
            // merge with, which matches the "we don't keep your answers" model.
            record.RawSignatureJson = JsonSerializer.Serialize(raw);
            record.FeatureCount = featureCount;
            record.UpdatedAt = now;
        }
        await _db.SaveChangesAsync();

        var totalSources = await RecomputeCombinedFingerprintAsync(userId);
        await _db.SaveChangesAsync();
        await transaction.CommitAsync();

        // Same one-shot themed summary connectors show, built from the whole (canonicalized + custom)
        // interest set and then discarded with the request — only theme names and counts survive, never
        // the interests themselves.
        var lens = InterestLens.Summarize(allFeatures);
        TempData["InterestLens"] = JsonSerializer.Serialize(lens);
        TempData["Success"] = $"Saved {featureCount} interest{(featureCount == 1 ? "" : "s")}. " +
            $"Your fingerprint now covers {totalSources} source{(totalSources == 1 ? "" : "s")}.";

        return RedirectToAction("Index", "Matches");
    }

    [HttpPost("disconnect")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Disconnect(string source)
    {
        var userId = CurrentUserId;

        var record = await _db.SourceFingerprints
            .FirstOrDefaultAsync(s => s.UserId == userId && s.Source == source);
        if (record == null)
        {
            TempData["Error"] = $"{source} is not a connected source.";
            return RedirectToAction(nameof(Dashboard));
        }

        _db.SourceFingerprints.Remove(record);
        var remaining = await RecomputeCombinedFingerprintAsync(userId);
        await _db.SaveChangesAsync();

        TempData["Success"] = remaining > 0
            ? $"{source} disconnected. Your fingerprint now covers {remaining} source{(remaining == 1 ? "" : "s")}."
            : $"{source} disconnected. You have no connected sources left, so your fingerprint was removed.";
        return RedirectToAction(nameof(Dashboard));
    }

    private async Task<List<string>> ConnectedSourceNamesAsync(int userId) =>
        await _db.SourceFingerprints
            .Where(s => s.UserId == userId)
            .OrderBy(s => s.Source)
            .Select(s => s.Source)
            .ToListAsync();

    /// <summary>
    /// Rebuilds the matching fingerprint as the element-wise minimum of the user's per-source
    /// raw signatures (pending removals in the change tracker are respected). Returns the
    /// number of sources included. Caller saves changes.
    /// </summary>
    private async Task<int> RecomputeCombinedFingerprintAsync(int userId)
    {
        var sourceRecords = (await _db.SourceFingerprints
                .Where(s => s.UserId == userId)
                .ToListAsync())
            .Where(s => _db.Entry(s).State != EntityState.Deleted)
            .ToList();

        var existing = await _db.Fingerprints.FirstOrDefaultAsync(f => f.UserId == userId);

        if (sourceRecords.Count == 0)
        {
            if (existing != null)
                _db.Fingerprints.Remove(existing);
            return 0;
        }

        var raws = sourceRecords
            .Select(s => JsonSerializer.Deserialize<ulong[]>(s.RawSignatureJson) ?? Array.Empty<ulong>())
            .Where(raw => raw.Length > 0);
        var fingerprint = FingerprintGenerator.FromRaw(FingerprintGenerator.CombineRaw(raws));
        var sources = sourceRecords.Select(s => s.Source).OrderBy(s => s).ToList();

        if (existing == null)
        {
            _db.Fingerprints.Add(new FingerprintRecord
            {
                UserId = userId,
                FingerprintJson = fingerprint.ToJson(),
                SourcesJson = JsonSerializer.Serialize(sources),
                UpdatedAt = DateTime.UtcNow
            });
        }
        else
        {
            existing.FingerprintJson = fingerprint.ToJson();
            existing.SourcesJson = JsonSerializer.Serialize(sources);
            existing.UpdatedAt = DateTime.UtcNow;
        }

        return sourceRecords.Count;
    }
}
