using System.Net.Sockets;
using System.Threading.RateLimiting;
using Profiler.Web.Connectors;
using Profiler.Web.Security;
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
                if (!ctx.Principal.TryGetUserId(out var userId))
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

// Unlimited registration lets one person flood the matching pool with sybil accounts, which
// degrades match quality for real users and is the enabling step for harvesting the contact
// lines that matches expose. A generous hourly window is enough to stop that without ever
// bothering someone who is just having trouble picking a username.
// Limit is configurable ("RateLimiting:RegisterPermitLimit") so tests can relax it.
var registerPermitLimit = builder.Configuration.GetValue<int?>("RateLimiting:RegisterPermitLimit") ?? 5;

// The connect endpoint makes outbound requests to third-party APIs (and, via the RSS connector,
// to whatever URL the caller supplies), so an unmetered connect lets this host be used to hammer
// third parties on someone else's behalf.
// Limit is configurable ("RateLimiting:ConnectPermitLimit") so tests can relax it.
var connectPermitLimit = builder.Configuration.GetValue<int?>("RateLimiting:ConnectPermitLimit") ?? 10;

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
    opt.AddPolicy("register", ctx => RateLimitPartition.GetFixedWindowLimiter(
        ctx.Connection.RemoteIpAddress?.ToString() ?? "unknown",
        _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = registerPermitLimit,
            Window = TimeSpan.FromHours(1),
            QueueLimit = 0
        }));
    opt.AddPolicy("connect", ctx => RateLimitPartition.GetFixedWindowLimiter(
        ctx.Connection.RemoteIpAddress?.ToString() ?? "unknown",
        _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = connectPermitLimit,
            Window = TimeSpan.FromMinutes(1),
            QueueLimit = 0
        }));
    // Rejection happens before MVC, so this renders a small page directly rather than a view.
    // It links the app's own stylesheet instead of duplicating any of it.
    opt.OnRejected = async (ctx, token) =>
    {
        ctx.HttpContext.Response.ContentType = "text/html; charset=utf-8";
        ctx.HttpContext.Response.Headers.RetryAfter = "60";
        await ctx.HttpContext.Response.WriteAsync("""
            <!DOCTYPE html>
            <html lang="en">
            <head>
                <meta charset="UTF-8">
                <meta name="viewport" content="width=device-width, initial-scale=1.0">
                <title>Too many attempts – Profiler</title>
                <link rel="stylesheet" href="/css/style.css">
            </head>
            <body>
                <main class="page-wrap">
                    <div class="container">
                        <div class="empty-state">
                            <div class="empty-icon">⏳</div>
                            <h4>Too many attempts</h4>
                            <p>For your account's safety we pause sign-in attempts for a minute. Please wait, then try again.</p>
                            <p class="mt-4"><a href="/account/login" class="btn btn-primary">Back to sign in</a></p>
                        </div>
                    </div>
                </main>
            </body>
            </html>
            """, token);
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
// auto-redirect is off (the connector follows hops itself, re-validating each one) and the
// connection is pinned to addresses SsrfGuard has just approved, so a name cannot resolve to
// something public during the check and something internal when the socket is opened.
builder.Services.AddHttpClient("rss-connector", client =>
{
    client.Timeout = TimeSpan.FromSeconds(15);
    client.MaxResponseContentBufferSize = maxConnectorResponseBytes;
})
.ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler
{
    AllowAutoRedirect = false,
    ConnectCallback = async (context, cancellationToken) =>
    {
        var addresses = await SsrfGuard.ResolveAllowedAsync(context.DnsEndPoint.Host, cancellationToken);

        var socket = new Socket(SocketType.Stream, ProtocolType.Tcp) { NoDelay = true };
        try
        {
            await socket.ConnectAsync(addresses, context.DnsEndPoint.Port, cancellationToken);
            return new NetworkStream(socket, ownsSocket: true);
        }
        catch
        {
            socket.Dispose();
            throw;
        }
    }
});

// Off unless configured; see ProxyTrust for why believing these headers is opt-in and why enabling
// them without naming trusted proxies is refused outright.
var forwardedHeaders = Profiler.Web.Security.ProxyTrust.Build(builder.Configuration);

var app = builder.Build();

// Must run before anything reads the client address or scheme — the rate limiters partition on the
// former and HTTPS redirection depends on the latter.
if (forwardedHeaders != null) app.UseForwardedHeaders(forwardedHeaders);

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
