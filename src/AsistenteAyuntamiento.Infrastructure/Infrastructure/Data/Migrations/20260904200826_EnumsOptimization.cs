using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AsistenteAyuntamiento.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class EnumsOptimization : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
                ALTER TABLE ingestion.""ParentDocuments"" ALTER COLUMN ""Bulletin"" TYPE smallint USING CASE ""Bulletin"" WHEN 'BOE' THEN 1 WHEN 'BOJA' THEN 2 WHEN 'BOPMA' THEN 3 ELSE 1 END;
                ALTER TABLE ingestion.""IngestionMetrics"" ALTER COLUMN ""Pipeline"" TYPE smallint USING CASE ""Pipeline"" WHEN 'BASELINE' THEN 1 WHEN 'HIERARCHICAL' THEN 2 ELSE 1 END;
                ALTER TABLE ingestion.""IngestionMetrics"" ALTER COLUMN ""Bulletin"" TYPE smallint USING CASE ""Bulletin"" WHEN 'BOE' THEN 1 WHEN 'BOJA' THEN 2 WHEN 'BOPMA' THEN 3 ELSE 1 END;
                ALTER TABLE ingestion.""ChildFragments"" ALTER COLUMN ""Bulletin"" TYPE smallint USING CASE ""Bulletin"" WHEN 'BOE' THEN 1 WHEN 'BOJA' THEN 2 WHEN 'BOPMA' THEN 3 ELSE 1 END;
                ALTER TABLE arena.""ArenaBattles"" ALTER COLUMN ""Winner"" TYPE smallint USING CASE ""Winner"" WHEN 'PENDING' THEN 0 WHEN 'ALFA' THEN 1 WHEN 'BETA' THEN 2 WHEN 'TIE' THEN 3 WHEN 'BOTH_BAD' THEN 4 ELSE 0 END;
                ALTER TABLE arena.""ArenaBattles"" ALTER COLUMN ""LeftSystem"" TYPE smallint USING CASE ""LeftSystem"" WHEN 'BASELINE' THEN 1 WHEN 'HIERARCHICAL' THEN 2 ELSE 1 END;
                ALTER TABLE arena.""ArenaBattles"" ALTER COLUMN ""RightSystem"" TYPE smallint USING CASE ""RightSystem"" WHEN 'BASELINE' THEN 1 WHEN 'HIERARCHICAL' THEN 2 ELSE 1 END;
                ALTER TABLE arena.""ArenaBattles"" ALTER COLUMN ""ClarityReason"" TYPE smallint USING CASE ""ClarityReason"" WHEN 'ALFA' THEN 1 WHEN 'BETA' THEN 2 WHEN 'EQUAL' THEN 3 ELSE NULL END;
                ALTER TABLE arena.""ArenaBattles"" ALTER COLUMN ""PrecisionReason"" TYPE smallint USING CASE ""PrecisionReason"" WHEN 'ALFA' THEN 1 WHEN 'BETA' THEN 2 WHEN 'EQUAL' THEN 3 ELSE NULL END;
            ");

            migrationBuilder.AlterColumn<byte>(
                name: "Bulletin",
                schema: "ingestion",
                table: "ParentDocuments",
                type: "smallint",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(10)",
                oldMaxLength: 10);

            migrationBuilder.AlterColumn<byte>(
                name: "Pipeline",
                schema: "ingestion",
                table: "IngestionMetrics",
                type: "smallint",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(30)",
                oldMaxLength: 30);

            migrationBuilder.AlterColumn<byte>(
                name: "Bulletin",
                schema: "ingestion",
                table: "IngestionMetrics",
                type: "smallint",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(10)",
                oldMaxLength: 10);

            migrationBuilder.AlterColumn<byte>(
                name: "Bulletin",
                schema: "ingestion",
                table: "ChildFragments",
                type: "smallint",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(10)",
                oldMaxLength: 10);

            migrationBuilder.AlterColumn<byte>(
                name: "Winner",
                schema: "arena",
                table: "ArenaBattles",
                type: "smallint",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(20)",
                oldMaxLength: 20);

            migrationBuilder.AlterColumn<byte>(
                name: "RightSystem",
                schema: "arena",
                table: "ArenaBattles",
                type: "smallint",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(30)",
                oldMaxLength: 30);

            migrationBuilder.AlterColumn<byte>(
                name: "PrecisionReason",
                schema: "arena",
                table: "ArenaBattles",
                type: "smallint",
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(20)",
                oldMaxLength: 20,
                oldNullable: true);

            migrationBuilder.AlterColumn<byte>(
                name: "LeftSystem",
                schema: "arena",
                table: "ArenaBattles",
                type: "smallint",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(30)",
                oldMaxLength: 30);

            migrationBuilder.AlterColumn<byte>(
                name: "ClarityReason",
                schema: "arena",
                table: "ArenaBattles",
                type: "smallint",
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(20)",
                oldMaxLength: 20,
                oldNullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<string>(
                name: "Bulletin",
                schema: "ingestion",
                table: "ParentDocuments",
                type: "character varying(10)",
                maxLength: 10,
                nullable: false,
                oldClrType: typeof(byte),
                oldType: "smallint");

            migrationBuilder.AlterColumn<string>(
                name: "Pipeline",
                schema: "ingestion",
                table: "IngestionMetrics",
                type: "character varying(30)",
                maxLength: 30,
                nullable: false,
                oldClrType: typeof(byte),
                oldType: "smallint");

            migrationBuilder.AlterColumn<string>(
                name: "Bulletin",
                schema: "ingestion",
                table: "IngestionMetrics",
                type: "character varying(10)",
                maxLength: 10,
                nullable: false,
                oldClrType: typeof(byte),
                oldType: "smallint");

            migrationBuilder.AlterColumn<string>(
                name: "Bulletin",
                schema: "ingestion",
                table: "ChildFragments",
                type: "character varying(10)",
                maxLength: 10,
                nullable: false,
                oldClrType: typeof(byte),
                oldType: "smallint");

            migrationBuilder.AlterColumn<string>(
                name: "Winner",
                schema: "arena",
                table: "ArenaBattles",
                type: "character varying(20)",
                maxLength: 20,
                nullable: false,
                oldClrType: typeof(byte),
                oldType: "smallint");

            migrationBuilder.AlterColumn<string>(
                name: "RightSystem",
                schema: "arena",
                table: "ArenaBattles",
                type: "character varying(30)",
                maxLength: 30,
                nullable: false,
                oldClrType: typeof(byte),
                oldType: "smallint");

            migrationBuilder.AlterColumn<string>(
                name: "PrecisionReason",
                schema: "arena",
                table: "ArenaBattles",
                type: "character varying(20)",
                maxLength: 20,
                nullable: true,
                oldClrType: typeof(byte),
                oldType: "smallint",
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "LeftSystem",
                schema: "arena",
                table: "ArenaBattles",
                type: "character varying(30)",
                maxLength: 30,
                nullable: false,
                oldClrType: typeof(byte),
                oldType: "smallint");

            migrationBuilder.AlterColumn<string>(
                name: "ClarityReason",
                schema: "arena",
                table: "ArenaBattles",
                type: "character varying(20)",
                maxLength: 20,
                nullable: true,
                oldClrType: typeof(byte),
                oldType: "smallint",
                oldNullable: true);
        }
    }
}
