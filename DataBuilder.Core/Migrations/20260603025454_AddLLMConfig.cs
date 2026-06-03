using System;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DataBuilder.Core.Migrations
{
    /// <inheritdoc />
    public partial class AddLLMConfig : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "LLMConfigId",
                table: "Projects",
                type: "int",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "LLMConfigs",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    Provider = table.Column<string>(type: "varchar(100)", maxLength: 100, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    ApiUrl = table.Column<string>(type: "varchar(500)", maxLength: 500, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    ApiKeyEncrypted = table.Column<string>(type: "longtext", nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    ModelId = table.Column<string>(type: "varchar(200)", maxLength: 200, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    ModelName = table.Column<string>(type: "varchar(200)", maxLength: 200, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    ModelLabel = table.Column<string>(type: "varchar(100)", maxLength: 100, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Temperature = table.Column<float>(type: "float", nullable: false),
                    MaxTokens = table.Column<int>(type: "int", nullable: false),
                    TopP = table.Column<float>(type: "float", nullable: false),
                    IsDeleted = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LLMConfigs", x => x.Id);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateIndex(
                name: "IX_Projects_LLMConfigId",
                table: "Projects",
                column: "LLMConfigId");

            migrationBuilder.AddForeignKey(
                name: "FK_Projects_LLMConfigs_LLMConfigId",
                table: "Projects",
                column: "LLMConfigId",
                principalTable: "LLMConfigs",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Projects_LLMConfigs_LLMConfigId",
                table: "Projects");

            migrationBuilder.DropTable(
                name: "LLMConfigs");

            migrationBuilder.DropIndex(
                name: "IX_Projects_LLMConfigId",
                table: "Projects");

            migrationBuilder.DropColumn(
                name: "LLMConfigId",
                table: "Projects");
        }
    }
}
