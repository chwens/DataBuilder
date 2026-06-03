using DataBuilder.Core;
using DataBuilder.Core.Entities;
using DataBuilder.Core.Interfaces;
using DataBuilder.Api.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace DataBuilder.Api.Controllers;

public class QAController : Controller
{
    private readonly AppDbContext _db;
    private readonly ILLMService _llm;
    private readonly ILogger<QAController> _logger;

    public QAController(AppDbContext db, ILLMService llm, ILogger<QAController> logger)
    {
        _db = db;
        _llm = llm;
        _logger = logger;
    }

    // GET /QA/Preview/{documentId}
    public async Task<IActionResult> Preview(int documentId, string? filterType = null)
    {
        var document = await _db.Documents
            .Include(d => d.Project)
            .FirstOrDefaultAsync(d => d.Id == documentId);

        if (document == null)
            return NotFound();

        var query = _db.QAPairs
            .Include(q => q.Chunk)
            .Where(q => q.Chunk!.DocumentId == documentId);

        if (!string.IsNullOrEmpty(filterType))
            query = query.Where(q => q.Type == filterType);

        var qaPairs = await query
            .OrderBy(q => q.Id)
            .ToListAsync();

        var vm = new QAPreviewViewModel
        {
            Document = document,
            QAPairs = qaPairs,
            QaType = "Factoid",
            CountPerChunk = 3,
            FilterType = filterType,
            TypeList = new List<string> { "Factoid", "Reasoning", "Summary" },
            TotalCount = qaPairs.Count,
            AnsweredCount = qaPairs.Count(q => q.Answered)
        };

        return View(vm);
    }

    // GET /QA/ProjectPreview/{projectId}
    public async Task<IActionResult> ProjectPreview(int projectId, string? filterType = null)
    {
        var project = await _db.Projects
            .Include(p => p.Documents)
            .FirstOrDefaultAsync(p => p.Id == projectId);

        if (project == null)
            return NotFound();

        var documentIds = project.Documents.Select(d => d.Id).ToList();

        var query = _db.QAPairs
            .Include(q => q.Chunk)
            .Where(q => documentIds.Contains(q.Chunk!.DocumentId));

        if (!string.IsNullOrEmpty(filterType))
            query = query.Where(q => q.Type == filterType);

        var qaPairs = await query
            .OrderBy(q => q.Id)
            .ToListAsync();

        // 取第一个文档作为 ViewModel 的 Document 上下文
        var firstDocument = project.Documents.OrderBy(d => d.Id).FirstOrDefault()
            ?? new Document { Id = 0, FileName = project.Name };

        var vm = new QAPreviewViewModel
        {
            Document = firstDocument,
            QAPairs = qaPairs,
            QaType = "Factoid",
            CountPerChunk = 3,
            FilterType = filterType,
            TypeList = new List<string> { "Factoid", "Reasoning", "Summary" },
            TotalCount = qaPairs.Count,
            AnsweredCount = qaPairs.Count(q => q.Answered)
        };

        ViewData["ProjectId"] = projectId;
        ViewData["IsProjectView"] = true;

        return View("Preview", vm);
    }

