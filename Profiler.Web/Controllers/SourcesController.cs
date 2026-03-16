using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Profiler.Web.Connectors;
using Profiler.Web.Data;
using Profiler.Web.Data.Models;
using Profiler.Web.Profile;
using Profiler.Web.ViewModels;

namespace Profiler.Web.Controllers;

[Route("sources")]
public class SourcesController : Controller
{
    private readonly AppDbContext _db;
    private readonly IHttpClientFactory _httpFactory;

    public SourcesController(AppDbContext db, IHttpClientFactory httpFactory)
    {
        _db = db;
        _httpFactory = httpFactory;
    }

    private IActionResult? RequireLogin()
    {
        if (HttpContext.Session.GetInt32("UserId") is null)
            return RedirectToAction("Login", "Account");
        return null;
    }

    [HttpGet("dashboard")]
    public async Task<IActionResult> Dashboard()
    {
        if (RequireLogin() is { } r) return r;
        var userId = HttpContext.Session.GetInt32("UserId")!.Value;
        var fp = await _db.Fingerprints.FirstOrDefaultAsync(f => f.UserId == userId);
        ViewBag.HasFingerprint = fp != null;
        ViewBag.Sources = fp != null
            ? JsonSerializer.Deserialize<List<string>>(fp.SourcesJson) ?? new()
            : new List<string>();
        return View();
    }

    [HttpGet("connect")]
    public IActionResult Connect()
    {
        if (RequireLogin() is { } r) return r;
        return View(new ConnectSourcesViewModel());
    }

    [HttpPost("connect")]
    public async Task<IActionResult> Connect(ConnectSourcesViewModel vm)
    {
        if (RequireLogin() is { } r) return r;
        var userId = HttpContext.Session.GetInt32("UserId")!.Value;

        var connectors = new List<IConnector>();
        var http = _httpFactory.CreateClient();

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

        if (connectors.Count == 0)
        {
            ModelState.AddModelError("", "Please connect at least one source.");
            return View(vm);
        }

        var aggregator = new ProfileAggregator(connectors);
        var (sources, features) = await aggregator.AggregateAsync();

        var generator = new FingerprintGenerator();
        var fp = generator.Generate(features);

        var existing = await _db.Fingerprints.FirstOrDefaultAsync(f => f.UserId == userId);
        if (existing == null)
        {
            _db.Fingerprints.Add(new FingerprintRecord
            {
                UserId = userId,
                FingerprintJson = fp.ToJson(),
                SourcesJson = JsonSerializer.Serialize(sources),
                UpdatedAt = DateTime.UtcNow
            });
        }
        else
        {
            existing.FingerprintJson = fp.ToJson();
            existing.SourcesJson = JsonSerializer.Serialize(sources);
            existing.UpdatedAt = DateTime.UtcNow;
        }

        await _db.SaveChangesAsync();
        return RedirectToAction("Index", "Matches");
    }
}
