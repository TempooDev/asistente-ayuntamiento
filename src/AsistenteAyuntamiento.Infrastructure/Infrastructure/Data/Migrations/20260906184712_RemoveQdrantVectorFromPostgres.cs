using Microsoft.EntityFrameworkCore.Migrations;
using Pgvector;

#nullable disable

namespace AsistenteAyuntamiento.Infrastructure.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class RemoveQdrantVectorFromPostgres : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Embedding",
                schema: "ingestion",
                table: "ChildFragments");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Vector>(
                name: "Embedding",
                schema: "ingestion",
                table: "ChildFragments",
                type: "vector(4096)",
                nullable: true);
        }
    }
}
