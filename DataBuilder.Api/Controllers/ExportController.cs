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
    public async Task<IActionResult> Download(int projectId, string? qaIds = null)
    {
        var project = await _db.Projects.FindAsync(projectId);
        if (project == null)
            return NotFound();

        var documentIds = await _db.Documents
            .Where(d => d.ProjectId == projectId)
            .Select(d => d.Id)
            .ToListAsync();

        var query = _db.QAPairs
            .Include(q => q.Chunk)
            .Where(q => q.Answered && documentIds.Contains(q.Chunk!.DocumentId));

        // 如果指定了 qaIds，按指定 ID 进一步筛选
        if (!string.IsNullOrEmpty(qaIds))
        {
            var idList = ParseQaIds(qaIds);
            if (idList.Count > 0)
                query = query.Where(q => idList.Contains(q.Id));
        }

        var qaPairs = await query.ToListAsync();

        var bytes = await _exporter.ExportAsync(qaPairs);
        var safeName = Regex.Replace(project.Name, @"[<>:""/\\|?*]", "_");
        var fileName = $"{safeName}_alpaca_{DateTime.UtcNow:yyyyMMdd_HHmmss}.jsonl";

        return File(bytes, "application/x-ndjson", fileName);
    }

    // GET /Export/ExportSelected?qaIds=1,2,3
    public async Task<IActionResult> ExportSelected(string qaIds)
    {
        var idList = ParseQaIds(qaIds);
        if (idList.Count == 0)
            return BadRequest("未提供有效的问答对 ID。");

        var qaPairs = await _db.QAPairs
            .Include(q => q.Chunk)
            .Where(q => q.Answered && idList.Contains(q.Id))
            .ToListAsync();

        var bytes = await _exporter.ExportAsync(qaPairs);
        var fileName = $"alpaca_selected_{DateTime.UtcNow:yyyyMMdd_HHmmss}.jsonl";

        return File(bytes, "application/x-ndjson", fileName);
    }

    /// <summary>
    /// 解析逗号分隔的 QA ID 字符串为 int 列表
    /// </summary>
    private static List<int> ParseQaIds(string qaIds)
    {
        return qaIds
            .Split(',', StringSplitOptions.RemoveEmptyEntries)
            .Select(s => int.TryParse(s.Trim(), out var id) ? id : (int?)null)
            .Where(id => id.HasValue)
            .Select(id => id!.Value)
            .ToList();
    }
}
