using Microsoft.EntityFrameworkCore.Migrations;
using Pgvector;

#nullable disable

namespace AsistenteAyuntamiento.Infrastructure.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class FixVectorDimensions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP INDEX IF EXISTS ingestion.\"IX_DocumentChunks_Embedding\";");

            migrationBuilder.AlterColumn<Vector>(
                name: "Embedding",
                schema: "ingestion",
                table: "DocumentChunks",
                type: "vector(4096)",
                nullable: true,
                oldClrType: typeof(Vector),
                oldType: "vector(768)",
                oldNullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<Vector>(
                name: "Embedding",
                schema: "ingestion",
                table: "DocumentChunks",
                type: "vector(768)",
                nullable: true,
                oldClrType: typeof(Vector),
                oldType: "vector(4096)",
                oldNullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_DocumentChunks_Embedding",
                schema: "ingestion",
                table: "DocumentChunks",
                column: "Embedding")
                .Annotation("Npgsql:IndexMethod", "hnsw")
                .Annotation("Npgsql:IndexOperators", new[] { "vector_cosine_ops" });
        }
    }
}
