using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AsistenteAyuntamiento.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    /// <remarks>The row version maps to PostgreSQL's system column <c>xmin</c>, which always exists: no column is added.</remarks>
    public partial class DocumentJobStatePerPipeline : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "BaselineStatus",
                schema: "public",
                table: "DocumentJobStates",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "HierarchicalStatus",
                schema: "public",
                table: "DocumentJobStates",
                type: "text",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "BaselineStatus",
                schema: "public",
                table: "DocumentJobStates");

            migrationBuilder.DropColumn(
                name: "HierarchicalStatus",
                schema: "public",
                table: "DocumentJobStates");
        }
    }
}
