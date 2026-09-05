using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AsistenteAyuntamiento.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddHierarchicalPipelineAndRenameBaseline : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropPrimaryKey(
                name: "PK_DocumentChunks",
                schema: "identity",
                table: "DocumentChunks");

            migrationBuilder.RenameTable(
                name: "DocumentChunks",
                schema: "identity",
                newName: "chunks_baseline_v1",
                newSchema: "ingestion");

            migrationBuilder.AddPrimaryKey(
                name: "PK_chunks_baseline_v1",
                schema: "ingestion",
                table: "chunks_baseline_v1",
                column: "Id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropPrimaryKey(
                name: "PK_chunks_baseline_v1",
                schema: "ingestion",
                table: "chunks_baseline_v1");

            migrationBuilder.RenameTable(
                name: "chunks_baseline_v1",
                schema: "ingestion",
                newName: "DocumentChunks",
                newSchema: "identity");

            migrationBuilder.AddPrimaryKey(
                name: "PK_DocumentChunks",
                schema: "identity",
                table: "DocumentChunks",
                column: "Id");
        }
    }
}