    // POST /QA/GenerateQuestions
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> GenerateQuestions(int documentId, string qaType, int countPerChunk)
    {
        // 白名单校验 qaType
        var allowedTypes = new[] { "Factoid", "Reasoning", "Summary" };
        if (!allowedTypes.Contains(qaType ?? ""))
            qaType = "Factoid";

        var document = await _db.Documents
            .Include(d => d.Project)
            .Include(d => d.Chunks)
            .FirstOrDefaultAsync(d => d.Id == documentId);

        if (document == null)
            return NotFound();

        // 加载项目关联的 LLMConfig
        LLMConfig? llmConfig = null;
        if (document.Project?.LLMConfigId != null)
        {
            llmConfig = await _db.LLMConfigs
                .IgnoreQueryFilters()
                .FirstOrDefaultAsync(c => c.Id == document.Project.LLMConfigId);
        }

        // 如果配置了 LLMConfig 但已失效，阻止操作
        if (llmConfig != null && llmConfig.IsDeleted)
        {
            TempData["ErrorMessage"] = "当前项目使用的模型配置已失效，请重新选择模型。";
            return RedirectToAction("Detail", "Project", new { id = document.ProjectId });
        }

        // 如果没有选择 LLMConfig，返回错误提示
        if (llmConfig == null)
        {
            TempData["ErrorMessage"] = "请先在项目设置中选择模型配置。";
            return RedirectToAction("Detail", "Project", new { id = document.ProjectId });
        }

        var chunks = document.Chunks.OrderBy(c => c.Sequence).ToList();
        var customPrompt = document.Project?.QuestionPrompt;

        try
        {
            // CR-10: 在 LLM 调用之前设置 Generating 状态，防止用户重复点击
            document.Status = DocumentStatus.Generating;
            await _db.SaveChangesAsync();

            // 先生成所有新问题（收集到内存），全部成功后再替换旧数据
            var newQaPairs = new List<QAPair>();
            foreach (var chunk in chunks)
            {
                List<string> questions;
                questions = await _llm.GenerateQuestionsAsync(
                    chunk.TextContent, llmConfig,
                    qaType!, countPerChunk, customPrompt,
                    HttpContext.RequestAborted);

                foreach (var question in questions)
                {
                    newQaPairs.Add(new QAPair
                    {
                        ChunkId = chunk.Id,
                        Instruction = question,
                        Type = qaType!,
                        Answered = false
                    });
                }
            }

            // CR-7: 用事务包裹 QA 替换操作，保证数据一致性
            using var transaction = await _db.Database.BeginTransactionAsync();
            try
            {
                var existingQas = await _db.QAPairs
                    .Where(q => q.Chunk!.DocumentId == documentId)
                    .ToListAsync();
                _db.QAPairs.RemoveRange(existingQas);
                _db.QAPairs.AddRange(newQaPairs);
                await _db.SaveChangesAsync();

                await transaction.CommitAsync();
            }
            catch
            {
                await transaction.RollbackAsync();
                throw;
            }

            _logger.LogInformation("问题生成完成: DocumentId={DocumentId}, 类型={QaType}, 数量={Count}", documentId, qaType, newQaPairs.Count);

            TempData["Message"] = $"已为 {chunks.Count} 个文本片段生成问题。";
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "问题生成失败: DocumentId={DocumentId}", documentId);
            TempData["ErrorMessage"] = $"问题生成失败：{ex.Message}";
        }

