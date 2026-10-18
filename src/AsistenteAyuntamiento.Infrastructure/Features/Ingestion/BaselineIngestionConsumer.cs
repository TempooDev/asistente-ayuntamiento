using AsistenteAyuntamiento.Application.Features.Ingestion;
using AsistenteAyuntamiento.Domain.Common.Enums;
using AsistenteAyuntamiento.Infrastructure.Messaging;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using RabbitMQ.Client;

namespace AsistenteAyuntamiento.Infrastructure.Features.Ingestion;

/// <summary>Consumes <c>documents_to_process_baseline</c> and runs the fixed-chunk pipeline.</summary>
public sealed class BaselineIngestionConsumer(
    IConnectionFactory connectionFactory,
    IServiceScopeFactory scopeFactory,
    RabbitMqConsumerHealth health,
    ILogger<BaselineIngestionConsumer> logger)
    : RabbitMqConsumerBase<DocumentMessage>(connectionFactory, scopeFactory, health, logger)
{
    protected override string ConsumerName => "baseline-ingestion";

    protected override async Task<string> DeclareTopologyAsync(IChannel channel, CancellationToken cancellationToken)
    {
        await RabbitMqTopology.DeclareWorkQueueAsync(channel, RabbitMqTopology.BaselineQueue, cancellationToken);
        return RabbitMqTopology.BaselineQueue;
    }

    protected override async Task HandleAsync(DocumentMessage message, IServiceProvider services, CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(message.BlobPath))
        {
            throw new NonRetryableMessageException("Message without blob_path.");
        }

        // The baseline is usually first to receive a scraped document: it records the hierarchical side as queued.
        await IngestionJobStates.MarkRequestedAsync(services, message, cancellationToken);
        await services.GetRequiredService<IDocumentIngestionService>()
            .ProcessBlobAsync(message.BlobPath, message.Source, cancellationToken);
    }

    protected override Task OnDeadLetteredAsync(DocumentMessage? message, Exception exception, IServiceProvider services, CancellationToken cancellationToken) =>
        message is null
            ? Task.CompletedTask
            : IngestionJobStates.MarkFailedAsync(services, message.DocumentId, PipelineType.Baseline, exception, cancellationToken);
}
