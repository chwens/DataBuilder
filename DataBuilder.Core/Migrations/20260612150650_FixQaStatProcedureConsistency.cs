using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DataBuilder.Core.Migrations
{
    /// <summary>
    /// 修复 QA 统计 SP 的两个一致性问题：
    ///   Fix 4: WHERE 一致性 — 4 个 SP 缺 `q.Topic IS NOT NULL AND q.Topic <> ''` 过滤，
    ///          导致跨图表对账时把未打标的 QA 也计入。
    ///   Fix 5: ORDER BY 次级排序 — 5 SP 的 ORDER BY 都不带次级键，同 count 时不稳定。
    ///
    /// 注意：原实现尝试在 Up()/Down() 中各跑 5 条独立的 migrationBuilder.Sql 调用，
    ///       EF Core 的 migrationBuilder.Sql 默认每条独立隐式提交，无法保证 5 SP 原子性。
    ///       任意一条 CREATE 失败 → 5 SP 全部残留缺失，/Stats/* 端点 500。
    ///
    /// 现状：5 SP 的重建逻辑已迁出到 SpConsistencyService（IHostedService），
    ///       由应用启动时一次性执行。本 migration Up()/Down() 留作 schema 占位记录，
    ///       不再包含 DDL。
    /// </summary>
    public partial class FixQaStatProcedureConsistency : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // SP 重建逻辑由 SpConsistencyService 接管（应用启动时执行，保证 5 SP 一致性）。
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // SP 回滚 / DROP 逻辑由 SpConsistencyService 接管。
            // 若需要手动 DROP，启动后任意时刻可执行：DROP PROCEDURE IF EXISTS sp_stat_qa_*;
        }
    }
}
