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

    public QAController(AppDbContext db, ILLMService llm)
    {
        _db = db;
        _llm = llm;
    }

    // GET /QA/Preview/{documentId}
    public async Task<IActionResult> Preview(int documentId)
    {
        var document = await _db.Documents
            .Include(d => d.Project)
            .FirstOrDefaultAsync(d => d.Id == documentId);

        if (document == null)
            return NotFound();

        var qaPairs = await _db.QAPairs
            .Include(q => q.Chunk)
            .Where(q => q.Chunk!.DocumentId == documentId)
            .OrderBy(q => q.Id)
            .ToListAsync();

        var vm = new QAPreviewViewModel
        {
            Document = document,
            QAPairs = qaPairs,
            QaType = "Factoid",
            CountPerChunk = 3
        };

        return View(vm);
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

        var chunks = document.Chunks.OrderBy(c => c.Sequence).ToList();
        var customPrompt = document.Project?.QuestionPrompt;

        // 先生成所有新问题（收集到内存），全部成功后再替换旧数据
        var newQaPairs = new List<QAPair>();
        foreach (var chunk in chunks)
        {
            var questions = await _llm.GenerateQuestionsAsync(
                chunk.TextContent,
                qaType!,
                countPerChunk,
                customPrompt,
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

        // 全部生成成功后才替换旧数据
        var existingQas = await _db.QAPairs
            .Where(q => q.Chunk!.DocumentId == documentId)
            .ToListAsync();
        _db.QAPairs.RemoveRange(existingQas);
        _db.QAPairs.AddRange(newQaPairs);
        await _db.SaveChangesAsync();

        document.Status = DocumentStatus.Generating;
        await _db.SaveChangesAsync();

        TempData["Message"] = $"已为 {chunks.Count} 个文本片段生成问题。";
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
                var answer = await _llm.GenerateAnswerAsync(
                    qa.Chunk!.TextContent,
                    qa.Instruction,
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

            TempData["Message"] = $"已为 {qaPairs.Count} 个问题生成答案。";
        }
        catch (Exception ex)
        {
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

        TempData["Message"] = "问答对已删除。";
        return RedirectToAction(nameof(Preview), new { documentId });
    }
}
