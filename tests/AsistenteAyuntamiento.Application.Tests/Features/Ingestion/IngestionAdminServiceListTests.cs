using AsistenteAyuntamiento.Application.Common.Interfaces;
using AsistenteAyuntamiento.Application.Common.Messaging;
using AsistenteAyuntamiento.Application.Common.Notifications;
using AsistenteAyuntamiento.Application.Common.Storage;
using AsistenteAyuntamiento.Application.Common.VectorStore;
using AsistenteAyuntamiento.Application.Features.Ingestion;
using AsistenteAyuntamiento.Application.Features.Ingestion.DTOs;
using AsistenteAyuntamiento.Domain.Common.Enums;
using AsistenteAyuntamiento.Domain.Features.Ingestion;
using FluentAssertions;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using MockQueryable.Moq;
using Moq;

namespace AsistenteAyuntamiento.Application.Tests.Features.Ingestion;

/// <summary>The documents page: per-pipeline statuses and progress.</summary>
public class IngestionAdminServiceListTests
{
    private static IngestionAdminService CreateSut()
    {
        var blobs = new Mock<IBlobStorage>();
        blobs.Setup(b => b.ListAsync("json/", It.IsAny<CancellationToken>()))
            .Returns(new[] { "BOE-1", "BOE-2", "BOE-3", "BOE-4" }.Select(id => new BlobObjectInfo($"json/BOE/{id}.json", 100, DateTime.UtcNow)).ToAsyncEnumerable());

        var db = new Mock<IAppDbContext>();
        db.Setup(d => d.DocumentJobStates).Returns(new List<DocumentJobState>
        {
            State("BOE-1", JobStatus.Completed, JobStatus.Completed, recursive: JobStatus.Completed, semantic: JobStatus.Completed),
            State("BOE-2", JobStatus.Completed, JobStatus.Queued, recursive: JobStatus.Completed, semantic: JobStatus.Processing),
            State("BOE-3", JobStatus.Completed, JobStatus.Failed, "Hierarchical: boom"),
        }.BuildMockDbSet().Object);

        return new IngestionAdminService(blobs.Object, db.Object, new MemoryCache(new MemoryCacheOptions()), Mock.Of<IIngestionQueuePublisher>(),
            Mock.Of<IVectorStore>(), Mock.Of<IIngestionMaintenance>(), Mock.Of<INotificationService>(), NullLogger<IngestionAdminService>.Instance);
    }

    private static DocumentJobState State(string id, JobStatus baseline, JobStatus hierarchical, string? error = null,
        JobStatus? recursive = null, JobStatus? semantic = null)
    {
        var state = new DocumentJobState { DocumentId = id };
        state.SetPipelineStatus(PipelineType.Baseline, baseline);
        state.SetPipelineStatus(PipelineType.Hierarchical, hierarchical, error);
        if (recursive is { } r) state.SetPipelineStatus(PipelineType.Recursive, r);
        if (semantic is { } s) state.SetPipelineStatus(PipelineType.Semantic, s);
        return state;
    }

    [Fact]
    public async Task Each_pipeline_reports_its_own_progress()
    {
        var result = await CreateSut().ListBlobsAsync(1, 20, null, null, null, null, null, null);

        result.PipelineStats.Should().Equal(
            new PipelineBlobStats("Baseline", 4, Completed: 3, Processing: 0, Queued: 0, Failed: 0, Pending: 0, NotSent: 1),
            new PipelineBlobStats("Hierarchical", 4, Completed: 1, Processing: 0, Queued: 1, Failed: 1, Pending: 0, NotSent: 1),
            new PipelineBlobStats("Recursive", 4, Completed: 2, Processing: 0, Queued: 0, Failed: 0, Pending: 0, NotSent: 2),
            new PipelineBlobStats("Semantic", 4, Completed: 1, Processing: 1, Queued: 0, Failed: 0, Pending: 0, NotSent: 2));
        var second = result.Items.Single(i => i.Name.Contains("BOE-2"));
        (second.RecursiveStatus, second.SemanticStatus).Should().Be(("Completed", "Processing"));
        var failed = result.Items.Single(i => i.Name.Contains("BOE-3"));
        (failed.BaselineStatus, failed.HierarchicalStatus).Should().Be(("Completed", "Failed"));
        failed.ErrorMessage.Should().Contain("boom");
        result.Items.Single(i => i.Name.Contains("BOE-4")).BaselineStatus.Should().BeNull("never sent");
    }

    [Fact]
    public async Task The_status_filter_applies_to_the_chosen_pipeline()
    {
        var sut = CreateSut();

        var baselineDone = await sut.ListBlobsAsync(1, 20, "Procesados", null, null, null, null, null, pipeline: "BASELINE");
        var hierarchicalDone = await sut.ListBlobsAsync(1, 20, "Procesados", null, null, null, null, null, pipeline: "HIERARCHICAL");
        var hierarchicalPending = await sut.ListBlobsAsync(1, 20, "Pendientes", null, null, null, null, null, pipeline: "HIERARCHICAL");

        baselineDone.Total.Should().Be(3);
        hierarchicalDone.Total.Should().Be(1);
        hierarchicalPending.Items.Select(i => i.Name).Should().BeEquivalentTo("json/BOE/BOE-3.json", "json/BOE/BOE-4.json");

        (await sut.ListBlobsAsync(1, 20, "Procesados", null, null, null, null, null, pipeline: "RECURSIVE")).Total.Should().Be(2);
        (await sut.ListBlobsAsync(1, 20, "Encolados", null, null, null, null, null, pipeline: "semantic")).Items
            .Select(i => i.Name).Should().Equal("json/BOE/BOE-2.json");
    }
}
