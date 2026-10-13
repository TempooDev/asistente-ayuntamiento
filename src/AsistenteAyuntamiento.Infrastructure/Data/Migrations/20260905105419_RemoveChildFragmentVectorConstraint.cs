using Microsoft.EntityFrameworkCore.Migrations;
using Pgvector;

#nullable disable

namespace AsistenteAyuntamiento.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class RemoveChildFragmentVectorConstraint : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<Vector>(
                name: "Embedding",
                schema: "ingestion",
                table: "ChildFragments",
                type: "vector",
                nullable: true,
                oldClrType: typeof(Vector),
                oldType: "vector(1536)",
                oldNullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<Vector>(
                name: "Embedding",
                schema: "ingestion",
                table: "ChildFragments",
                type: "vector(1536)",
                nullable: true,
                oldClrType: typeof(Vector),
                oldType: "vector",
                oldNullable: true);
        }
    }
}
