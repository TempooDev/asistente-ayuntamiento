using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AsistenteAyuntamiento.ApiService.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class InitialUserProfiles : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "identity");

            migrationBuilder.CreateTable(
                name: "UserProfiles",
                schema: "identity",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Auth0UserId = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    TenantId = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    FullName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    Department = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    Position = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    PhoneNumber = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UserProfiles", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_UserProfiles_Auth0UserId",
                schema: "identity",
                table: "UserProfiles",
                column: "Auth0UserId",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "UserProfiles",
                schema: "identity");
        }
    }
}
