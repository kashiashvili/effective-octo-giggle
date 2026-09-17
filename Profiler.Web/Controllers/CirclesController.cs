using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Profiler.Web.Data;
using Profiler.Web.Data.Models;
using Profiler.Web.Security;
using Profiler.Web.ViewModels;

namespace Profiler.Web.Controllers;

/// <summary>
/// Circles: a named group people join through a shareable invite link, so a meetup, a cohort or a
/// team can find each other first (docs/DESIGN_CIRCLES.md). Membership only ever adds a "Same
/// circle" chip and a sort on the match list — never a filter, never part of a score. Joining is an
/// explicit POST on a page that names the circle; a link alone never joins anyone.
/// </summary>
[Authorize]
[Route("circles")]
public class CirclesController : Controller
{
    public const int MaxNameLength = 40;

    private readonly AppDbContext _db;
    private readonly CircleInvites _invites;
    private readonly FeatureFlags _flags;

    public CirclesController(AppDbContext db, CircleInvites invites, FeatureFlags flags)
    {
        _db = db;
        _invites = invites;
        _flags = flags;
    }

    private int CurrentUserId => User.GetUserId();

    [HttpPost("create")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(string? name)
    {
        if (!_flags.CirclesEnabled) return NotFound();

        var trimmed = (name ?? "").Trim();
        if (trimmed.Length == 0 || trimmed.Length > MaxNameLength)
        {
            TempData["Error"] = $"Give the circle a name of up to {MaxNameLength} characters.";
            return RedirectToAction("Dashboard", "Sources");
        }
        if (TextPolicy.ValidateProfileText(trimmed, "Circle name", allowNewlines: false) is { } bad)
        {
            TempData["Error"] = bad;
            return RedirectToAction("Dashboard", "Sources");
        }

        var circle = new Circle { Name = trimmed };
        _db.Circles.Add(circle);
        await _db.SaveChangesAsync();
        _db.CircleMemberships.Add(new CircleMembership { CircleId = circle.Id, UserId = CurrentUserId });
        await _db.SaveChangesAsync();

        TempData["Success"] = $"Circle \"{trimmed}\" started. Share its invite link from your dashboard.";
        return RedirectToAction("Dashboard", "Sources");
    }

    /// <summary>
    /// The invite landing page. Anonymous on purpose: someone who has no account yet must see what they
    /// were invited to before registering. Nothing happens until the "Join" POST.
    /// </summary>
    [HttpGet("join/{token}")]
    [AllowAnonymous]
    public async Task<IActionResult> Join(string token)
    {
        if (!_flags.CirclesEnabled) return NotFound();

        var circle = await CircleForAsync(token);
        if (circle == null) return View("InviteExpired");

        var vm = new CircleJoinViewModel
        {
            CircleName = circle.Name,
            Token = token,
            MemberCount = await _db.CircleMemberships.CountAsync(m => m.CircleId == circle.Id),
            IsSignedIn = User.Identity?.IsAuthenticated == true,
        };
        if (vm.IsSignedIn)
            vm.AlreadyMember = await _db.CircleMemberships.AnyAsync(m => m.CircleId == circle.Id && m.UserId == CurrentUserId);
        return View(vm);
    }

    [HttpPost("join")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> JoinConfirm(string? token)
    {
        if (!_flags.CirclesEnabled) return NotFound();

        var circle = await CircleForAsync(token);
        if (circle == null) return View("InviteExpired");

        var userId = CurrentUserId;
        if (!await _db.CircleMemberships.AnyAsync(m => m.CircleId == circle.Id && m.UserId == userId))
        {
            _db.CircleMemberships.Add(new CircleMembership { CircleId = circle.Id, UserId = userId });
            try
            {
                await _db.SaveChangesAsync();
            }
            catch (DbUpdateException)
            {
                // Two submits racing: the unique index kept one membership, which is the intended state.
            }
        }

        TempData["Success"] = $"You're in \"{circle.Name}\". People from the same circle are marked on your matches.";
        return RedirectToAction("Dashboard", "Sources");
    }

    [HttpPost("leave")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Leave(int circleId)
    {
        if (!_flags.CirclesEnabled) return NotFound();

        var userId = CurrentUserId;
        var membership = await _db.CircleMemberships.FirstOrDefaultAsync(m => m.CircleId == circleId && m.UserId == userId);
        if (membership != null)
        {
            _db.CircleMemberships.Remove(membership);
            await _db.SaveChangesAsync();
            await RemoveIfEmptyAsync(_db, circleId);
            TempData["Success"] = "You left the circle.";
        }
        return RedirectToAction("Dashboard", "Sources");
    }

    private async Task<Circle?> CircleForAsync(string? token)
    {
        var id = _invites.TryRead(token);
        return id == null ? null : await _db.Circles.FirstOrDefaultAsync(c => c.Id == id);
    }

    /// <summary>A circle nobody is in any more has no reason to exist; its name is the only thing left.</summary>
    public static async Task RemoveIfEmptyAsync(AppDbContext db, int circleId)
    {
        if (await db.CircleMemberships.AnyAsync(m => m.CircleId == circleId)) return;
        await db.Circles.Where(c => c.Id == circleId).ExecuteDeleteAsync();
    }
}
