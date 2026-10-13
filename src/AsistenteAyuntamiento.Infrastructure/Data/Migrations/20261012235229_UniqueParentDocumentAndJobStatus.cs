using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AsistenteAyuntamiento.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class UniqueParentDocumentAndJobStatus : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Re-ingestion used to insert a new ParentDocument every time. Keep only the newest version of each
            // document (its ChildFragments are removed by the ON DELETE CASCADE foreign key); the vectors of the
            // removed versions are cleaned up the next time the document is ingested.
            migrationBuilder.Sql("""
                DELETE FROM ingestion."ParentDocuments" older
                USING ingestion."ParentDocuments" newer
                WHERE older."DocumentId" = newer."DocumentId"
                  AND older."Id" < newer."Id";
                """);

            // DocumentJobStates.Status becomes an enum stored as text with the same values: no schema change.

            migrationBuilder.CreateIndex(
                name: "IX_ParentDocuments_DocumentId",
                schema: "ingestion",
                table: "ParentDocuments",
                column: "DocumentId",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_ParentDocuments_DocumentId",
                schema: "ingestion",
                table: "ParentDocuments");
        }
    }
}
