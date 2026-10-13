using AsistenteAyuntamiento.Application.Common.Interfaces;
using AsistenteAyuntamiento.Application.Features.Ingestion;
using AsistenteAyuntamiento.Domain.Common.Enums;
using AsistenteAyuntamiento.Infrastructure.Data;
using AsistenteAyuntamiento.Infrastructure.Tests.Features.Messaging;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Moq;
using Testcontainers.PostgreSql;

namespace AsistenteAyuntamiento.Infrastructure.Tests.Features.Ingestion;

/// <summary>
/// The two workers update the same document row at the same time (real PostgreSQL with the migrations, including
/// the per-pipeline columns and the xmin row version): neither status is lost.
/// </summary>
public sealed class DocumentJobStateConcurrencyTests : IAsyncLifetime
{
    private PostgreSqlContainer? _postgres;

    public async Task InitializeAsync()
    {
        if (!DockerFactAttribute.IsDockerAvailable) return;
        _postgres = new PostgreSqlBuilder("pgvector/pgvector:pg17").Build();
        await _postgres.StartAsync();
        await using var db = NewContext();
        await db.Database.MigrateAsync();
    }

    public async Task DisposeAsync()
    {
        if (_postgres is not null) await _postgres.DisposeAsync();
    }

    private AppDbContext NewContext() => new(
        new DbContextOptionsBuilder<AppDbContext>().UseNpgsql(_postgres!.GetConnectionString(), o => o.UseVector()).Options,
        Mock.Of<ICurrentTenantService>(t => t.TenantId == "system"));

    [DockerFact]
    public async Task Concurrent_updates_from_both_pipelines_keep_both_statuses()
    {
        for (var round = 0; round < 10; round++)
        {
            var documentId = $"BOE-A-{round}";
            await using (var seed = NewContext())
            {
                await DocumentJobStateUpdates.SetPipelineStatusAsync(seed, documentId, PipelineType.Baseline, JobStatus.Processing, null, default);
            }

            await using var baseline = NewContext();
            await using var hierarchical = NewContext();
            await Task.WhenAll(
                DocumentJobStateUpdates.SetPipelineStatusAsync(baseline, documentId, PipelineType.Baseline, JobStatus.Completed, null, default),
                DocumentJobStateUpdates.SetPipelineStatusAsync(hierarchical, documentId, PipelineType.Hierarchical, JobStatus.Failed, "boom", default));

            await using var check = NewContext();
            var state = await check.DocumentJobStates.AsNoTracking().SingleAsync(j => j.DocumentId == documentId);
            state.BaselineStatus.Should().Be(JobStatus.Completed);
            state.HierarchicalStatus.Should().Be(JobStatus.Failed);
            state.Status.Should().Be(JobStatus.Failed);
        }
    }

    [DockerFact]
    public async Task Concurrent_first_inserts_do_not_fail()
    {
        await using var baseline = NewContext();
        await using var hierarchical = NewContext();

        await Task.WhenAll(
            DocumentJobStateUpdates.SetPipelineStatusAsync(baseline, "BOJA-2026-1-1", PipelineType.Baseline, JobStatus.Processing, null, default),
            DocumentJobStateUpdates.SetPipelineStatusAsync(hierarchical, "BOJA-2026-1-1", PipelineType.Hierarchical, JobStatus.Processing, null, default));

        await using var check = NewContext();
        var state = await check.DocumentJobStates.AsNoTracking().SingleAsync(j => j.DocumentId == "BOJA-2026-1-1");
        (state.BaselineStatus, state.HierarchicalStatus).Should().Be(((JobStatus?)JobStatus.Processing, (JobStatus?)JobStatus.Processing));
    }
}
