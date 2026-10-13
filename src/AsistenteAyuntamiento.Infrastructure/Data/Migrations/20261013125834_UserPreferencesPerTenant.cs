using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AsistenteAyuntamiento.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class UserPreferencesPerTenant : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_UserPreferences_Auth0UserId",
                schema: "identity",
                table: "UserPreferences");

            migrationBuilder.CreateIndex(
                name: "IX_UserPreferences_Auth0UserId_TenantId",
                schema: "identity",
                table: "UserPreferences",
                columns: new[] { "Auth0UserId", "TenantId" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_UserPreferences_Auth0UserId_TenantId",
                schema: "identity",
                table: "UserPreferences");

            migrationBuilder.CreateIndex(
                name: "IX_UserPreferences_Auth0UserId",
                schema: "identity",
                table: "UserPreferences",
                column: "Auth0UserId",
                unique: true);
        }
    }
}
