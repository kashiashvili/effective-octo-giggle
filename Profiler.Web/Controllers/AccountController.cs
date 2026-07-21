using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Profiler.Web.Data;
using Profiler.Web.Security;
using Profiler.Web.Data.Models;
using Profiler.Web.ViewModels;

namespace Profiler.Web.Controllers;

[Route("account")]
// Deny by default: this controller serves profile, data export, password and deletion, so a new
// action must be protected unless it deliberately opts out with [AllowAnonymous].
[Authorize]
public class AccountController : Controller
{
    private readonly AppDbContext _db;

    public AccountController(AppDbContext db) => _db = db;

    [HttpGet("register")]
    [AllowAnonymous]
    public IActionResult Register()
    {
        if (User.Identity?.IsAuthenticated == true)
            return RedirectToAction("Dashboard", "Sources");
        return View();
    }

    [HttpPost("register")]
    [AllowAnonymous]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Register(RegisterViewModel vm)
    {
        if (!ModelState.IsValid) return View(vm);

        var username = vm.Username.Trim();

        if (Security.PasswordPolicy.Validate(vm.Password, username) is { } weak)
        {
            ModelState.AddModelError(nameof(vm.Password), weak);
            return View(vm);
        }

        // Case-insensitive check (Username column is NOCASE). The unique index is the real
        // guard against a concurrent duplicate slipping past this check.
        if (await _db.Users.AnyAsync(u => u.Username == username))
        {
            ModelState.AddModelError(nameof(vm.Username), "Username already taken.");
            return View(vm);
        }

        var user = new AppUser
        {
            Username = username,
            PasswordHash = BCrypt.Net.BCrypt.HashPassword(vm.Password)
        };
        _db.Users.Add(user);
        try
        {
            await _db.SaveChangesAsync();
        }
        catch (DbUpdateException)
        {
            ModelState.AddModelError(nameof(vm.Username), "Username already taken.");
            return View(vm);
        }

        await SignInUserAsync(user);
        TempData["Success"] = "Welcome to Profiler! Connect a source to generate your fingerprint.";
        return RedirectToAction("Connect", "Sources");
    }

    [HttpGet("login")]
    [AllowAnonymous]
    public IActionResult Login()
    {
        if (User.Identity?.IsAuthenticated == true)
            return RedirectToAction("Dashboard", "Sources");
        return View();
    }

    [HttpPost("login")]
    [AllowAnonymous]
    [ValidateAntiForgeryToken]
    [EnableRateLimiting("login")]
    public async Task<IActionResult> Login(LoginViewModel vm)
    {
        if (!ModelState.IsValid) return View(vm);

        var user = await _db.Users.FirstOrDefaultAsync(u => u.Username == vm.Username);
        if (user == null || !BCrypt.Net.BCrypt.Verify(vm.Password, user.PasswordHash))
        {
            ModelState.AddModelError("", "Invalid username or password.");
            return View(vm);
        }

        await SignInUserAsync(user);
        return RedirectToAction("Dashboard", "Sources");
    }

    /// <summary>
    /// Loads the signed-in user. Cookie validation already rejects principals whose user row is
    /// gone, so null here means the account was deleted during this very request; callers sign the
    /// stale principal out rather than fail.
    /// </summary>
    private Task<AppUser?> FindCurrentUserAsync() =>
        _db.Users.FirstOrDefaultAsync(u => u.Id == User.GetUserId());

    private async Task<IActionResult> SignOutToHomeAsync()
    {
        await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
        return Redirect("/");
    }

    private async Task SignInUserAsync(AppUser user)
    {
        var identity = new ClaimsIdentity(new[]
        {
            new Claim(ClaimTypes.NameIdentifier, user.Id.ToString()),
            new Claim(ClaimTypes.Name, user.Username)
        }, CookieAuthenticationDefaults.AuthenticationScheme);

        await HttpContext.SignInAsync(
            CookieAuthenticationDefaults.AuthenticationScheme,
            new ClaimsPrincipal(identity),
            new AuthenticationProperties { IsPersistent = true });
    }

    [HttpPost("logout")]
    [AllowAnonymous]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Logout()
    {
        await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
        return Redirect("/");
    }

    [HttpGet("profile")]
    public async Task<IActionResult> Profile()
    {
        var user = await FindCurrentUserAsync();
        if (user == null) return await SignOutToHomeAsync();
        return View(new ProfileViewModel { Bio = user.Bio, Contact = user.Contact });
    }

