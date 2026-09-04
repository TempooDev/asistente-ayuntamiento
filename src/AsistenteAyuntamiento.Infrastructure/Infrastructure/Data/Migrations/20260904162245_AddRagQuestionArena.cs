using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;
using NpgsqlTypes;
using Pgvector;

#nullable disable

namespace AsistenteAyuntamiento.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddRagQuestionArena : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "arena");

            migrationBuilder.EnsureSchema(
                name: "ingestion");

            migrationBuilder.CreateTable(
                name: "ArenaBattles",
                schema: "arena",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    SessionId = table.Column<Guid>(type: "uuid", nullable: false),
                    UserQuery = table.Column<string>(type: "text", nullable: false),
                    LeftSystem = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    RightSystem = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    LeftResponse = table.Column<string>(type: "text", nullable: false),
                    RightResponse = table.Column<string>(type: "text", nullable: false),
                    LeftLatencyMs = table.Column<int>(type: "integer", nullable: false),
                    RightLatencyMs = table.Column<int>(type: "integer", nullable: false),
                    Winner = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    ClarityReason = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    PrecisionReason = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    OptionalComment = table.Column<string>(type: "text", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ArenaBattles", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "IngestionMetrics",
                schema: "ingestion",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Pipeline = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    Bulletin = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    DocumentId = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    TotalTokensEmbedded = table.Column<int>(type: "integer", nullable: false),
                    TotalLlmCalls = table.Column<int>(type: "integer", nullable: false),
                    TotalLlmTokens = table.Column<int>(type: "integer", nullable: false),
                    ProcessingDurationMs = table.Column<long>(type: "bigint", nullable: false),
                    ChunksGenerated = table.Column<int>(type: "integer", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_IngestionMetrics", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ParentDocuments",
                schema: "ingestion",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Bulletin = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    DocumentId = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    NormativeRank = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    IssuingBody = table.Column<string>(type: "text", nullable: true),
                    NormTitle = table.Column<string>(type: "text", nullable: false),
                    NormSection = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                    Municipality = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    FullText = table.Column<string>(type: "text", nullable: false),
                    PublicationDate = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    Metadata = table.Column<string>(type: "jsonb", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ParentDocuments", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ChildFragments",
                schema: "ingestion",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    ParentId = table.Column<long>(type: "bigint", nullable: false),
                    Bulletin = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    Municipality = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    SubSection = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    ChunkText = table.Column<string>(type: "text", nullable: false),
                    TsvContent = table.Column<NpgsqlTsVector>(type: "tsvector", nullable: true),
                    Embedding = table.Column<Vector>(type: "vector(1536)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ChildFragments", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ChildFragments_ParentDocuments_ParentId",
                        column: x => x.ParentId,
                        principalSchema: "ingestion",
                        principalTable: "ParentDocuments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ChildFragments_Embedding",
                schema: "ingestion",
                table: "ChildFragments",
                column: "Embedding")
                .Annotation("Npgsql:IndexMethod", "hnsw")
                .Annotation("Npgsql:IndexOperators", new[] { "vector_cosine_ops" });

            migrationBuilder.CreateIndex(
                name: "IX_ChildFragments_ParentId",
                schema: "ingestion",
                table: "ChildFragments",
                column: "ParentId");

            migrationBuilder.CreateIndex(
                name: "IX_ChildFragments_TsvContent",
                schema: "ingestion",
                table: "ChildFragments",
                column: "TsvContent")
                .Annotation("Npgsql:IndexMethod", "gin");

            using var stream = typeof(AddRagQuestionArena).Assembly.GetManifestResourceStream("AsistenteAyuntamiento.Infrastructure.Infrastructure.Data.Migrations.Scripts.Up_AddChildFragmentsTsvTrigger.sql")
                ?? throw new InvalidOperationException("No se pudo cargar el script SQL 'Up_AddChildFragmentsTsvTrigger.sql'. Verifica que esté marcado como EmbeddedResource y la ruta coincida.");

            using var reader = new System.IO.StreamReader(stream);
            migrationBuilder.Sql(reader.ReadToEnd());
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            using var stream = typeof(AddRagQuestionArena).Assembly.GetManifestResourceStream("AsistenteAyuntamiento.Infrastructure.Infrastructure.Data.Migrations.Scripts.Down_DropChildFragmentsTsvTrigger.sql")
                ?? throw new InvalidOperationException("No se pudo cargar el script SQL 'Down_DropChildFragmentsTsvTrigger.sql'. Verifica que esté marcado como EmbeddedResource y la ruta coincida.");

            using var reader = new System.IO.StreamReader(stream);
            migrationBuilder.Sql(reader.ReadToEnd());

            migrationBuilder.DropTable(
                name: "ArenaBattles",
                schema: "arena");

            migrationBuilder.DropTable(
                name: "ChildFragments",
                schema: "ingestion");

            migrationBuilder.DropTable(
                name: "IngestionMetrics",
                schema: "ingestion");

            migrationBuilder.DropTable(
                name: "ParentDocuments",
                schema: "ingestion");
        }
    }
}
