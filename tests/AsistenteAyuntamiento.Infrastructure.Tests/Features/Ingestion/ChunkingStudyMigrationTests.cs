using AsistenteAyuntamiento.Application.Common.Interfaces;
using AsistenteAyuntamiento.Domain.Common.Enums;
using AsistenteAyuntamiento.Domain.Features.Ingestion;
using AsistenteAyuntamiento.Infrastructure.Data;
using AsistenteAyuntamiento.Infrastructure.Tests.Features.Messaging;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Moq;
using Testcontainers.PostgreSql;

namespace AsistenteAyuntamiento.Infrastructure.Tests.Features.Ingestion;

/// <summary>
/// The chunking study migration keeps existing data: baseline chunks become <c>FixedOverlap</c> and the new job
/// statuses start unrequested, so the baseline keeps answering without re-ingestion (spec: chunking-embeddings).
/// </summary>
public sealed class ChunkingStudyMigrationTests : IAsyncLifetime
{
    private const string PreviousMigration = "20261022225334_MetricsDashboardEvaluationResults";

    private PostgreSqlContainer? _postgres;

    public async Task InitializeAsync()
    {
        if (!DockerFactAttribute.IsDockerAvailable) return;
        _postgres = new PostgreSqlBuilder("pgvector/pgvector:pg17").Build();
        await _postgres.StartAsync();
    }

    public async Task DisposeAsync()
    {
        if (_postgres is not null) await _postgres.DisposeAsync();
    }

    private AppDbContext NewContext() => new(
        new DbContextOptionsBuilder<AppDbContext>().UseNpgsql(_postgres!.GetConnectionString(), o => o.UseVector()).Options,
        Mock.Of<ICurrentTenantService>(t => t.TenantId == "system"));

    [DockerFact]
    public async Task Existing_chunks_become_fixed_overlap_and_new_statuses_start_null()
    {
        await using (var db = NewContext())
        {
            await db.GetService<IMigrator>().MigrateAsync(PreviousMigration);
            await db.Database.ExecuteSqlRawAsync("""
                INSERT INTO ingestion."DocumentChunks" ("DocumentId", "Source", "Title", "Department", "Content", "ChunkIndex", "PublicationDate")
                VALUES ('BOE-A-2026-1234', 'BOE', 'Ayudas', 'Ministerio', 'texto', 0, now());
                INSERT INTO ingestion."DocumentJobStates" ("DocumentId", "Status", "BaselineStatus", "CreatedAt", "LastUpdatedAt")
                VALUES ('BOE-A-2026-1234', 'Completed', 'Completed', now(), now());
                """);

            await db.Database.MigrateAsync();
        }

        await using var check = NewContext();
        (await check.DocumentChunks.AsNoTracking().SingleAsync()).ChunkingStrategy.Should().Be(DocumentChunk.FixedOverlapStrategy);
        var state = await check.DocumentJobStates.AsNoTracking().SingleAsync();
        (state.RecursiveStatus, state.SemanticStatus, state.BaselineStatus).Should().Be(((JobStatus?)null, (JobStatus?)null, (JobStatus?)JobStatus.Completed));
    }

    [DockerFact]
    public async Task Rolling_back_deletes_the_study_variants_data_before_dropping_its_columns()
    {
        await using (var db = NewContext())
        {
            await db.Database.MigrateAsync();
            await db.Database.ExecuteSqlRawAsync("""
                INSERT INTO ingestion."DocumentChunks" ("DocumentId", "Source", "Title", "Department", "Content", "ChunkIndex", "PublicationDate", "ChunkingStrategy")
                VALUES ('BOE-A-1', 'BOE', 't', 'd', 'fijo', 0, now(), 'FixedOverlap'),
                       ('BOE-A-1', 'BOE', 't', 'd', 'recursivo', 0, now(), 'Recursive'),
                       ('BOE-A-1', 'BOE', 't', 'd', 'semantico', 0, now(), 'Semantic');
                INSERT INTO ingestion."DocumentJobStates" ("DocumentId", "Status", "BaselineStatus", "SemanticStatus", "ErrorMessage", "CreatedAt", "LastUpdatedAt")
                VALUES ('BOE-A-1', 'Completed', 'Completed', 'Failed', '[Semantic] ollama down', now(), now()),
                       ('BOE-A-2', 'Pending', NULL, 'Queued', NULL, now(), now());
                """);

            await db.GetService<IMigrator>().MigrateAsync(PreviousMigration);
        }

        await using var check = NewContext();
        var contents = await check.Database.SqlQueryRaw<string>("""SELECT "Content" AS "Value" FROM ingestion."DocumentChunks" """).ToListAsync();
        contents.Should().Equal(["fijo"], "only the baseline chunks are left for the previous schema");
        var states = await check.Database.SqlQueryRaw<string>("""SELECT "DocumentId" || ':' || coalesce("ErrorMessage", '') AS "Value" FROM ingestion."DocumentJobStates" """).ToListAsync();
        states.Should().Equal(["BOE-A-1:"], "a row with only study statuses describes deleted data, and the study error goes with it");
    }
}
