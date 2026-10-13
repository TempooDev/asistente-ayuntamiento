using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AsistenteAyuntamiento.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class CallLogStepsVoteVersionJobStateSchema : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameTable(
                name: "DocumentJobStates",
                schema: "public",
                newName: "DocumentJobStates",
                newSchema: "ingestion");

            // ArenaBattles' row version is PostgreSQL's system column xmin: nothing to add.

            // Rows written so far are answers (other steps were never stored).
            migrationBuilder.AddColumn<string>(
                name: "Operation",
                schema: "chat",
                table: "AiCallLogs",
                type: "character varying(50)",
                maxLength: 50,
                nullable: false,
                defaultValue: "answer");

            migrationBuilder.AddColumn<string>(
                name: "Pipeline",
                schema: "chat",
                table: "AiCallLogs",
                type: "character varying(50)",
                maxLength: 50,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Operation",
                schema: "chat",
                table: "AiCallLogs");

            migrationBuilder.DropColumn(
                name: "Pipeline",
                schema: "chat",
                table: "AiCallLogs");

            migrationBuilder.EnsureSchema(
                name: "public");

            migrationBuilder.RenameTable(
                name: "DocumentJobStates",
                schema: "ingestion",
                newName: "DocumentJobStates",
                newSchema: "public");
        }
    }
}
