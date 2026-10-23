using AsistenteAyuntamiento.Application.Common.Interfaces;
using AsistenteAyuntamiento.Domain.Common.Enums;
using AsistenteAyuntamiento.Domain.Features.Ingestion;
using AsistenteAyuntamiento.Infrastructure.Data;
using AsistenteAyuntamiento.Infrastructure.Tests.Features.Messaging;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Moq;
using Testcontainers.PostgreSql;

namespace AsistenteAyuntamiento.Infrastructure.Tests.Features.Ingestion;

/// <summary>
/// The baseline reset (<c>/api/ingestion/reset</c>) only forgets what it deletes: baseline chunks and baseline statuses.
/// The other pipelines' chunks and statuses, whose data stays, are kept (spec: ingestion-administration). Runs on PostgreSQL (raw SQL
/// path) and on the in-memory provider (EF path).
/// </summary>
public sealed class IngestionMaintenanceTests : IAsyncLifetime
{
    private PostgreSqlContainer? _postgres;
    private readonly string _inMemoryName = $"maintenance-{Guid.NewGuid()}";

    public async Task InitializeAsync()
    {
        if (!DockerFactAttribute.IsDockerAvailable) return;
        _postgres = new PostgreSqlBuilder("pgvector/pgvector:pg17").Build();
        await _postgres.StartAsync();
        await using var db = PostgresContext();
        await db.Database.MigrateAsync();
    }

    public async Task DisposeAsync()
    {
        if (_postgres is not null) await _postgres.DisposeAsync();
    }

    private AppDbContext PostgresContext() => new(
        new DbContextOptionsBuilder<AppDbContext>().UseNpgsql(_postgres!.GetConnectionString(), o => o.UseVector()).Options,
        Mock.Of<ICurrentTenantService>(t => t.TenantId == "system"));

    private AppDbContext InMemoryContext() => new(
        new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(_inMemoryName).Options,
        Mock.Of<ICurrentTenantService>(t => t.TenantId == "system"));

    [DockerFact]
    public Task Postgres_reset_keeps_hierarchical_statuses() => ResetKeepsHierarchicalStatusesAsync(PostgresContext);

    [Fact]
    public Task In_memory_reset_keeps_hierarchical_statuses() => ResetKeepsHierarchicalStatusesAsync(InMemoryContext);

    private static async Task ResetKeepsHierarchicalStatusesAsync(Func<AppDbContext> newContext)
    {
        await using (var seed = newContext())
        {
            seed.DocumentChunks.Add(new DocumentChunk { DocumentId = "BOE-A-1", Source = "BOE", Content = "texto", PublicationDate = DateTime.UtcNow });
            seed.DocumentChunks.Add(new DocumentChunk { DocumentId = "BOE-A-1", Source = "BOE", Content = "texto", PublicationDate = DateTime.UtcNow, ChunkingStrategy = "Recursive" });
            seed.DocumentJobStates.AddRange(
                State("baseline-only", s => s.SetPipelineStatus(PipelineType.Baseline, JobStatus.Completed)),
                State("both", s =>
                {
                    s.SetPipelineStatus(PipelineType.Hierarchical, JobStatus.Completed);
                    s.SetPipelineStatus(PipelineType.Baseline, JobStatus.VectorSyncFailed, "qdrant down");
                }),
                State("hierarchical-failed", s =>
                {
                    s.SetPipelineStatus(PipelineType.Baseline, JobStatus.Completed);
                    s.SetPipelineStatus(PipelineType.Hierarchical, JobStatus.Failed, "llm down");
                }),
                State("hierarchical-only", s => s.SetPipelineStatus(PipelineType.Hierarchical, JobStatus.Queued)),
                State("baseline-and-semantic", s =>
                {
                    s.SetPipelineStatus(PipelineType.Baseline, JobStatus.Completed);
                    s.SetPipelineStatus(PipelineType.Semantic, JobStatus.Processing);
                }),
                State("hierarchical-and-recursive", s =>
                {
                    s.SetPipelineStatus(PipelineType.Baseline, JobStatus.Completed);
                    s.SetPipelineStatus(PipelineType.Hierarchical, JobStatus.Completed);
                    s.SetPipelineStatus(PipelineType.Recursive, JobStatus.Failed, "ollama down");
                }));
            await seed.SaveChangesAsync();
        }

        await using (var db = newContext())
        {
            await new IngestionMaintenance(db).ClearBaselineDataAsync();
        }

        await using var check = newContext();
        (await check.DocumentChunks.Select(c => c.ChunkingStrategy).ToListAsync()).Should().Equal(["Recursive"], "only the baseline's chunks are deleted");
        var states = await check.DocumentJobStates.AsNoTracking().ToDictionaryAsync(j => j.DocumentId);
        states.Keys.Should().BeEquivalentTo(["both", "hierarchical-failed", "hierarchical-only", "baseline-and-semantic", "hierarchical-and-recursive"], "a row with only a baseline status describes deleted data");
        states.Values.Should().OnlyContain(s => s.BaselineStatus == null);

        (states["both"].Status, states["both"].HierarchicalStatus, states["both"].ErrorMessage)
            .Should().Be((JobStatus.Completed, (JobStatus?)JobStatus.Completed, (string?)null), "the baseline error went with the baseline data");
        (states["hierarchical-failed"].Status, states["hierarchical-failed"].ErrorMessage)
            .Should().Be((JobStatus.Failed, "[Hierarchical] llm down"));
        states["hierarchical-only"].Status.Should().Be(JobStatus.Queued);
        (states["baseline-and-semantic"].Status, states["baseline-and-semantic"].SemanticStatus)
            .Should().Be((JobStatus.Pending, (JobStatus?)JobStatus.Processing), "a study status is kept but never shows in the summary");
        (states["hierarchical-and-recursive"].Status, states["hierarchical-and-recursive"].RecursiveStatus, states["hierarchical-and-recursive"].ErrorMessage)
            .Should().Be((JobStatus.Completed, (JobStatus?)JobStatus.Failed, "[Recursive] ollama down"));
    }

    private static DocumentJobState State(string documentId, Action<DocumentJobState> apply)
    {
        var state = new DocumentJobState { DocumentId = documentId };
        apply(state);
        return state;
    }
}
