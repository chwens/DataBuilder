using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DataBuilder.Core.Migrations
{
    /// <inheritdoc />
    public partial class AddProjectChunkConfig : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "CharsPerQuestion",
                table: "Projects",
                type: "int",
                nullable: false,
                defaultValue: 240);

            migrationBuilder.AddColumn<int>(
                name: "ChunkMaxLength",
                table: "Projects",
                type: "int",
                nullable: false,
                defaultValue: 4000);

            migrationBuilder.AddColumn<int>(
                name: "ChunkMinLength",
                table: "Projects",
                type: "int",
                nullable: false,
                defaultValue: 2500);

            migrationBuilder.AddColumn<int>(
                name: "QuestionMarkRemovalRate",
                table: "Projects",
                type: "int",
                nullable: false,
                defaultValue: 60);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "CharsPerQuestion",
                table: "Projects");

            migrationBuilder.DropColumn(
                name: "ChunkMaxLength",
                table: "Projects");

            migrationBuilder.DropColumn(
                name: "ChunkMinLength",
                table: "Projects");

            migrationBuilder.DropColumn(
                name: "QuestionMarkRemovalRate",
                table: "Projects");
        }
    }
}
