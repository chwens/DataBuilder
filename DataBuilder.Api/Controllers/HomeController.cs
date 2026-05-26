using Microsoft.AspNetCore.Mvc;

namespace DataBuilder.Api.Controllers;

public class HomeController : Controller
{
    public IActionResult Index()
    {
        return View();
    }

    [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
    public IActionResult Error()
    {
        ViewData["RequestId"] = HttpContext.TraceIdentifier;
        return View();
    }
}
