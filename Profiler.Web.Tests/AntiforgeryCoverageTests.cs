using System.Reflection;
using Microsoft.AspNetCore.Mvc;
using Xunit;

namespace Profiler.Web.Tests;

/// <summary>
/// Every state-changing action must be CSRF-protected. Checking this by reading the controllers
/// only proves it for the actions that exist today, so it is asserted by reflection instead: a
/// new POST that forgets the attribute fails the build's test run rather than shipping a hole.
/// </summary>
public class AntiforgeryCoverageTests
{
    private static IEnumerable<MethodInfo> PostActions() =>
        typeof(Profiler.Web.Controllers.AccountController).Assembly
            .GetTypes()
            .Where(t => typeof(Controller).IsAssignableFrom(t) && !t.IsAbstract)
            .SelectMany(t => t.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly))
            .Where(m => m.GetCustomAttributes<HttpPostAttribute>().Any());

    [Fact]
    public void EveryPostAction_RequiresAnAntiforgeryToken()
    {
        var unprotected = PostActions()
            .Where(m => !m.GetCustomAttributes<ValidateAntiForgeryTokenAttribute>().Any())
            .Select(m => $"{m.DeclaringType!.Name}.{m.Name}")
            .ToList();

        Assert.Empty(unprotected);
    }

    [Fact]
    public void ThereArePostActionsToCheck()
    {
        // Guards the test above from silently passing if the discovery ever stops finding anything.
        Assert.NotEmpty(PostActions());
    }
}
