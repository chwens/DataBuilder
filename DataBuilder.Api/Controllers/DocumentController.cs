using DataBuilder.Api.Models;
using DataBuilder.Core;
using DataBuilder.Core.Entities;
using DataBuilder.Core.Interfaces;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace DataBuilder.Api.Controllers;

public class DocumentController : Controller
{
    private readonly AppDbContext _db;
    private readonly IDocumentParser _parser;
    private readonly ILogger<DocumentController> _logger;

    public DocumentController(AppDbContext db, IDocumentParser parser, ILogger<DocumentController> logger)
    {
        _db = db;
        _parser = parser;
        _logger = logger;
    }

    // GET: /Document/Upload/{projectId}
    public async Task<IActionResult> Upload(int projectId)
    {
        ViewData["ProjectId"] = projectId; // Phase 2C: 让 _Layout 渲染项目内 4 Tab 容器

        var project = await _db.Projects.FindAsync(projectId);
        if (project == null)
        {
            return NotFound();
        }

        var vm = new DocumentUploadViewModel
        {
            ProjectId = projectId,
            ProjectName = project.Name
        };

        // Phase 5: 让"固定长度分段"描述动态显示项目配置的最大分段长度
        ViewData["ChunkMaxLength"] = project.ChunkMaxLength;

        return View(vm);
    }

    // POST: /Document/Upload
    [HttpPost]
    [ValidateAntiForgeryToken]
    [RequestSizeLimit(10_485_760)]
    [RequestFormLimits(MultipartBodyLengthLimit = 10_485_760)]
    public async Task<IActionResult> Upload(int projectId, IFormFile file, string strategy = "Heading")
    {
        // 验证 projectId 有效性
        if (projectId <= 0)
            return BadRequest("无效的项目 ID。");

        // 验证文件存在
        if (file == null || file.Length == 0)
        {
            ModelState.AddModelError("file", "请选择一个文件。");
            return await RebuildUploadView(projectId);
        }

        // 验证文件扩展名
        var ext = Path.GetExtension(file.FileName).ToLowerInvariant();
        if (ext != ".md" && ext != ".txt")
        {
            ModelState.AddModelError("file", "仅支持 .md 和 .txt 文件。");
            return await RebuildUploadView(projectId);
        }

        // 验证 MIME 类型
        var allowedTypes = new[] { "text/plain", "text/markdown", "text/x-markdown", "application/octet-stream" };
        if (!allowedTypes.Contains(file.ContentType?.ToLowerInvariant() ?? ""))
        {
            ModelState.AddModelError("file", "不支持的文件类型。");
            return await RebuildUploadView(projectId);
        }

        // 读取文件内容
        using var reader = new StreamReader(file.OpenReadStream());
        var content = await reader.ReadToEndAsync();

        var doc = new Document
        {
            ProjectId = projectId,
            FileName = file.FileName,
            Content = content,
            Status = DocumentStatus.Uploaded
        };

        _db.Documents.Add(doc);
        await _db.SaveChangesAsync();

        _logger.LogInformation("文档上传: {FileName}, 项目Id={ProjectId}", doc.FileName, projectId);

        TempData["ParseStrategy"] = strategy;
        TempData["SuccessMessage"] = $"文档 \"{file.FileName}\" 上传成功。";

        return RedirectToAction(nameof(Chunks), new { id = doc.Id });
    }

