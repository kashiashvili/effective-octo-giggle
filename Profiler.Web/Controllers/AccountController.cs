using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Profiler.Web.Data;
using Profiler.Web.Data.Models;
using Profiler.Web.ViewModels;

namespace Profiler.Web.Controllers;

[Route("account")]
public class AccountController : Controller
{
    private readonly AppDbContext _db;

    public AccountController(AppDbContext db) => _db = db;

    [HttpGet("register")]
    public IActionResult Register() => View();

    [HttpPost("register")]
    public async Task<IActionResult> Register(RegisterViewModel vm)
    {
        if (!ModelState.IsValid) return View(vm);

        if (await _db.Users.AnyAsync(u => u.Username == vm.Username))
        {
            ModelState.AddModelError(nameof(vm.Username), "Username already taken.");
            return View(vm);
        }

        var user = new AppUser
        {
            Username = vm.Username,
            PasswordHash = BCrypt.Net.BCrypt.HashPassword(vm.Password)
        };
        _db.Users.Add(user);
        await _db.SaveChangesAsync();
        TempData["Success"] = "Account created! Please log in.";
        return RedirectToAction(nameof(Login));
    }

    [HttpGet("login")]
    public IActionResult Login() => View();

    [HttpPost("login")]
    public async Task<IActionResult> Login(LoginViewModel vm)
    {
        if (!ModelState.IsValid) return View(vm);

        var user = await _db.Users.FirstOrDefaultAsync(u => u.Username == vm.Username);
        if (user == null || !BCrypt.Net.BCrypt.Verify(vm.Password, user.PasswordHash))
        {
            ModelState.AddModelError("", "Invalid username or password.");
            return View(vm);
        }

        HttpContext.Session.SetInt32("UserId", user.Id);
        HttpContext.Session.SetString("Username", user.Username);
        return RedirectToAction("Dashboard", "Sources");
    }

    [HttpGet("logout")]
    public IActionResult Logout()
    {
        HttpContext.Session.Clear();
        return Redirect("/");
    }
}
