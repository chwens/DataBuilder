using DataBuilder.Core;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace DataBuilder.Api.Controllers;

public class HomeController : Controller
{
    private readonly AppDbContext _db;
    private readonly ILogger<HomeController> _logger;

    public HomeController(AppDbContext db, ILogger<HomeController> logger)
    {
        _db = db;
        _logger = logger;
    }

    public async Task<IActionResult> Index()
    {
        var projects = await _db.Projects
            .AsNoTracking()
            .Include(p => p.Documents)
                .ThenInclude(d => d.Chunks)
                    .ThenInclude(c => c.QAPairs)
            .OrderByDescending(p => p.CreatedAt)
            .ToListAsync();

        return View(projects);
    }

    [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
    public IActionResult Error()
    {
        ViewData["RequestId"] = HttpContext.TraceIdentifier;
        return View();
    }
}