        return RedirectToAction(nameof(Preview), new { documentId });
    }

    // POST /QA/GenerateAnswers/{documentId}
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> GenerateAnswers(int documentId)
    {
        var document = await _db.Documents
            .Include(d => d.Project)
            .FirstOrDefaultAsync(d => d.Id == documentId);

        if (document == null)
            return NotFound();

        // 加载项目关联的 LLMConfig
        LLMConfig? llmConfig = null;
        if (document.Project?.LLMConfigId != null)
        {
            llmConfig = await _db.LLMConfigs
                .IgnoreQueryFilters()
                .FirstOrDefaultAsync(c => c.Id == document.Project.LLMConfigId);
        }

        // 如果配置了 LLMConfig 但已失效，阻止操作
        if (llmConfig != null && llmConfig.IsDeleted)
        {
            TempData["ErrorMessage"] = "当前项目使用的模型配置已失效，请重新选择模型。";
            return RedirectToAction("Detail", "Project", new { id = document.ProjectId });
        }

        // 如果没有选择 LLMConfig，返回错误提示
        if (llmConfig == null)
        {
            TempData["ErrorMessage"] = "请先在项目设置中选择模型配置。";
            return RedirectToAction("Detail", "Project", new { id = document.ProjectId });
        }

        var qaPairs = await _db.QAPairs
            .Include(q => q.Chunk)
            .Where(q => q.Chunk!.DocumentId == documentId && !q.Answered)
            .ToListAsync();

        var customPrompt = document.Project?.AnswerPrompt;

        int completed = 0;
        try
        {
            foreach (var qa in qaPairs)
            {
                string answer;
                answer = await _llm.GenerateAnswerAsync(
                    qa.Chunk!.TextContent,
                    qa.Instruction,
                    llmConfig,
                    customPrompt,
                    HttpContext.RequestAborted);

                qa.Output = answer;
                qa.Answered = true;

                // 每个问题完成后立即持久化，避免 LLM 调用失败时全部丢失
                await _db.SaveChangesAsync();
                completed++;
            }

            document.Status = DocumentStatus.Done;
            await _db.SaveChangesAsync();

            _logger.LogInformation("答案生成完成: DocumentId={DocumentId}, 成功={Completed}/{Total}", documentId, completed, qaPairs.Count);

            TempData["Message"] = $"已为 {qaPairs.Count} 个问题生成答案。";
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "答案生成中断: DocumentId={DocumentId}, 已完成={Completed}/{Total}", documentId, completed, qaPairs.Count);

            TempData["ErrorMessage"] = $"答案生成中断：已完成 {completed}/{qaPairs.Count}。错误：{ex.Message}";
        }

        return RedirectToAction(nameof(Preview), new { documentId });
    }

    // GET /QA/EditQuestion/{id}
    public async Task<IActionResult> EditQuestion(int id)
    {
        var qaPair = await _db.QAPairs
            .Include(q => q.Chunk)
            .FirstOrDefaultAsync(q => q.Id == id);
        if (qaPair == null)
            return NotFound();

        var vm = new QAEditViewModel
        {
            QAPair = qaPair,
            ReturnUrl = Url.Action(nameof(Preview), new { documentId = qaPair.Chunk!.DocumentId })
        };

        return View(vm);
    }

    // POST /QA/EditQuestion/{id}
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> EditQuestion(int id, string instruction)
    {
        var qaPair = await _db.QAPairs
            .Include(q => q.Chunk)
            .FirstOrDefaultAsync(q => q.Id == id);
        if (qaPair == null)
            return NotFound();

        qaPair.Instruction = instruction ?? string.Empty;
        await _db.SaveChangesAsync();

        TempData["Message"] = "问题已更新。";
        return RedirectToAction(nameof(Preview), new { documentId = qaPair.Chunk!.DocumentId });
    }

    // GET /QA/EditAnswer/{id}
    public async Task<IActionResult> EditAnswer(int id)
    {
        var qaPair = await _db.QAPairs
            .Include(q => q.Chunk)
            .FirstOrDefaultAsync(q => q.Id == id);
        if (qaPair == null)
            return NotFound();

        var vm = new QAEditViewModel
        {
            QAPair = qaPair,
            ReturnUrl = Url.Action(nameof(Preview), new { documentId = qaPair.Chunk!.DocumentId })
        };

        return View(vm);
    }

    // POST /QA/EditAnswer/{id}
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> EditAnswer(int id, string output)
    {
        var qaPair = await _db.QAPairs
            .Include(q => q.Chunk)
            .FirstOrDefaultAsync(q => q.Id == id);
        if (qaPair == null)
            return NotFound();

        qaPair.Output = output ?? string.Empty;
        qaPair.Answered = true; // 手动编辑答案即标记为已回答
        await _db.SaveChangesAsync();

        TempData["Message"] = "答案已更新。";
        return RedirectToAction(nameof(Preview), new { documentId = qaPair.Chunk!.DocumentId });
    }

    // POST /QA/Delete/{id}
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Delete(int id)
    {
        var qaPair = await _db.QAPairs
            .Include(q => q.Chunk)
            .FirstOrDefaultAsync(q => q.Id == id);
        if (qaPair == null)
            return NotFound();

        var documentId = qaPair.Chunk!.DocumentId;
        _db.QAPairs.Remove(qaPair);
        await _db.SaveChangesAsync();

        _logger.LogInformation("QA对已删除: Id={Id}", id);

        TempData["Message"] = "问答对已删除。";
        return RedirectToAction(nameof(Preview), new { documentId });
    }
}
