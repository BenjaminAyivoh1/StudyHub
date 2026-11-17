using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace StudyHub.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddPractice : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "IsPublic",
                table: "Quizzes",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "Topic",
                table: "Quizzes",
                type: "character varying(50)",
                maxLength: 50,
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "IsPractice",
                table: "GameSessions",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.CreateIndex(
                name: "IX_Quizzes_IsPublic_Topic",
                table: "Quizzes",
                columns: new[] { "IsPublic", "Topic" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Quizzes_IsPublic_Topic",
                table: "Quizzes");

            migrationBuilder.DropColumn(
                name: "IsPublic",
                table: "Quizzes");

            migrationBuilder.DropColumn(
                name: "Topic",
                table: "Quizzes");

            migrationBuilder.DropColumn(
                name: "IsPractice",
                table: "GameSessions");
        }
    }
}
