using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AsistenteAyuntamiento.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class ArenaBattleTokens : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "LeftTokens",
                schema: "arena",
                table: "ArenaBattles",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "RightTokens",
                schema: "arena",
                table: "ArenaBattles",
                type: "integer",
                nullable: false,
                defaultValue: 0);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "LeftTokens",
                schema: "arena",
                table: "ArenaBattles");

            migrationBuilder.DropColumn(
                name: "RightTokens",
                schema: "arena",
                table: "ArenaBattles");
        }
    }
}
