using System.Text.Json;
using AsistenteAyuntamiento.Application.Common.Interfaces;
using AsistenteAyuntamiento.Application.Features.Ingestion;
using AsistenteAyuntamiento.Domain.Common.Enums;
using AsistenteAyuntamiento.Domain.Features.Ingestion;
using AsistenteAyuntamiento.Infrastructure.Data;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Moq;
using Xunit;

namespace AsistenteAyuntamiento.Infrastructure.Tests.Features.Ingestion;

/// <summary>
/// A scraped document is published to both queues: the first worker to receive it records the other pipeline as
/// queued, so the summary does not show it as completed while the slower pipeline has not processed it.
/// </summary>
public class RequestedPipelinesTests
{
    private readonly string _databaseName = $"requested-{Guid.NewGuid()}";

    private AppDbContext NewContext() => new(
        new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(_databaseName).Options,
        Mock.Of<ICurrentTenantService>(t => t.TenantId == "system"));

    private static readonly PipelineType[] Both = [PipelineType.Baseline, PipelineType.Hierarchical];

    [Fact]
    public async Task A_document_completed_by_the_baseline_stays_queued_until_the_hierarchical_pipeline_runs()
    {
        await using var db = NewContext();

        await DocumentJobStateUpdates.MarkQueuedIfUnsetAsync(db, "BOJA-2026-198-1", Both, default);
        var state = await DocumentJobStateUpdates.SetPipelineStatusAsync(db, "BOJA-2026-198-1", PipelineType.Baseline, JobStatus.Completed, null, default);

        state.HierarchicalStatus.Should().Be(JobStatus.Queued);
        state.Status.Should().Be(JobStatus.Queued, "before this change it was Completed with the hierarchical pipeline never requested");
    }

    [Fact]
    public async Task A_pipeline_that_already_has_a_status_is_not_overwritten()
    {
        await using (var seed = NewContext())
        {
            seed.DocumentJobStates.Add(new DocumentJobState { DocumentId = "BOE-A-2026-1", HierarchicalStatus = JobStatus.Completed, Status = JobStatus.Completed });
            await seed.SaveChangesAsync();
        }

        await using var db = NewContext();
        var state = await DocumentJobStateUpdates.MarkQueuedIfUnsetAsync(db, "BOE-A-2026-1", Both, default);

        state!.HierarchicalStatus.Should().Be(JobStatus.Completed, "a redelivered message must not move a finished pipeline back to queued");
        state.BaselineStatus.Should().Be(JobStatus.Queued);
    }

    [Fact]
    public async Task Nothing_changes_when_every_requested_pipeline_already_has_a_status()
    {
        await using var db = NewContext();
        await DocumentJobStateUpdates.SetPipelineStatusAsync(db, "BOE-A-2026-2", PipelineType.Baseline, JobStatus.Processing, null, default);
        await DocumentJobStateUpdates.SetPipelineStatusAsync(db, "BOE-A-2026-2", PipelineType.Hierarchical, JobStatus.Processing, null, default);

        (await DocumentJobStateUpdates.MarkQueuedIfUnsetAsync(db, "BOE-A-2026-2", Both, default)).Should().BeNull();
        (await DocumentJobStateUpdates.MarkQueuedIfUnsetAsync(db, "BOE-A-2026-3", [], default)).Should().BeNull();
    }

    [Fact]
    public void The_message_reads_the_pipelines_the_scraper_published_to()
    {
        var message = JsonSerializer.Deserialize<DocumentMessage>(
            """{"source":"BOJA","document_id":"BOJA-2026-198-1","blob_path":"json/BOJA/BOJA-2026-198-1.json","pipelines":["BASELINE","HIERARCHICAL","OTHER","7"]}""")!;

        message.RequestedPipelines().Should().Equal(PipelineType.Baseline, PipelineType.Hierarchical);
        new DocumentMessage().RequestedPipelines().Should().BeEmpty("older messages and administrator re-queues carry no list");
    }
}
