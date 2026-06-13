using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DataBuilder.Core.Migrations
{
    /// <inheritdoc />
    public partial class AddQAPairTopicChunkIndex : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // 覆盖 5 个统计 SP 的 JOIN 路径：QAPairs JOIN Chunks JOIN Documents WHERE ProjectId + Topic
            // 单列 IX_QAPairs_Topic 无法利用 Chunks.ProjectId 过滤，故新增 (ChunkId, Topic) 复合索引
            migrationBuilder.Sql("CREATE INDEX IX_QAPairs_ChunkId_Topic ON QAPairs (ChunkId, Topic);");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP INDEX IX_QAPairs_ChunkId_Topic ON QAPairs;");
        }
    }
}