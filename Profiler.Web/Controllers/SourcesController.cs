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

    private readonly AppDbContext _db;
    private readonly IHttpClientFactory _httpFactory;
    // Injected rather than constructed here: it carries the deployment's pepper, and a generator
    // built without it would write signatures nothing else could compare.
    private readonly FingerprintGenerator _generator;

    public SourcesController(AppDbContext db, IHttpClientFactory httpFactory, FingerprintGenerator generator)
    {
        _db = db;
        _httpFactory = httpFactory;
        _generator = generator;
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
        ViewBag.IsDiscoverable = user.IsDiscoverable;
        // Accounts predating recovery have no code on file, and a used code is not replaced if the
        // replacement was never saved. Either way the account has no way back from a lost password.
        ViewBag.HasRecoveryCode = user.RecoveryCodeHash != null;
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
    // Two 10 MB CSVs plus form fields. Rejects oversized bodies at the pipeline level, before
    // ASP.NET buffers up to its 128 MB default and the per-file check below ever runs.
    [RequestSizeLimit(25 * 1024 * 1024)]
    // Every submit fans out to third-party APIs (and user-supplied RSS URLs), so this endpoint
    // is metered the same as login/register rather than left to make unlimited outbound calls.
    [EnableRateLimiting("connect")]
    public async Task<IActionResult> Connect(ConnectSourcesViewModel vm)
    {
        var userId = CurrentUserId;

        foreach (var (file, label) in new[] { (vm.GoodreadsCsv, "Goodreads"), (vm.NetflixCsv, "Netflix") })
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

        foreach (var source in result.Results)
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

        TempData["Success"] = $"{string.Join(", ", result.Sources)} " +
            $"{(result.Sources.Count == 1 ? "was" : "were")} updated. " +
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
