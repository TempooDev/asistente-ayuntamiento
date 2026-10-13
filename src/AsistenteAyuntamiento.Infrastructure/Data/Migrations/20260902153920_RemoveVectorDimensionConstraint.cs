using Microsoft.EntityFrameworkCore.Migrations;
using Pgvector;

#nullable disable

namespace AsistenteAyuntamiento.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class RemoveVectorDimensionConstraint : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<Vector>(
                name: "Embedding",
                schema: "identity",
                table: "DocumentChunks",
                type: "vector",
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
                schema: "identity",
                table: "DocumentChunks",
                type: "vector(768)",
                nullable: true,
                oldClrType: typeof(Vector),
                oldType: "vector",
                oldNullable: true);
        }
    }
}
