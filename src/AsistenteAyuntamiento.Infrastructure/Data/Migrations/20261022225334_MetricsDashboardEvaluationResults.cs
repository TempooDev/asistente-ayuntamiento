using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AsistenteAyuntamiento.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class MetricsDashboardEvaluationResults : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "evaluation");

            migrationBuilder.AddColumn<string>(
                name: "Provider",
                schema: "chat",
                table: "AiCallLogs",
                type: "character varying(50)",
                maxLength: 50,
                nullable: true);

            migrationBuilder.AddColumn<double>(
                name: "TimeToFirstTokenMs",
                schema: "chat",
                table: "AiCallLogs",
                type: "double precision",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "EvaluationRuns",
                schema: "evaluation",
                columns: table => new
                {
                    RunId = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    StartedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    FinishedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    GitCommit = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    Seed = table.Column<int>(type: "integer", nullable: false),
                    ChatModel = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    EmbeddingModel = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    JudgeModel = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    QueryInstruction = table.Column<string>(type: "text", nullable: true),
                    QuestionCount = table.Column<int>(type: "integer", nullable: false),
                    DatasetHash = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    CorpusHash = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    RagasVersion = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    ImportedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EvaluationRuns", x => x.RunId);
                });

            migrationBuilder.CreateTable(
                name: "EvaluationPipelineResults",
                schema: "evaluation",
                columns: table => new
                {
                    RunId = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    Pipeline = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    ContextPrecision = table.Column<double>(type: "double precision", nullable: true),
                    ContextPrecisionStd = table.Column<double>(type: "double precision", nullable: true),
                    ContextRecall = table.Column<double>(type: "double precision", nullable: true),
                    ContextRecallStd = table.Column<double>(type: "double precision", nullable: true),
                    Faithfulness = table.Column<double>(type: "double precision", nullable: true),
                    FaithfulnessStd = table.Column<double>(type: "double precision", nullable: true),
                    AnswerRelevancy = table.Column<double>(type: "double precision", nullable: true),
                    AnswerRelevancyStd = table.Column<double>(type: "double precision", nullable: true),
                    HallucinationRate = table.Column<double>(type: "double precision", nullable: true),
                    HitAt1 = table.Column<double>(type: "double precision", nullable: true),
                    HitAt3 = table.Column<double>(type: "double precision", nullable: true),
                    HitAt5 = table.Column<double>(type: "double precision", nullable: true),
                    Mrr = table.Column<double>(type: "double precision", nullable: true),
                    NoAnswerDetectedRate = table.Column<double>(type: "double precision", nullable: true),
                    Failed = table.Column<int>(type: "integer", nullable: false),
                    Missing = table.Column<int>(type: "integer", nullable: false),
                    LatencyP50Ms = table.Column<double>(type: "double precision", nullable: true),
                    LatencyP95Ms = table.Column<double>(type: "double precision", nullable: true),
                    TtftP50Ms = table.Column<double>(type: "double precision", nullable: true),
                    TtftP95Ms = table.Column<double>(type: "double precision", nullable: true),
                    MeanTokens = table.Column<double>(type: "double precision", nullable: true),
                    IndexingP50Ms = table.Column<double>(type: "double precision", nullable: true),
                    IndexingP95Ms = table.Column<double>(type: "double precision", nullable: true),
                    ChunksPerDocument = table.Column<double>(type: "double precision", nullable: true),
                    Points = table.Column<long>(type: "bigint", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EvaluationPipelineResults", x => new { x.RunId, x.Pipeline });
                    table.ForeignKey(
                        name: "FK_EvaluationPipelineResults_EvaluationRuns_RunId",
                        column: x => x.RunId,
                        principalSchema: "evaluation",
                        principalTable: "EvaluationRuns",
                        principalColumn: "RunId",
                        onDelete: ReferentialAction.Cascade);
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "EvaluationPipelineResults",
                schema: "evaluation");

            migrationBuilder.DropTable(
                name: "EvaluationRuns",
                schema: "evaluation");

            migrationBuilder.DropColumn(
                name: "Provider",
                schema: "chat",
                table: "AiCallLogs");

            migrationBuilder.DropColumn(
                name: "TimeToFirstTokenMs",
                schema: "chat",
                table: "AiCallLogs");
        }
    }
}
