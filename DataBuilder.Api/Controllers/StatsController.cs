using DataBuilder.Api.Models.Stats;
using DataBuilder.Core;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace DataBuilder.Api.Controllers;

/// <summary>
/// 数据集统计图表数据源（5 个端点）。
/// 全部走 MySQL 存储过程，pomelo SqlQueryRaw 严格按属性名匹配列名。
/// 所有 SP 输出列别名已在迁移 #19 中通过反引号 AS 锁定。
/// </summary>
[Route("Stats")]
public class StatsController : Controller
{
    private readonly AppDbContext _db;
    private readonly ILogger<StatsController> _logger;

    public StatsController(AppDbContext db, ILogger<StatsController> logger)
    {
        _db = db;
        _logger = logger;
    }

    /// <summary>
    /// 按 QAPair.Type 分组计数（饼图：Factoid/Reasoning/Summary）。
    /// </summary>
    [HttpGet("QAType")]
    public async Task<IActionResult> QAType(int projectId, string? topic = null)
    {
        if (projectId <= 0)
        {
            _logger.LogWarning("Stats/QAType: 无效的 projectId={ProjectId}", projectId);
            return BadRequest(new { error = "Invalid projectId", projectId });
        }

        var data = await _db.Database
            .SqlQueryRaw<CategoryValueDto>(
                "CALL sp_stat_qa_type({0}, {1})",
                projectId,
                (object?)topic ?? DBNull.Value)
            .ToListAsync();

        return new JsonResult(data);
    }

    /// <summary>
    /// 按 QualityScore 1-5 分组计数（柱图）。
    /// </summary>
    [HttpGet("QualityScore")]
    public async Task<IActionResult> Quality(int projectId, string? topic = null)
    {
        if (projectId <= 0)
        {
            _logger.LogWarning("Stats/QualityScore: 无效的 projectId={ProjectId}", projectId);
            return BadRequest(new { error = "Invalid projectId", projectId });
        }

        var data = await _db.Database
            .SqlQueryRaw<QualityScoreDto>(
                "CALL sp_stat_qa_quality({0}, {1})",
                projectId,
                (object?)topic ?? DBNull.Value)
            .ToListAsync();

        return new JsonResult(data);
    }

    /// <summary>
    /// 按 Topic 聚合计数 Top N（默认 15，横柱图 + 联动源）。
    /// 不接受 topic 入参（自己是过滤源）。
    /// </summary>
    [HttpGet("Topic")]
    public async Task<IActionResult> Topic(int projectId, int topN = 15)
    {
        if (projectId <= 0)
        {
            _logger.LogWarning("Stats/Topic: 无效的 projectId={ProjectId}", projectId);
            return BadRequest(new { error = "Invalid projectId", projectId });
        }

        if (topN <= 0) topN = 15;

        var data = await _db.Database
            .SqlQueryRaw<CategoryValueDto>(
                "CALL sp_stat_qa_topic({0}, {1}, {2})",
                projectId,
                DBNull.Value,
                topN)
            .ToListAsync();

        return new JsonResult(data);
    }

    /// <summary>
    /// 按 Document.FileName 分组计数 Top N（默认 10）。
    /// </summary>
    [HttpGet("DocumentTop")]
    public async Task<IActionResult> DocTop(int projectId, int topN = 10, string? topic = null)
    {
        if (projectId <= 0)
        {
            _logger.LogWarning("Stats/DocumentTop: 无效的 projectId={ProjectId}", projectId);
            return BadRequest(new { error = "Invalid projectId", projectId });
        }

        if (topN <= 0) topN = 10;

        var data = await _db.Database
            .SqlQueryRaw<CategoryValueDto>(
                "CALL sp_stat_qa_doc_top({0}, {1}, {2})",
                projectId,
                (object?)topic ?? DBNull.Value,
                topN)
            .ToListAsync();

        return new JsonResult(data);
    }

    /// <summary>
    /// 按 Answered 分组计数（环形图：Answered/Pending）。
    /// </summary>
    [HttpGet("AnswerStatus")]
    public async Task<IActionResult> AnswerStatus(int projectId, string? topic = null)
    {
        if (projectId <= 0)
        {
            _logger.LogWarning("Stats/AnswerStatus: 无效的 projectId={ProjectId}", projectId);
            return BadRequest(new { error = "Invalid projectId", projectId });
        }

        var data = await _db.Database
            .SqlQueryRaw<CategoryValueDto>(
                "CALL sp_stat_qa_answer_status({0}, {1})",
                projectId,
                (object?)topic ?? DBNull.Value)
            .ToListAsync();

        return new JsonResult(data);
    }
}
