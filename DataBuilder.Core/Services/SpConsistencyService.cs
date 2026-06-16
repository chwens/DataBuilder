using DataBuilder.Core;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using MySqlConnector;

namespace DataBuilder.Core.Services;

/// <summary>
/// 应用启动时一次性重建 5 个 QA 统计 SP，确保 5 SP 总是处于一致状态。
///
/// 背景：
///   - 旧 migration 用 5 条独立 migrationBuilder.Sql 调用，DROP/CREATE 不在同一个事务中。
///   - EF Core 的 migrationBuilder.Sql 默认隐式提交，任意一条 CREATE 失败都会导致
///     5 SP 全部残留缺失，/Stats/* 端点全部 500。
///
/// 修复方案 (方案 D)：
///   - 将 5 SP 的重建从 migration 搬到应用启动时执行。
///   - 在单次 DbConnection 上用 MySqlBatch 一次性批量执行所有 DROP + CREATE。
///   - MySqlBatch 在 MySqlConnector 2.4+ 引入，单连接批量执行多条语句。
///   - 失败仅 LogError，不抛（不让应用启动失败）。
/// </summary>
public class SpConsistencyService : IHostedService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<SpConsistencyService> _logger;

    public SpConsistencyService(
        IServiceProvider serviceProvider,
        ILogger<SpConsistencyService> logger)
    {
        _serviceProvider = serviceProvider;
        _logger = logger;
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        try
        {
            RebuildAllProceduresAsync(cancellationToken).GetAwaiter().GetResult();
        }
        catch (Exception ex)
        {
            // 启动期不抛，避免阻塞应用启动；运行时 /Stats/* 端点会再次暴露问题。
            _logger.LogError(ex, "[SpConsistency] 重建 5 SP 失败，/Stats/* 端点可能不可用。");
        }

        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    private async Task RebuildAllProceduresAsync(CancellationToken cancellationToken)
    {
        using var scope = _serviceProvider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var connection = db.Database.GetDbConnection();

        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            var mysqlConnection = (MySqlConnection)connection;

            // 使用 MySqlBatch 在单连接上批量执行 10 条 DDL。
            // MySqlConnector 2.4+ 引入 MySqlBatch；本项目 Pomelo 9.0.0 → MySqlConnector 2.5.0。
            // MySqlBatch 内部在单连接上顺序执行，DDL 不支持事务，但单连接保证
            // "前一条失败则后续全部不执行"，避免旧版 5 独立隐式提交导致部分成功部分失败。
            var statements = BuildRebuildStatements();
            var batch = mysqlConnection.CreateBatch();
            foreach (var sql in statements)
            {
                var cmd = batch.CreateBatchCommand();
                cmd.CommandText = sql;
                batch.BatchCommands.Add(cmd);
            }

            await batch.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);

            _logger.LogInformation(
                "[SpConsistency] 5 SP 重建成功: sp_stat_qa_type, sp_stat_qa_quality, sp_stat_qa_topic, sp_stat_qa_doc_top, sp_stat_qa_answer_status");
        }
        finally
        {
            await connection.CloseAsync().ConfigureAwait(false);
        }
    }

    /// <summary>
    /// 拆分 DROP + CREATE 为独立语句（避免 MySqlScript 在 2.5.0 中已移除）。
    /// 顺序：DROP 全部 → CREATE 全部。中间任一 DROP 失败则 CREATE 全部跳过。
    /// 注意：仅 sp_stat_qa_topic 需要 Topic NOT NULL 硬过滤（它 GROUP BY Topic，NULL 无法分组）；
    ///       其余 4 个 SP（type/quality/doc_top/answer_status）不按 Topic 分组，不应过滤无 Topic 的 QA 对，
    ///       确保图表在 Topic 被异步标记之前就能反映数据。
    /// </summary>
    private static IReadOnlyList<string> BuildRebuildStatements() => new[]
    {
        "DROP PROCEDURE IF EXISTS `sp_stat_qa_type`;",
        "DROP PROCEDURE IF EXISTS `sp_stat_qa_quality`;",
        "DROP PROCEDURE IF EXISTS `sp_stat_qa_topic`;",
        "DROP PROCEDURE IF EXISTS `sp_stat_qa_doc_top`;",
        "DROP PROCEDURE IF EXISTS `sp_stat_qa_answer_status`;",
        @"CREATE PROCEDURE `sp_stat_qa_type`(IN p_project_id INT, IN p_topic VARCHAR(100))
BEGIN
    SELECT q.`Type` AS `Name`, COUNT(*) AS `Value`
    FROM QAPairs q
    INNER JOIN Chunks c ON q.ChunkId = c.Id
    INNER JOIN Documents d ON c.DocumentId = d.Id
    WHERE d.ProjectId = p_project_id
      AND (p_topic IS NULL OR q.Topic = p_topic)
    GROUP BY q.`Type`
    ORDER BY `Value` DESC, q.`Type` ASC;
END;",
        @"CREATE PROCEDURE `sp_stat_qa_quality`(IN p_project_id INT, IN p_topic VARCHAR(100))
BEGIN
    SELECT q.QualityScore AS `Score`, COUNT(*) AS `Count`
    FROM QAPairs q
    INNER JOIN Chunks c ON q.ChunkId = c.Id
    INNER JOIN Documents d ON c.DocumentId = d.Id
    WHERE d.ProjectId = p_project_id
      AND (p_topic IS NULL OR q.Topic = p_topic)
    GROUP BY q.QualityScore
    ORDER BY `Count` DESC, q.QualityScore ASC;
END;",
        @"CREATE PROCEDURE `sp_stat_qa_topic`(IN p_project_id INT, IN p_topic VARCHAR(100), IN p_top_n INT)
BEGIN
    IF p_top_n IS NULL OR p_top_n <= 0 THEN
        SET p_top_n = 15;
    END IF;
    SELECT q.Topic AS `Name`, COUNT(*) AS `Value`
    FROM QAPairs q
    INNER JOIN Chunks c ON q.ChunkId = c.Id
    INNER JOIN Documents d ON c.DocumentId = d.Id
    WHERE d.ProjectId = p_project_id
      AND q.Topic IS NOT NULL AND q.Topic <> ''
      AND (p_topic IS NULL OR q.Topic = p_topic)
    GROUP BY q.Topic
    ORDER BY `Value` DESC, q.Topic ASC
    LIMIT p_top_n;
END;",
        @"CREATE PROCEDURE `sp_stat_qa_doc_top`(IN p_project_id INT, IN p_topic VARCHAR(100), IN p_top_n INT)
BEGIN
    IF p_top_n IS NULL OR p_top_n <= 0 THEN
        SET p_top_n = 10;
    END IF;
    SELECT d.FileName AS `Name`, COUNT(*) AS `Value`
    FROM QAPairs q
    INNER JOIN Chunks c ON q.ChunkId = c.Id
    INNER JOIN Documents d ON c.DocumentId = d.Id
    WHERE d.ProjectId = p_project_id
      AND (p_topic IS NULL OR q.Topic = p_topic)
    GROUP BY d.FileName
    ORDER BY `Value` DESC, d.FileName ASC
    LIMIT p_top_n;
END;",
        @"CREATE PROCEDURE `sp_stat_qa_answer_status`(IN p_project_id INT, IN p_topic VARCHAR(100))
BEGIN
    SELECT
        CASE WHEN q.Answered = 1 THEN 'Answered' ELSE 'Pending' END AS `Name`,
        COUNT(*) AS `Value`
    FROM QAPairs q
    INNER JOIN Chunks c ON q.ChunkId = c.Id
    INNER JOIN Documents d ON c.DocumentId = d.Id
    WHERE d.ProjectId = p_project_id
      AND (p_topic IS NULL OR q.Topic = p_topic)
    GROUP BY q.Answered
    ORDER BY `Value` DESC, q.Answered ASC;
END;",
    };
}
