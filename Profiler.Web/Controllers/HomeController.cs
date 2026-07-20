using Microsoft.AspNetCore.Mvc;

namespace Profiler.Web.Controllers;

public class HomeController : Controller
{
    public IActionResult Index() => View();
}