    [HttpPost("profile")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Profile(ProfileViewModel vm)
    {
        if (!ModelState.IsValid) return View(vm);

        var user = await FindCurrentUserAsync();
        if (user == null) return await SignOutToHomeAsync();

        user.Bio = string.IsNullOrWhiteSpace(vm.Bio) ? null : vm.Bio.Trim();
        user.Contact = string.IsNullOrWhiteSpace(vm.Contact) ? null : vm.Contact.Trim();
        await _db.SaveChangesAsync();

        TempData["Success"] = "Your profile has been saved.";
        return RedirectToAction("Dashboard", "Sources");
    }

    [HttpGet("data")]
    public async Task<IActionResult> Data()
    {
        var export = await BuildDataExportAsync();
        if (export == null) return await SignOutToHomeAsync();
        return View(export);
    }

    [HttpGet("data.json")]
    public async Task<IActionResult> DataJson()
    {
        var export = await BuildDataExportAsync();
        if (export == null) return await SignOutToHomeAsync();

        var json = System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(export,
            new System.Text.Json.JsonSerializerOptions { WriteIndented = true });
        return File(json, "application/json", "profiler-data.json");
    }

    private async Task<DataExportViewModel?> BuildDataExportAsync()
    {
        var userId = User.GetUserId();
        var user = await _db.Users.FirstOrDefaultAsync(u => u.Id == userId);
        if (user == null) return null;

        var fp = await _db.Fingerprints.FirstOrDefaultAsync(f => f.UserId == userId);
        var dimensions = 0;
        if (fp != null)
            dimensions = (System.Text.Json.JsonSerializer.Deserialize<int[]>(fp.FingerprintJson) ?? Array.Empty<int>()).Length;

        var sources = await _db.SourceFingerprints
            .Where(s => s.UserId == userId)
            .OrderBy(s => s.Source)
            .Select(s => new DataExportSource { Source = s.Source, SignalCount = s.FeatureCount, UpdatedAt = s.UpdatedAt })
            .ToListAsync();

        return new DataExportViewModel
        {
            Username = user.Username,
            CreatedAt = user.CreatedAt,
            IsDiscoverable = user.IsDiscoverable,
            Bio = user.Bio,
            Contact = user.Contact,
            FingerprintDimensions = dimensions,
            Sources = sources
        };
    }

    [HttpPost("visibility")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Visibility(bool discoverable)
    {
        var user = await FindCurrentUserAsync();
        if (user == null) return await SignOutToHomeAsync();

        user.IsDiscoverable = discoverable;
        await _db.SaveChangesAsync();

        TempData["Success"] = discoverable
            ? "You're now discoverable — other people can find you in their matches."
            : "You're now hidden — you won't appear in anyone else's matches. You can still see yours.";
        return RedirectToAction("Dashboard", "Sources");
    }

    [HttpGet("password")]
    public IActionResult Password() => View(new ChangePasswordViewModel());

    [HttpPost("password")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Password(ChangePasswordViewModel vm)
    {
        if (!ModelState.IsValid) return View(vm);

        var user = await FindCurrentUserAsync();
        if (user == null) return await SignOutToHomeAsync();

        if (!BCrypt.Net.BCrypt.Verify(vm.CurrentPassword, user.PasswordHash))
        {
            ModelState.AddModelError(nameof(vm.CurrentPassword), "Current password is incorrect.");
            return View(vm);
        }

        if (BCrypt.Net.BCrypt.Verify(vm.NewPassword, user.PasswordHash))
        {
            ModelState.AddModelError(nameof(vm.NewPassword), "New password must be different from the current one.");
            return View(vm);
        }

        if (Security.PasswordPolicy.Validate(vm.NewPassword, user.Username) is { } weak)
        {
            ModelState.AddModelError(nameof(vm.NewPassword), weak);
            return View(vm);
        }

        user.PasswordHash = BCrypt.Net.BCrypt.HashPassword(vm.NewPassword);
        await _db.SaveChangesAsync();

        TempData["Success"] = "Your password has been changed.";
        return RedirectToAction("Dashboard", "Sources");
    }

    [HttpPost("delete")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Delete(string password)
    {
        var user = await FindCurrentUserAsync();
        if (user == null) return await SignOutToHomeAsync();

        if (string.IsNullOrEmpty(password) || !BCrypt.Net.BCrypt.Verify(password, user.PasswordHash))
        {
            TempData["Error"] = "Account not deleted — the password you entered is incorrect.";
            return RedirectToAction("Dashboard", "Sources");
        }

        _db.SourceFingerprints.RemoveRange(_db.SourceFingerprints.Where(s => s.UserId == user.Id));
        _db.Fingerprints.RemoveRange(_db.Fingerprints.Where(f => f.UserId == user.Id));
        _db.Users.Remove(user);
        await _db.SaveChangesAsync();

        await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
        TempData["Success"] = "Your account and all associated data have been permanently deleted.";
        return Redirect("/");
    }
}
