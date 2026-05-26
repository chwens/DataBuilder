using DataBuilder.Api.Models;
using DataBuilder.Core;
using DataBuilder.Core.Entities;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace DataBuilder.Api.Controllers;

public class ProjectController : Controller
{
    private readonly AppDbContext _db;

    public ProjectController(AppDbContext db)
    {
        _db = db;
    }

    // GET: /Project
    public async Task<IActionResult> Index()
    {
        var projects = await _db.Projects
            .OrderByDescending(p => p.CreatedAt)
            .ToListAsync();

        var model = new ProjectListViewModel
        {
            Projects = projects
        };

        return View(model);
    }

    // GET: /Project/Create
    public IActionResult Create()
    {
        return View();
    }

    // POST: /Project/Create
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(ProjectCreateViewModel model)
    {
        if (!ModelState.IsValid)
        {
            return View(model);
        }

        var project = new Project
        {
            Name = model.Name,
            Description = model.Description,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };

        _db.Projects.Add(project);
        await _db.SaveChangesAsync();

        return RedirectToAction("Detail", new { id = project.Id });
    }

    // GET: /Project/{id}
    public async Task<IActionResult> Detail(int id)
    {
        var project = await _db.Projects
            .Include(p => p.Documents)
            .FirstOrDefaultAsync(p => p.Id == id);

        if (project == null)
        {
            return NotFound();
        }

        var model = new ProjectDetailViewModel
        {
            Project = project,
            Documents = project.Documents.OrderByDescending(d => d.CreatedAt).ToList()
        };

        return View(model);
    }

    // POST: /Project/Delete/{id}
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Delete(int id)
    {
        var project = await _db.Projects.FindAsync(id);
        if (project != null)
        {
            _db.Projects.Remove(project);
            await _db.SaveChangesAsync();
        }

        return RedirectToAction("Index");
    }

    // GET: /Project/Settings/{id}
    public async Task<IActionResult> Settings(int id)
    {
        var project = await _db.Projects.FindAsync(id);
        if (project == null)
        {
            return NotFound();
        }

        var model = new ProjectSettingsViewModel
        {
            Project = project
        };

        return View(model);
    }

    // POST: /Project/UpdatePrompt/{id}
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> UpdatePrompt(int id, string? questionPrompt, string? answerPrompt)
    {
        var project = await _db.Projects.FindAsync(id);
        if (project == null)
        {
            return NotFound();
        }

        project.QuestionPrompt = string.IsNullOrWhiteSpace(questionPrompt) ? null : questionPrompt;
        project.AnswerPrompt = string.IsNullOrWhiteSpace(answerPrompt) ? null : answerPrompt;
        project.UpdatedAt = DateTime.UtcNow;

        await _db.SaveChangesAsync();

        return RedirectToAction("Settings", new { id });
    }
}
