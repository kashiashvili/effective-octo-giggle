using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace Profiler.Web.Security;

/// <summary>
/// Gates an operator-only endpoint behind the <c>Metrics:Token</c> bearer secret. Implemented as a
/// resource filter so it runs BEFORE model binding: an <c>[ApiController]</c>'s automatic 400 on an
/// invalid body would otherwise fire first and reveal that a route exists even when the feature is off,
/// defeating the "don't even admit the route exists" design. Unset token → 404 (feature off, hidden);
/// wrong/absent bearer → 401. The comparison is constant-time.
/// </summary>
public sealed class OperatorTokenAttribute : Attribute, IAsyncResourceFilter
{
    public const string TokenKey = "Metrics:Token";

    public Task OnResourceExecutionAsync(ResourceExecutingContext context, ResourceExecutionDelegate next)
    {
        var config = context.HttpContext.RequestServices.GetRequiredService<IConfiguration>();
        var configured = config[TokenKey];

        // No token configured means the feature is off; do not even reveal that the route exists.
        if (string.IsNullOrWhiteSpace(configured))
        {
            context.Result = new NotFoundResult();
            return Task.CompletedTask;
        }

        var presented = ExtractBearer(context.HttpContext.Request.Headers.Authorization.ToString());
        if (presented == null || !FixedTimeEquals(presented, configured))
        {
            context.Result = new UnauthorizedResult();
            return Task.CompletedTask;
        }

        return next();
    }

    private static string? ExtractBearer(string? header)
    {
        if (string.IsNullOrEmpty(header)) return null;
        const string prefix = "Bearer ";
        return header.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
            ? header[prefix.Length..].Trim()
            : null;
    }

    private static bool FixedTimeEquals(string a, string b) =>
        CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(a), Encoding.UTF8.GetBytes(b));
}