    // POST: /Document/Parse/{id}
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Parse(int id, string strategy = "Heading", int? chunkSize = null)
    {
        var doc = await _db.Documents.FindAsync(id);
        if (doc == null)
        {
            return NotFound();
        }

        // Phase 3D: 从所属 Project 读取分块参数（前端未传时回退到项目配置）
        var project = await _db.Projects.FindAsync(doc.ProjectId);
        if (project == null)
        {
            return NotFound();
        }

        var effectiveChunkSize = chunkSize ?? project.ChunkMaxLength;
        var effectiveChunkMinLength = project.ChunkMinLength;

        doc.Status = DocumentStatus.Parsing;
        await _db.SaveChangesAsync();

        try
        {
            var chunks = _parser.Parse(doc.FileName, doc.Content, strategy, effectiveChunkSize, effectiveChunkMinLength);

            foreach (var chunk in chunks)
            {
                chunk.DocumentId = id;
                _db.Chunks.Add(chunk);
            }

            doc.Status = DocumentStatus.Parsed;
            await _db.SaveChangesAsync();

            _logger.LogInformation("文档解析完成: Id={DocumentId}, 策略={Strategy}, chunkSize={ChunkSize}, chunkMinLength={ChunkMinLength}, 分段数={Count}",
                id, strategy, effectiveChunkSize, effectiveChunkMinLength, chunks.Count);

            TempData["SuccessMessage"] = $"文档解析完成，共生成 {chunks.Count} 个分段。";
        }
        catch (Exception ex)
        {
            doc.Status = DocumentStatus.Uploaded;
            await _db.SaveChangesAsync();

            _logger.LogError(ex, "文档解析失败: Id={DocumentId}", id);

            TempData["ErrorMessage"] = $"解析失败：{ex.Message}";
        }

        return RedirectToAction(nameof(Chunks), new { id });
    }

    // GET: /Document/Chunks/{id}
    public async Task<IActionResult> Chunks(int id)
    {
        var doc = await _db.Documents
            .Include(d => d.Chunks)
            .FirstOrDefaultAsync(d => d.Id == id);

        if (doc == null)
        {
            return NotFound();
        }

        ViewData["ProjectId"] = doc.ProjectId; // Phase 2C: 让 _Layout 渲染项目内 4 Tab 容器

        var project = await _db.Projects.FindAsync(doc.ProjectId);

        var vm = new DocumentChunksViewModel
        {
            Document = doc,
            Chunks = doc.Chunks.OrderBy(c => c.Sequence).ToList(),
            ProjectChunkMaxLength = project?.ChunkMaxLength ?? 4000
        };

        if (TempData["ParseStrategy"] is string strategy)
        {
            ViewData["ParseStrategy"] = strategy;
        }

        return View(vm);
    }

    // POST: /Document/BatchDelete
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> BatchDelete(string ids)
    {
        if (string.IsNullOrWhiteSpace(ids))
            return BadRequest("未选择任何文档。");

        var idList = ids.Split(',', StringSplitOptions.RemoveEmptyEntries)
            .Select(s => int.TryParse(s.Trim(), out var id) ? id : (int?)null)
            .Where(id => id.HasValue)
            .Select(id => id!.Value)
            .ToList();

        if (idList.Count == 0)
            return BadRequest("无效的文档 ID。");

        var docs = await _db.Documents.Where(d => idList.Contains(d.Id)).ToListAsync();
        if (docs.Count == 0)
            return NotFound();

        var projectId = docs[0].ProjectId;
        _db.Documents.RemoveRange(docs);
        await _db.SaveChangesAsync();

        _logger.LogInformation("批量删除文档: Ids={Ids}, 数量={Count}", ids, docs.Count);

        TempData["SuccessMessage"] = $"已删除 {docs.Count} 篇文档。";
        return RedirectToAction("Detail", "Project", new { id = projectId });
    }

    // POST: /Document/Delete/{id}
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Delete(int id)
    {
        var doc = await _db.Documents.FindAsync(id);
        if (doc == null)
        {
            return NotFound();
        }

        var projectId = doc.ProjectId;
        _db.Documents.Remove(doc);
        await _db.SaveChangesAsync();

        _logger.LogInformation("文档已删除: Id={DocumentId}", id);

        TempData["SuccessMessage"] = $"文档 \"{doc.FileName}\" 已删除。";

        return RedirectToAction("Detail", "Project", new { id = projectId });
    }

    /// <summary>
    /// 验证失败时重新构建上传页面
    /// </summary>
    private async Task<IActionResult> RebuildUploadView(int projectId)
    {
        ViewData["ProjectId"] = projectId; // Phase 2C: 让 _Layout 渲染项目内 4 Tab 容器
        var project = await _db.Projects.FindAsync(projectId);
        var vm = new DocumentUploadViewModel
        {
            ProjectId = projectId,
            ProjectName = project?.Name ?? string.Empty
        };
        // Phase 5: 与 Upload GET 保持一致，让"固定长度分段"描述动态显示
        ViewData["ChunkMaxLength"] = project?.ChunkMaxLength ?? 2000;
        return View("Upload", vm);
    }
}
