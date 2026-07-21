using System.Security.Claims;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Profiler.Web.Data;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllersWithViews();
builder.Services.AddDbContext<AppDbContext>(opt =>
    opt.UseSqlite(builder.Configuration.GetConnectionString("Default") ?? "Data Source=profiler.db"));

// Persist the Data Protection key ring to disk so auth cookies stay valid across restarts and
// redeploys. The default location is ephemeral in containers, which would silently sign everyone
// out on every deploy. Override the folder with "DataProtection:KeyPath".
var keyPath = builder.Configuration["DataProtection:KeyPath"]
    ?? Path.Combine(builder.Environment.ContentRootPath, "keys");
Directory.CreateDirectory(keyPath);
builder.Services.AddDataProtection()
    .PersistKeysToFileSystem(new DirectoryInfo(keyPath))
    .SetApplicationName("Profiler.Web");

builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(opt =>
    {
        opt.LoginPath = "/account/login";
        opt.AccessDeniedPath = "/account/login";
        opt.ExpireTimeSpan = TimeSpan.FromDays(30);
        opt.SlidingExpiration = true;
        opt.Cookie.HttpOnly = true;
        opt.Cookie.SameSite = SameSiteMode.Lax;
        // Send the auth cookie only over HTTPS when the request is HTTPS (prod), while keeping
        // dev over plain HTTP working.
        opt.Cookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;
        opt.Events = new CookieAuthenticationEvents
        {
            // A persistent cookie can outlive its user row (account deletion elsewhere, DB restore).
            // Reject such cookies so a deleted account can never resurrect as a ghost session.
            OnValidatePrincipal = async ctx =>
            {
                var idClaim = ctx.Principal?.FindFirstValue(ClaimTypes.NameIdentifier);
                if (!int.TryParse(idClaim, out var userId))
                {
                    ctx.RejectPrincipal();
                    return;
                }
                var db = ctx.HttpContext.RequestServices.GetRequiredService<AppDbContext>();
                if (!await db.Users.AnyAsync(u => u.Id == userId))
                {
                    ctx.RejectPrincipal();
                    await ctx.HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
                }
            }
        };
    });

// Brute-force protection for the login endpoint (per client IP).
// Limit is configurable ("RateLimiting:LoginPermitLimit") so tests can relax it.
var loginPermitLimit = builder.Configuration.GetValue<int?>("RateLimiting:LoginPermitLimit") ?? 5;
builder.Services.AddRateLimiter(opt =>
{
    opt.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    opt.AddPolicy("login", ctx => RateLimitPartition.GetFixedWindowLimiter(
        ctx.Connection.RemoteIpAddress?.ToString() ?? "unknown",
        _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = loginPermitLimit,
            Window = TimeSpan.FromMinutes(1),
            QueueLimit = 0
        }));
    opt.OnRejected = async (ctx, token) =>
    {
        ctx.HttpContext.Response.ContentType = "text/plain";
        await ctx.HttpContext.Response.WriteAsync(
            "Too many login attempts. Please wait a minute and try again.", token);
    };
});

// External sources must not be able to hang a request for the default 100 seconds.
const long maxConnectorResponseBytes = 5 * 1024 * 1024;

builder.Services.AddHttpClient("connectors", client =>
{
    client.Timeout = TimeSpan.FromSeconds(15);
    // A hostile or broken endpoint must not be able to exhaust memory with a huge body.
    client.MaxResponseContentBufferSize = maxConnectorResponseBytes;
});

// RSS is the only connector that fetches user-supplied URLs, so it gets a locked-down client:
// auto-redirect is off and every hop is re-validated by SsrfGuard inside the connector.
builder.Services.AddHttpClient("rss-connector", client =>
{
    client.Timeout = TimeSpan.FromSeconds(15);
    client.MaxResponseContentBufferSize = maxConnectorResponseBytes;
})
.ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler { AllowAutoRedirect = false });

var app = builder.Build();

using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    db.Database.Migrate();
}

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Home/Error");
    app.UseHsts();
    app.UseHttpsRedirection();
}

app.UseStaticFiles();
app.UseRouting();
app.UseRateLimiter();
app.UseAuthentication();
app.UseAuthorization();
app.MapDefaultControllerRoute();

// Liveness/readiness probe for load balancers and uptime monitors. Anonymous by design, and
// deliberately reports nothing beyond reachability of the database.
app.MapGet("/health", async (AppDbContext db) =>
    await db.Database.CanConnectAsync()
        ? Results.Ok(new { status = "healthy" })
        : Results.Json(new { status = "unhealthy" }, statusCode: StatusCodes.Status503ServiceUnavailable));

app.Run();

// Exposed so the integration-test host (WebApplicationFactory<Program>) can boot the real app.
public partial class Program { }
