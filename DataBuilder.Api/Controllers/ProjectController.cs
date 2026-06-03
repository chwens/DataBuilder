using DataBuilder.Api.Models;
using DataBuilder.Core;
using DataBuilder.Core.Entities;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace DataBuilder.Api.Controllers;

public class ProjectController : Controller
{
    private readonly AppDbContext _db;
    private readonly ILogger<ProjectController> _logger;

    public ProjectController(AppDbContext db, ILogger<ProjectController> logger)
    {
        _db = db;
        _logger = logger;
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

        _logger.LogInformation("项目创建成功: {ProjectName} (Id={ProjectId})", project.Name, project.Id);

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

        var llmConfigs = await _db.LLMConfigs
            .OrderBy(c => c.Provider)
            .ThenBy(c => c.ModelName)
            .ToListAsync();

        LLMConfig? currentConfig = null;
        if (project.LLMConfigId != null)
        {
            currentConfig = await _db.LLMConfigs
                .IgnoreQueryFilters()
                .FirstOrDefaultAsync(c => c.Id == project.LLMConfigId);
        }

        ViewData["LLMConfigs"] = llmConfigs;
        ViewData["CurrentLLMConfig"] = currentConfig;

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

            _logger.LogInformation("项目已删除: Id={ProjectId}", id);
        }

        return RedirectToAction("Index", "Home");
    }

    // POST: /Project/UpdateLLMConfig/{id}
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> UpdateLLMConfig(int id, int? llmConfigId)
    {
        var project = await _db.Projects.FindAsync(id);
        if (project == null) return NotFound();

        if (llmConfigId != null)
        {
            var configExists = await _db.LLMConfigs.AnyAsync(c => c.Id == llmConfigId.Value);
            if (!configExists)
            {
                TempData["ErrorMessage"] = "所选模型配置不存在或已被删除。";
                return RedirectToAction("Detail", new { id });
            }
        }

        project.LLMConfigId = llmConfigId;
        project.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync();

        TempData["SuccessMessage"] = llmConfigId == null
            ? "已切换为默认模型 (MiniMax)。"
            : "模型配置已更新。";

        return RedirectToAction("Detail", new { id });
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
            Project = project,
            DefaultQuestionPrompt = DefaultTemplates.QuestionPrompt,
            DefaultAnswerPrompt = DefaultTemplates.AnswerPrompt,
            EffectiveQuestionPrompt = project.QuestionPrompt ?? DefaultTemplates.QuestionPrompt,
            EffectiveAnswerPrompt = project.AnswerPrompt ?? DefaultTemplates.AnswerPrompt
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

/// <summary>
/// 系统默认 Prompt 模板，与 DataBuilder.Core.Services.LLMService 中的默认模板保持一致。
/// {qaType} 和 {count} 为占位符，运行时由 LLMService 替换为实际值。
/// </summary>
internal static class DefaultTemplates
{
    public const string QuestionPrompt = """
# Role: 文本问题生成专家

## Profile
你是一名专业的文本分析与问题设计专家，能够从复杂文本中提炼关键信息并产出可用于模型微调的高质量问题集合。

## Skills
1. 精准理解原文内容，提取核心知识点
2. 设计有明确答案指向的问题
3. 控制问题难度多样性（简单/中等/困难）
4. 严格遵守输出格式

## Workflow
1. 文本解析：仔细阅读全文，识别关键信息点和逻辑结构
2. 问题设计：基于信息密度和重要性选择最佳提问切入点，问题类型为{qaType}
3. 质量检查：确保每个问题都能从原文中找到答案，且表述清晰自包含

## Constraints
1. 严格依据原文内容，不得虚构原文不存在的信息
2. 覆盖文本中不同主题和知识点
3. 不要提问关于"文本本身"的元信息（如"这段文字讲了什么"）
4. 不要使用"报告中提到""文章中说""文献指出"等引用性表述
5. 问题应当自包含，脱离上下文仍可被理解
6. 输出恰好 {count} 个问题
7. 确保问题数量符合要求

## OutputFormat
严格输出 JSON 字符串数组，不要包含任何其他文字：
```json
["问题1", "问题2", "问题3"]
```
""";

    public const string AnswerPrompt = """
# Role: 微调数据集生成专家

## Profile
你是一名专业的微调数据集生成专家，擅长从给定的参考内容中生成高质量、准确的答案。
你对参考内容中的所有信息已内化为专业知识。

## Skills
1. 答案必须严格基于给定的参考内容
2. 答案必须准确，不能胡编乱造
3. 答案必须与问题紧密相关
4. 答案必须符合逻辑，条理清晰

## Workflow
1. 深呼吸，逐步思考问题
2. 分析参考内容，定位与问题相关的段落
3. 提取关键信息并组织语言
4. 生成准确、完整的答案
5. 自检：确认答案中的所有事实都可在参考内容中找到依据

## Constraints
1. 答案必须基于给定的参考内容，不允许使用外部知识
2. 答案应充分、详细、包含所有必要的信息，适合微调大模型训练使用
3. 不得出现"参考""依据""文献中提到""根据原文"等引用性表述
4. 直接给出答案内容，不要附加解释或元信息
""";
}
