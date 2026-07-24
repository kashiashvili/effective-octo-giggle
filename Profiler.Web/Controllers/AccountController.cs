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
    [EnableRateLimiting("register")]
    public async Task<IActionResult> Register(RegisterViewModel vm)
    {
        if (!ModelState.IsValid) return View(vm);

        var username = vm.Username.Trim();

        if (Security.TextPolicy.ValidateUsername(username) is { } badName)
        {
            ModelState.AddModelError(nameof(vm.Username), badName);
            return View(vm);
        }

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

        // Beyond the ASCII case NOCASE already covers: reject a name that reduces to the same thing
        // as an existing one once case and compatibility differences are folded, so a stranger can't
        // register a lookalike of someone's name. A concurrent double-insert here is a narrow race
        // (the NOCASE unique index still blocks the ASCII-identical case); the non-unique index just
        // keeps this lookup cheap.
        var normalized = Security.TextPolicy.NormalizeForUniqueness(username);
        if (await _db.Users.AnyAsync(u => u.NormalizedUsername == normalized))
        {
            ModelState.AddModelError(nameof(vm.Username), "That username is too close to an existing one. Please choose another.");
            return View(vm);
        }

        var recoveryCode = RecoveryCode.Generate();
        var user = new AppUser
        {
            Username = username,
            NormalizedUsername = normalized,
            PasswordHash = BCrypt.Net.BCrypt.HashPassword(vm.Password),
            RecoveryCodeHash = RecoveryCode.Hash(recoveryCode)
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
        // Shown once, before anything else, because it is the only way back into this account and
        // nobody can reissue it for them.
        TempData["RecoveryCode"] = recoveryCode;
        return RedirectToAction(nameof(ShowRecoveryCode));
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
    /// Displays a freshly issued recovery code, once. The code only ever exists in TempData, so a
    /// reload or a later visit shows nothing — which is the honest behaviour, since only its hash was
    /// kept and nobody can produce it again.
    /// </summary>
    [HttpGet("recovery-code")]
    public IActionResult ShowRecoveryCode()
    {
        if (TempData["RecoveryCode"] is not string code)
        {
            TempData["Error"] = "That recovery code can only be shown once. Generate a new one if you didn't save it.";
            return RedirectToAction("Dashboard", "Sources");
        }
        return View("RecoveryCode", code);
    }

    /// <summary>
    /// Issues a replacement code, invalidating the previous one. Password-confirmed: someone who has
    /// walked up to an unlocked browser should not be able to mint themselves a way back in later.
    /// </summary>
    [HttpPost("recovery-code")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> RegenerateRecoveryCode(string password)
    {
        var user = await FindCurrentUserAsync();
        if (user == null) return await SignOutToHomeAsync();

        if (string.IsNullOrEmpty(password) || !BCrypt.Net.BCrypt.Verify(password, user.PasswordHash))
        {
            TempData["Error"] = "No new code was generated — the password you entered is incorrect.";
            return RedirectToAction("Dashboard", "Sources");
        }

        var code = RecoveryCode.Generate();
        user.RecoveryCodeHash = RecoveryCode.Hash(code);
        await _db.SaveChangesAsync();

        TempData["RecoveryCode"] = code;
        return RedirectToAction(nameof(ShowRecoveryCode));
    }

    /// <summary>
    /// Ends every other session without changing the password — for a shared or borrowed device
    /// someone forgot to sign out of, where there is nothing wrong with the password itself.
    /// </summary>
    [HttpPost("sign-out-everywhere")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> SignOutEverywhere()
    {
        var user = await FindCurrentUserAsync();
        if (user == null) return await SignOutToHomeAsync();

        user.SessionsValidFrom = DateTime.UtcNow;
        await _db.SaveChangesAsync();
        await SignInUserAsync(user);

        TempData["Success"] = "Signed out on every other device. You're still signed in here.";
        return RedirectToAction("Dashboard", "Sources");
    }

    [HttpGet("recover")]
    [AllowAnonymous]
    public IActionResult Recover()
    {
        if (User.Identity?.IsAuthenticated == true)
            return RedirectToAction("Dashboard", "Sources");
        return View(new RecoverAccountViewModel());
    }

    /// <summary>
    /// The whole recovery path: prove possession of the code, set a new password, and get signed in.
    /// Rate-limited with login, since it is the same thing an attacker would grind against.
    /// </summary>
    [HttpPost("recover")]
    [AllowAnonymous]
    [ValidateAntiForgeryToken]
    [EnableRateLimiting("login")]
    public async Task<IActionResult> Recover(RecoverAccountViewModel vm)
    {
        if (!ModelState.IsValid) return View(vm);

        var username = vm.Username.Trim();
        var user = await _db.Users.FirstOrDefaultAsync(u => u.Username == username);

        // One message for "no such user", "no code on file" and "wrong code" alike: which of the
        // three it was would tell an attacker whether a username exists.
        if (user == null || !RecoveryCode.Verify(vm.Code, user.RecoveryCodeHash))
        {
            ModelState.AddModelError("", "That username and recovery code don't match an account.");
            return View(vm);
        }

        if (Security.PasswordPolicy.Validate(vm.NewPassword, username) is { } weak)
        {
            ModelState.AddModelError(nameof(vm.NewPassword), weak);
            return View(vm);
        }

        user.PasswordHash = BCrypt.Net.BCrypt.HashPassword(vm.NewPassword);
        // Recovery is the strongest signal there is that someone else may be holding a session.
        user.SessionsValidFrom = DateTime.UtcNow;

        // Single use, and immediately replaced: recovering must not leave the account with no way
        // back the next time.
        var replacement = RecoveryCode.Generate();
        user.RecoveryCodeHash = RecoveryCode.Hash(replacement);
        await _db.SaveChangesAsync();

        await SignInUserAsync(user);
        TempData["Success"] = "Your password has been reset. Here is your new recovery code — the old one no longer works.";
        TempData["RecoveryCode"] = replacement;
        return RedirectToAction(nameof(ShowRecoveryCode));
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
            new Claim(ClaimTypes.Name, user.Username),
            // What lets a later password change invalidate this ticket without invalidating the
            // session doing the changing.
            new Claim(ClaimsPrincipalExtensions.IssuedAtClaim, DateTime.UtcNow.ToString("O"))
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
        return View(new ProfileViewModel { Bio = user.Bio, Contact = user.Contact, ConnectionIntent = user.ConnectionIntent });
    }

    [HttpPost("profile")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Profile(ProfileViewModel vm)
    {
        if (!ModelState.IsValid) return View(vm);

        var user = await FindCurrentUserAsync();
        if (user == null) return await SignOutToHomeAsync();

        // A bio may span lines; a contact line may not, since a newline there could push the
        // visible part away from what is actually stored.
        foreach (var (value, label, field, allowNewlines) in new[]
        {
            (vm.Bio, "Bios", nameof(vm.Bio), true),
            (vm.Contact, "Contact details", nameof(vm.Contact), false)
        })
        {
            if (Security.TextPolicy.ValidateProfileText(value, label, allowNewlines) is { } problem)
            {
                ModelState.AddModelError(field, problem);
                return View(vm);
            }
        }

        // Closed-set key from a selector; anyone posting something off-list is normalised to
        // unspecified rather than rejected, since it can only come from a tampered form.
        if (!Profiler.Web.Profile.ConnectionIntent.IsValid(vm.ConnectionIntent))
        {
            ModelState.AddModelError(nameof(vm.ConnectionIntent), "Please choose one of the listed options.");
            return View(vm);
        }

        user.Bio = string.IsNullOrWhiteSpace(vm.Bio) ? null : vm.Bio.Trim();
        user.Contact = string.IsNullOrWhiteSpace(vm.Contact) ? null : vm.Contact.Trim();
        user.ConnectionIntent = Profiler.Web.Profile.ConnectionIntent.Normalize(vm.ConnectionIntent);
        await _db.SaveChangesAsync();

        TempData["Success"] = "Your profile has been saved.";
        return RedirectToAction("Dashboard", "Sources");
    }

    [HttpGet("values")]
    public async Task<IActionResult> Values()
    {
        var user = await FindCurrentUserAsync();
        if (user == null) return await SignOutToHomeAsync();
        ViewBag.HasValues = user.ValuesOpenness.HasValue;
        return View(new ValuesViewModel());
    }

    /// <summary>
    /// Collects the questionnaire answers, derives the coarse bucket, stores only the bucket, and
    /// drops the answers. The raw answers exist only for the lifetime of this request — never saved,
    /// never logged — the same promise the interest fingerprint makes.
    /// </summary>
    [HttpPost("values")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Values(ValuesViewModel vm)
    {
        var user = await FindCurrentUserAsync();
        if (user == null) return await SignOutToHomeAsync();

        if (!vm.Consent)
            ModelState.AddModelError(nameof(vm.Consent), "Please confirm you understand before saving.");

        var bucket = Profiler.Web.Profile.ValuesQuestionnaire.DeriveBucket(vm.Answers);
        if (bucket is null)
            ModelState.AddModelError("", "Please answer every question to save your values profile.");

        if (!ModelState.IsValid)
        {
            ViewBag.HasValues = user.ValuesOpenness.HasValue;
            return View(vm);
        }

        user.ValuesOpenness = bucket;
        user.ValuesScheme = Profiler.Web.Profile.ValuesQuestionnaire.Version;
        await _db.SaveChangesAsync();

        TempData["Success"] = "Your values profile has been saved. Your answers were used to work it out and then discarded.";
        return RedirectToAction("Dashboard", "Sources");
    }

    [HttpPost("values/delete")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> DeleteValues()
    {
        var user = await FindCurrentUserAsync();
        if (user == null) return await SignOutToHomeAsync();

        user.ValuesOpenness = null;
        user.ValuesScheme = null;
        await _db.SaveChangesAsync();

        TempData["Success"] = "Your values profile has been removed.";
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

        var hidden = await _db.UserBlocks
            .Where(b => b.BlockerId == userId)
            .Join(_db.Users, b => b.BlockedId, u => u.Id, (b, u) => u.Username)
            .OrderBy(u => u)
            .ToListAsync();

        return new DataExportViewModel
        {
            Username = user.Username,
            CreatedAt = user.CreatedAt,
            IsDiscoverable = user.IsDiscoverable,
            Bio = user.Bio,
            Contact = user.Contact,
            ConnectionIntent = user.ConnectionIntent,
            ValuesOpenness = user.ValuesOpenness,
            FingerprintDimensions = dimensions,
            Sources = sources,
            HiddenPeople = hidden
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
        // Someone changing their password is usually doing it because a session may not be theirs
        // anymore. Cutting off every ticket issued so far is the point of the exercise; re-issuing
        // afterwards keeps the person who asked signed in.
        user.SessionsValidFrom = DateTime.UtcNow;
        await _db.SaveChangesAsync();
        await SignInUserAsync(user);

        TempData["Success"] = "Your password has been changed, and you've been signed out everywhere else.";
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
        // Blocks point both ways: the people you hid, and the people who hid you. The schema cascades
        // these, but doing it here too keeps the promise independent of whether the provider enforces
        // foreign keys.
        _db.UserBlocks.RemoveRange(_db.UserBlocks.Where(b => b.BlockerId == user.Id || b.BlockedId == user.Id));
        _db.Users.Remove(user);
        await _db.SaveChangesAsync();

        await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
        TempData["Success"] = "Your account and all associated data have been permanently deleted.";
        return Redirect("/");
    }
}
