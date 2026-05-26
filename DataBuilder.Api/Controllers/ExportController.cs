using DataBuilder.Core;
using DataBuilder.Core.Interfaces;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Text.RegularExpressions;

namespace DataBuilder.Api.Controllers;

public class ExportController : Controller
{
    private readonly AppDbContext _db;
    private readonly IAlpacaExporter _exporter;

    public ExportController(AppDbContext db, IAlpacaExporter exporter)
    {
        _db = db;
        _exporter = exporter;
    }

    // GET /Export/Download/{projectId}
    public async Task<IActionResult> Download(int projectId)
    {
        var project = await _db.Projects.FindAsync(projectId);
        if (project == null)
            return NotFound();

        var documentIds = await _db.Documents
            .Where(d => d.ProjectId == projectId)
            .Select(d => d.Id)
            .ToListAsync();

        var qaPairs = await _db.QAPairs
            .Include(q => q.Chunk)
            .Where(q => q.Answered && documentIds.Contains(q.Chunk!.DocumentId))
            .ToListAsync();

        var bytes = await _exporter.ExportAsync(qaPairs);
        var safeName = Regex.Replace(project.Name, @"[<>:""/\\|?*]", "_");
        var fileName = $"{safeName}_alpaca_{DateTime.UtcNow:yyyyMMdd_HHmmss}.jsonl";

        return File(bytes, "application/x-ndjson", fileName);
    }
}
