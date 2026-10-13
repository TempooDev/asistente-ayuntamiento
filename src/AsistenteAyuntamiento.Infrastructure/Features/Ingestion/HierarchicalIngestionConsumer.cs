using AsistenteAyuntamiento.Application.Common.Interfaces;
using AsistenteAyuntamiento.Application.Features.Ingestion;
using AsistenteAyuntamiento.Domain.Common.Enums;
using AsistenteAyuntamiento.Infrastructure.Messaging;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using RabbitMQ.Client;

namespace AsistenteAyuntamiento.Infrastructure.Features.Ingestion;

/// <summary>Consumes <c>documents_to_process_hierarchical</c> and dispatches to the processor of the bulletin.</summary>
public sealed class HierarchicalIngestionConsumer(
    IConnectionFactory connectionFactory,
    IServiceScopeFactory scopeFactory,
    RabbitMqConsumerHealth health,
    ILogger<HierarchicalIngestionConsumer> logger)
    : RabbitMqConsumerBase<DocumentMessage>(connectionFactory, scopeFactory, health, logger)
{
    protected override string ConsumerName => "hierarchical-ingestion";

    protected override async Task<string> DeclareTopologyAsync(IChannel channel, CancellationToken cancellationToken)
    {
        await RabbitMqTopology.DeclareWorkQueueAsync(channel, RabbitMqTopology.HierarchicalQueue, cancellationToken);
        return RabbitMqTopology.HierarchicalQueue;
    }

    protected override async Task HandleAsync(DocumentMessage message, IServiceProvider services, CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(message.BlobPath) || string.IsNullOrEmpty(message.DocumentId))
        {
            throw new NonRetryableMessageException("Message without blob_path or document_id.");
        }

        var processor = services.GetKeyedService<IHierarchicalIngestionProcessor>(message.Source.ToUpperInvariant())
            ?? throw new NonRetryableMessageException($"No hierarchical processor for source '{message.Source}'.");

        await IngestionJobStates.MarkRequestedAsync(services, message, cancellationToken);
        await IngestionJobStates.SetAsync(services, message.DocumentId, PipelineType.Hierarchical, JobStatus.Processing, null, cancellationToken);

        try
        {
            await processor.ProcessDocumentAsync(message.BlobPath, message.DocumentId, cancellationToken);
        }
        catch (VectorSyncFailedException ex)
        {
            // Visible while the message is retried; the retry re-syncs the vectors.
            await IngestionJobStates.SetAsync(services, message.DocumentId, PipelineType.Hierarchical, JobStatus.VectorSyncFailed, ex.InnerException?.Message, cancellationToken);
            throw;
        }

        await IngestionJobStates.SetAsync(services, message.DocumentId, PipelineType.Hierarchical, JobStatus.Completed, null, cancellationToken);
    }

    protected override Task OnDeadLetteredAsync(DocumentMessage? message, Exception exception, IServiceProvider services, CancellationToken cancellationToken) =>
        message is null
            ? Task.CompletedTask
            : IngestionJobStates.MarkFailedAsync(services, message.DocumentId, PipelineType.Hierarchical, exception, cancellationToken);
}

/// <summary>Job-state updates shared by the ingestion consumers: each pipeline updates its own status.</summary>
internal static class IngestionJobStates
{
    /// <summary>Records the pipeline's status and notifies the document's summary.</summary>
    public static async Task SetAsync(IServiceProvider services, string documentId, PipelineType pipeline, JobStatus status, string? error, CancellationToken cancellationToken)
    {
        var state = await DocumentJobStateUpdates.SetPipelineStatusAsync(
            services.GetRequiredService<IAppDbContext>(), documentId, pipeline, status, error, cancellationToken);
        await services.GetRequiredService<INotificationService>().NotifyDocumentStatusChangedAsync(documentId, state.Status.ToString());
    }

    /// <summary>
    /// Marks the pipelines the message was published to as queued when they have no status yet, so the summary does
    /// not show a document as completed while the slower pipeline has not reached it (see
    /// <see cref="DocumentJobStateUpdates.MarkQueuedIfUnsetAsync"/>).
    /// </summary>
    public static async Task MarkRequestedAsync(IServiceProvider services, DocumentMessage message, CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(message.DocumentId))
        {
            return;
        }

        var state = await DocumentJobStateUpdates.MarkQueuedIfUnsetAsync(
            services.GetRequiredService<IAppDbContext>(), message.DocumentId, message.RequestedPipelines(), cancellationToken);
        if (state is not null)
        {
            await services.GetRequiredService<INotificationService>().NotifyDocumentStatusChangedAsync(message.DocumentId, state.Status.ToString());
        }
    }

    public static Task MarkFailedAsync(IServiceProvider services, string documentId, PipelineType pipeline, Exception exception, CancellationToken cancellationToken) =>
        string.IsNullOrEmpty(documentId)
            ? Task.CompletedTask
            : SetAsync(services, documentId, pipeline, JobStatus.Failed, exception.Message, cancellationToken);
}
