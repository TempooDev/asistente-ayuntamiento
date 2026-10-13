using AsistenteAyuntamiento.Application.Common.Interfaces;
using AsistenteAyuntamiento.Application.Common.Messaging;
using AsistenteAyuntamiento.Application.Common.Storage;
using AsistenteAyuntamiento.Application.Common.VectorStore;
using AsistenteAyuntamiento.Application.Features.Ingestion;
using AsistenteAyuntamiento.Application.Features.Ingestion.DTOs;
using AsistenteAyuntamiento.Domain.Common.Enums;
using AsistenteAyuntamiento.Domain.Features.Ingestion;
using AsistenteAyuntamiento.Infrastructure.Data;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace AsistenteAyuntamiento.Infrastructure.Tests.Features.Ingestion;

/// <summary>Bulk enqueue: state is saved before publishing, failed publishes are reverted (spec: ingestion-consistency).</summary>
public class IngestionEnqueueTests
{
    private readonly string _databaseName = $"enqueue-{Guid.NewGuid()}";

    private AppDbContext NewContext() => new(
        new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(_databaseName).Options,
        Mock.Of<ICurrentTenantService>(t => t.TenantId == "system"));

    private readonly Mock<INotificationService> _notifications = new();
    private readonly List<IReadOnlyList<DocumentStatusChange>> _notifiedBatches = [];

    public IngestionEnqueueTests() =>
        _notifications.Setup(n => n.NotifyDocumentStatusesChangedAsync(It.IsAny<IReadOnlyList<DocumentStatusChange>>()))
            .Callback<IReadOnlyList<DocumentStatusChange>>(_notifiedBatches.Add)
            .Returns(Task.CompletedTask);

    private IngestionAdminService CreateSut(AppDbContext db, IIngestionQueuePublisher publisher, IBlobStorage? blobs = null) => new(
        blobs ?? Mock.Of<IBlobStorage>(),
        db,
        new MemoryCache(new MemoryCacheOptions()),
        publisher,
        Mock.Of<IVectorStore>(),
        Mock.Of<IIngestionMaintenance>(),
        _notifications.Object,
        NullLogger<IngestionAdminService>.Instance);

    private static ProcessBlobRequest Blob(string id) => new() { BlobPath = $"json/BOE/{id}.json" };

    [Fact]
    public async Task ReprocessAll_enqueues_every_listed_json_blob_in_batches()
    {
        var keys = Enumerable.Range(1, 2_500).Select(i => $"json/BOE/BOE-A-{i}.json").ToList();
        var blobs = new Mock<IBlobStorage>();
        blobs.Setup(b => b.ListAsync("json/", It.IsAny<CancellationToken>()))
            .Returns(keys.Select(k => new BlobObjectInfo(k, 1, null)).ToAsyncEnumerable());
        var publisher = new FakePublisher(_ => Task.CompletedTask);

        await using var db = NewContext();
        var result = await CreateSut(db, publisher, blobs.Object).ReprocessAllAsync("HIERARCHICAL");

        result.Should().Be(new EnqueueResult(Enqueued: 2_500, Failed: 0));
        publisher.Pipelines.Should().HaveCount(2_500).And.AllBeEquivalentTo(IngestionPipelines.Hierarchical);
    }

    [Fact]
    public async Task Documents_are_already_Queued_in_the_database_when_they_are_published()
    {
        var statusSeenAtPublish = new Dictionary<string, JobStatus?>();
        var publisher = new FakePublisher(async message =>
        {
            await using var reader = NewContext();
            statusSeenAtPublish[message.DocumentId] = (await reader.DocumentJobStates.AsNoTracking()
                .SingleOrDefaultAsync(j => j.DocumentId == message.DocumentId))?.Status;
        });

        await using var db = NewContext();
        await CreateSut(db, publisher).EnqueueBulkAsync([Blob("BOE-A-1"), Blob("BOE-A-2")], "BOTH");

        statusSeenAtPublish.Should().BeEquivalentTo(new Dictionary<string, JobStatus?>
        {
            ["BOE-A-1"] = JobStatus.Queued,
            ["BOE-A-2"] = JobStatus.Queued,
        });
        publisher.Pipelines.Should().AllBeEquivalentTo(IngestionPipelines.Both);
    }

    [Fact]
    public async Task Failed_publishes_are_reverted_to_Pending_and_reported()
    {
        var publisher = new FakePublisher(message =>
            message.DocumentId == "BOE-A-2" ? throw new InvalidOperationException("broker nack") : Task.CompletedTask);

        await using (var seed = NewContext())
        {
            seed.DocumentJobStates.Add(new DocumentJobState { DocumentId = "BOE-A-1", Status = JobStatus.Failed, ErrorMessage = "old" });
            await seed.SaveChangesAsync();
        }

        await using var db = NewContext();
        var result = await CreateSut(db, publisher).EnqueueBulkAsync([Blob("BOE-A-1"), Blob("BOE-A-2"), Blob("BOE-A-3")], "BASELINE");

        result.Should().Be(new EnqueueResult(Enqueued: 2, Failed: 1));

        await using var check = NewContext();
        var states = await check.DocumentJobStates.AsNoTracking().ToDictionaryAsync(j => j.DocumentId);
        states["BOE-A-1"].Status.Should().Be(JobStatus.Queued);
        states["BOE-A-1"].ErrorMessage.Should().BeNull();
        states["BOE-A-2"].Status.Should().Be(JobStatus.Pending);
        states["BOE-A-3"].Status.Should().Be(JobStatus.Queued);

        // One notification batch with the final states, instead of one message per document.
        _notifiedBatches.Should().ContainSingle().Which.Should().BeEquivalentTo(new[]
        {
            new DocumentStatusChange("BOE-A-1", "Queued"),
            new DocumentStatusChange("BOE-A-2", "Pending"),
            new DocumentStatusChange("BOE-A-3", "Queued"),
        });
        _notifications.Verify(n => n.NotifyDocumentStatusChangedAsync(It.IsAny<string>(), It.IsAny<string>()), Times.Never);
        publisher.Pipelines.Should().AllBeEquivalentTo(IngestionPipelines.Baseline);
    }

    private sealed class FakePublisher(Func<DocumentMessage, Task> onPublish) : IIngestionQueuePublisher
    {
        public List<IngestionPipelines> Pipelines { get; } = [];

        public async Task PublishAsync(DocumentMessage message, IngestionPipelines pipelines, CancellationToken cancellationToken = default)
        {
            Pipelines.Add(pipelines);
            await onPublish(message);
        }
    }
}
