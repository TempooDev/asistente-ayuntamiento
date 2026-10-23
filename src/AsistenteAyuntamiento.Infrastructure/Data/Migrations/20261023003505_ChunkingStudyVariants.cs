using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AsistenteAyuntamiento.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class ChunkingStudyVariants : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "RecursiveStatus",
                schema: "ingestion",
                table: "DocumentJobStates",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SemanticStatus",
                schema: "ingestion",
                table: "DocumentJobStates",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ChunkingStrategy",
                schema: "ingestion",
                table: "DocumentChunks",
                type: "character varying(32)",
                maxLength: 32,
                nullable: false,
                defaultValue: "FixedOverlap");

            migrationBuilder.CreateIndex(
                name: "IX_DocumentChunks_DocumentId_ChunkingStrategy",
                schema: "ingestion",
                table: "DocumentChunks",
                columns: new[] { "DocumentId", "ChunkingStrategy" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // The previous schema only knows the baseline and hierarchical pipelines: without the ChunkingStrategy
            // column the study chunks would pass for baseline ones, so the data of the study variants goes first.
            migrationBuilder.Sql("""
                DELETE FROM ingestion."DocumentChunks" WHERE "ChunkingStrategy" <> 'FixedOverlap';
                DELETE FROM ingestion."IngestionMetrics" WHERE "Pipeline" IN (3, 4);
                DELETE FROM ingestion."DocumentJobStates"
                WHERE "BaselineStatus" IS NULL AND "HierarchicalStatus" IS NULL
                  AND ("RecursiveStatus" IS NOT NULL OR "SemanticStatus" IS NOT NULL);
                UPDATE ingestion."DocumentJobStates" SET "ErrorMessage" = NULL
                WHERE "ErrorMessage" LIKE '[Recursive]%' OR "ErrorMessage" LIKE '[Semantic]%';
                """);

            migrationBuilder.DropIndex(
                name: "IX_DocumentChunks_DocumentId_ChunkingStrategy",
                schema: "ingestion",
                table: "DocumentChunks");

            migrationBuilder.DropColumn(
                name: "RecursiveStatus",
                schema: "ingestion",
                table: "DocumentJobStates");

            migrationBuilder.DropColumn(
                name: "SemanticStatus",
                schema: "ingestion",
                table: "DocumentJobStates");

            migrationBuilder.DropColumn(
                name: "ChunkingStrategy",
                schema: "ingestion",
                table: "DocumentChunks");
        }
    }
}
