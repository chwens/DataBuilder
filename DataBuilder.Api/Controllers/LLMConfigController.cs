using DataBuilder.Api.Models;
using DataBuilder.Core;
using DataBuilder.Core.Entities;
using DataBuilder.Core.Interfaces;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace DataBuilder.Api.Controllers;

public class LLMConfigController : Controller
{
    private readonly AppDbContext _db;
    private readonly IEncryptionService _encryption;
    private readonly ILogger<LLMConfigController> _logger;

    public LLMConfigController(AppDbContext db, IEncryptionService encryption, ILogger<LLMConfigController> logger)
    {
        _db = db;
        _encryption = encryption;
        _logger = logger;
    }

    // GET: /LLMConfig
    public async Task<IActionResult> Index()
    {
        var configs = await _db.LLMConfigs
            .Include(c => c.Projects)
            .OrderByDescending(c => c.CreatedAt)
            .ToListAsync();

        var model = new LLMConfigListViewModel
        {
            Configs = configs.Select(c => new LLMConfigItemViewModel
            {
                Id = c.Id,
                Provider = c.Provider,
                ModelName = c.ModelName,
                ModelLabel = c.ModelLabel,
                ModelId = c.ModelId,
                ProjectCount = c.Projects.Count,
                CreatedAt = c.CreatedAt
            }).ToList()
        };

        return View(model);
    }

    // GET: /LLMConfig/Create
    public IActionResult Create()
    {
        return View(new LLMConfigEditViewModel());
    }

    // POST: /LLMConfig/Create
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(LLMConfigEditViewModel model)
    {
        if (!ModelState.IsValid)
        {
            return View(model);
        }

        if (string.IsNullOrWhiteSpace(model.ApiKey))
        {
            ModelState.AddModelError("ApiKey", "API Key 不能为空");
            return View(model);
        }

        var config = new LLMConfig
        {
            Provider = model.Provider,
            ApiUrl = model.ApiUrl,
            ApiKeyEncrypted = _encryption.Encrypt(model.ApiKey),
            ModelId = model.ModelId,
            ModelName = model.ModelName,
            ModelLabel = model.ModelLabel,
            Temperature = model.Temperature,
            MaxTokens = model.MaxTokens,
            TopP = model.TopP,
            CreatedAt = DateTime.UtcNow
        };

        _db.LLMConfigs.Add(config);
        await _db.SaveChangesAsync();

        _logger.LogInformation("模型配置已创建: {ModelName} (Id={ConfigId})", config.ModelName, config.Id);

        TempData["SuccessMessage"] = "模型配置已创建。";
        return RedirectToAction("Index");
    }

    // GET: /LLMConfig/Edit/{id}
    public async Task<IActionResult> Edit(int id)
    {
        var config = await _db.LLMConfigs.FirstOrDefaultAsync(c => c.Id == id);
        if (config == null)
        {
            return NotFound();
        }

        var model = new LLMConfigEditViewModel
        {
            Id = config.Id,
            Provider = config.Provider,
            ApiUrl = config.ApiUrl,
            ApiKey = null, // 编辑时不显示已加密的 key
            ModelId = config.ModelId,
            ModelName = config.ModelName,
            ModelLabel = config.ModelLabel,
            Temperature = config.Temperature,
            MaxTokens = config.MaxTokens,
            TopP = config.TopP
        };

        return View(model);
    }

    // POST: /LLMConfig/Edit/{id}
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Edit(int id, LLMConfigEditViewModel model)
    {
        if (!ModelState.IsValid)
        {
            return View(model);
        }

        var config = await _db.LLMConfigs.FirstOrDefaultAsync(c => c.Id == id);
        if (config == null)
        {
            return NotFound();
        }

        config.Provider = model.Provider;
        config.ApiUrl = model.ApiUrl;
        config.ModelId = model.ModelId;
        config.ModelName = model.ModelName;
        config.ModelLabel = model.ModelLabel;
        config.Temperature = model.Temperature;
        config.MaxTokens = model.MaxTokens;
        config.TopP = model.TopP;

        if (!string.IsNullOrWhiteSpace(model.ApiKey))
        {
            config.ApiKeyEncrypted = _encryption.Encrypt(model.ApiKey);
        }

        await _db.SaveChangesAsync();

        _logger.LogInformation("模型配置已更新: {ModelName} (Id={ConfigId})", config.ModelName, config.Id);

        TempData["SuccessMessage"] = "模型配置已更新。";
        return RedirectToAction("Index");
    }

    // POST: /LLMConfig/Delete/{id}
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Delete(int id)
    {
        var config = await _db.LLMConfigs.FirstOrDefaultAsync(c => c.Id == id);
        if (config == null)
        {
            return NotFound();
        }

        // 安全检查：查询是否有项目正在使用此配置生成 QA
        var hasActiveGeneration = await _db.Projects
            .Where(p => p.LLMConfigId == id)
            .SelectMany(p => p.Documents)
            .AnyAsync(d => d.Status == DocumentStatus.Generating);

        if (hasActiveGeneration)
        {
            TempData["ErrorMessage"] = "还有项目正在使用该模型生成 QA，无法删除。请等待生成完成后再试。";
            return RedirectToAction("Index");
        }

        config.IsDeleted = true;
        await _db.SaveChangesAsync();

        _logger.LogInformation("模型配置已删除: Id={ConfigId}", id);

        TempData["SuccessMessage"] = "模型配置已删除。";
        return RedirectToAction("Index");
    }
}
