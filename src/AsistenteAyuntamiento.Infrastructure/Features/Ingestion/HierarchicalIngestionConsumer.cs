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

        var dbContext = services.GetRequiredService<IAppDbContext>();
        await IngestionJobStates.SetAsync(dbContext, message.DocumentId, JobStatus.Processing, null, cancellationToken);

        await processor.ProcessDocumentAsync(message.BlobPath, message.DocumentId, cancellationToken);

        await IngestionJobStates.SetAsync(dbContext, message.DocumentId, JobStatus.Completed, null, cancellationToken);
    }

    protected override Task OnDeadLetteredAsync(DocumentMessage? message, Exception exception, IServiceProvider services, CancellationToken cancellationToken) =>
        message is null
            ? Task.CompletedTask
            : IngestionJobStates.MarkFailedAsync(services, message.DocumentId, exception, cancellationToken);
}

/// <summary>Job-state updates shared by the ingestion consumers.</summary>
internal static class IngestionJobStates
{
    public static async Task SetAsync(IAppDbContext dbContext, string documentId, JobStatus status, string? error, CancellationToken cancellationToken)
    {
        var jobState = await dbContext.DocumentJobStates.FirstOrDefaultAsync(j => j.DocumentId == documentId, cancellationToken);
        if (jobState is null)
        {
            dbContext.DocumentJobStates.Add(new Domain.Features.Ingestion.DocumentJobState
            {
                DocumentId = documentId,
                Status = status,
                ErrorMessage = error,
            });
        }
        else
        {
            jobState.Status = status;
            jobState.ErrorMessage = error;
            jobState.LastUpdatedAt = DateTime.UtcNow;
        }

        await dbContext.SaveChangesAsync(cancellationToken);
    }

    public static async Task MarkFailedAsync(IServiceProvider services, string documentId, Exception exception, CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(documentId)) return;

        var dbContext = services.GetRequiredService<IAppDbContext>();
        await SetAsync(dbContext, documentId, JobStatus.Failed, exception.Message, cancellationToken);
        await services.GetRequiredService<INotificationService>()
            .NotifyDocumentStatusChangedAsync(documentId, JobStatus.Failed.ToString());
    }
}
