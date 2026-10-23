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
}
