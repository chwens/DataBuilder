using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DataBuilder.Core.Migrations
{
    /// <inheritdoc />
    public partial class AddQAPairTopic : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Topic",
                table: "QAPairs",
                type: "varchar(100)",
                maxLength: 100,
                nullable: true)
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AddColumn<string>(
                name: "TopicRaw",
                table: "QAPairs",
                type: "varchar(500)",
                maxLength: 500,
                nullable: true)
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateIndex(
                name: "IX_QAPairs_Topic",
                table: "QAPairs",
                column: "Topic");

            // ===== 5 个统计存储过程 =====
            // 设计：所有 SP 都接受 p_project_id INT 和可选的 p_topic VARCHAR(100)
            // JOIN 链：QAPairs -> Chunks -> Documents
            // 输出列别名使用反引号包裹（MySQL 关键字兼容）

            migrationBuilder.Sql(@"
CREATE PROCEDURE `sp_stat_qa_type`(IN p_project_id INT, IN p_topic VARCHAR(100))
BEGIN
    SELECT q.`Type` AS `Name`, COUNT(*) AS `Value`
    FROM QAPairs q
    INNER JOIN Chunks c ON q.ChunkId = c.Id
    INNER JOIN Documents d ON c.DocumentId = d.Id
    WHERE d.ProjectId = p_project_id
      AND (p_topic IS NULL OR q.Topic = p_topic)
    GROUP BY q.`Type`
    ORDER BY `Value` DESC;
END;
");

            migrationBuilder.Sql(@"
CREATE PROCEDURE `sp_stat_qa_quality`(IN p_project_id INT, IN p_topic VARCHAR(100))
BEGIN
    SELECT q.QualityScore AS `Score`, COUNT(*) AS `Count`
    FROM QAPairs q
    INNER JOIN Chunks c ON q.ChunkId = c.Id
    INNER JOIN Documents d ON c.DocumentId = d.Id
    WHERE d.ProjectId = p_project_id
      AND (p_topic IS NULL OR q.Topic = p_topic)
    GROUP BY q.QualityScore
    ORDER BY q.QualityScore ASC;
END;
");

            migrationBuilder.Sql(@"
CREATE PROCEDURE `sp_stat_qa_topic`(IN p_project_id INT, IN p_topic VARCHAR(100), IN p_top_n INT)
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
    ORDER BY `Value` DESC
    LIMIT p_top_n;
END;
");

            migrationBuilder.Sql(@"
CREATE PROCEDURE `sp_stat_qa_doc_top`(IN p_project_id INT, IN p_topic VARCHAR(100), IN p_top_n INT)
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
    ORDER BY `Value` DESC
    LIMIT p_top_n;
END;
");

            migrationBuilder.Sql(@"
CREATE PROCEDURE `sp_stat_qa_answer_status`(IN p_project_id INT, IN p_topic VARCHAR(100))
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
    ORDER BY `Value` DESC;
END;
");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // 清理 5 个 SP
            migrationBuilder.Sql("DROP PROCEDURE IF EXISTS `sp_stat_qa_type`;");
            migrationBuilder.Sql("DROP PROCEDURE IF EXISTS `sp_stat_qa_quality`;");
            migrationBuilder.Sql("DROP PROCEDURE IF EXISTS `sp_stat_qa_topic`;");
            migrationBuilder.Sql("DROP PROCEDURE IF EXISTS `sp_stat_qa_doc_top`;");
            migrationBuilder.Sql("DROP PROCEDURE IF EXISTS `sp_stat_qa_answer_status`;");

            migrationBuilder.DropIndex(
                name: "IX_QAPairs_Topic",
                table: "QAPairs");

            migrationBuilder.DropColumn(
                name: "Topic",
                table: "QAPairs");

            migrationBuilder.DropColumn(
                name: "TopicRaw",
                table: "QAPairs");
        }
    }
}
