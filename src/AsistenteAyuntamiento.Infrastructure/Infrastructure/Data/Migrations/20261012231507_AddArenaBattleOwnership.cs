using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AsistenteAyuntamiento.Infrastructure.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddArenaBattleOwnership : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Battles created before ownership existed get TenantId "legacy" / UserId "unknown":
            // they stay in the (cross-tenant) analytics but nobody can vote on or read them.
            // The pgvector extension annotation drift (vector columns were removed earlier) is
            // intentionally not turned into DROP EXTENSION here.
            migrationBuilder.AddColumn<string>(
                name: "TenantId",
                schema: "arena",
                table: "ArenaBattles",
                type: "character varying(50)",
                maxLength: 50,
                nullable: false,
                defaultValue: "legacy");

            migrationBuilder.AddColumn<string>(
                name: "UserId",
                schema: "arena",
                table: "ArenaBattles",
                type: "character varying(100)",
                maxLength: 100,
                nullable: false,
                defaultValue: "unknown");

            migrationBuilder.CreateIndex(
                name: "IX_ArenaBattles_TenantId_UserId_SessionId",
                schema: "arena",
                table: "ArenaBattles",
                columns: new[] { "TenantId", "UserId", "SessionId" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_ArenaBattles_TenantId_UserId_SessionId",
                schema: "arena",
                table: "ArenaBattles");

            migrationBuilder.DropColumn(
                name: "TenantId",
                schema: "arena",
                table: "ArenaBattles");

            migrationBuilder.DropColumn(
                name: "UserId",
                schema: "arena",
                table: "ArenaBattles");
        }
    }
}
