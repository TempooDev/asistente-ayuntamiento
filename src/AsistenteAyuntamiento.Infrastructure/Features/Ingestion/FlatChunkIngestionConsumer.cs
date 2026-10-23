using AsistenteAyuntamiento.Application.Features.Ingestion;
using AsistenteAyuntamiento.Infrastructure.Messaging;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using RabbitMQ.Client;

namespace AsistenteAyuntamiento.Infrastructure.Features.Ingestion;

/// <summary>
/// Consumes the queue of one flat-chunk variant and runs its <see cref="IDocumentIngestionService"/> (keyed by the
/// variant's pipeline type). One subclass per variant, since a Worker hosts exactly one consumer.
/// </summary>
public abstract class FlatChunkIngestionConsumer(
    FlatChunkVariant variant,
    IConnectionFactory connectionFactory,
    IServiceScopeFactory scopeFactory,
    RabbitMqConsumerHealth health,
    ILogger logger)
    : RabbitMqConsumerBase<DocumentMessage>(connectionFactory, scopeFactory, health, logger)
{
    protected override string ConsumerName => $"{variant.Pipeline.ToString().ToLowerInvariant()}-ingestion";

    protected override async Task<string> DeclareTopologyAsync(IChannel channel, CancellationToken cancellationToken)
    {
        var queue = RabbitMqTopology.QueueOf(variant.Pipeline);
        await RabbitMqTopology.DeclareWorkQueueAsync(channel, queue, cancellationToken);
        return queue;
    }

    protected override async Task HandleAsync(DocumentMessage message, IServiceProvider services, CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(message.BlobPath))
        {
            throw new NonRetryableMessageException("Message without blob_path.");
        }

        // The baseline is usually first to receive a scraped document: it records the hierarchical side as queued.
        await IngestionJobStates.MarkRequestedAsync(services, message, cancellationToken);
        await services.GetRequiredKeyedService<IDocumentIngestionService>(variant.Pipeline)
            .ProcessBlobAsync(message.BlobPath, message.Source, cancellationToken);
    }

    protected override Task OnDeadLetteredAsync(DocumentMessage? message, Exception exception, IServiceProvider services, CancellationToken cancellationToken) =>
        message is null
            ? Task.CompletedTask
            : IngestionJobStates.MarkFailedAsync(services, message.DocumentId, variant.Pipeline, exception, cancellationToken);
}

/// <summary>Consumes <c>documents_to_process_baseline</c> and runs the fixed-chunk pipeline.</summary>
public sealed class BaselineIngestionConsumer(
    IConnectionFactory connectionFactory,
    IServiceScopeFactory scopeFactory,
    RabbitMqConsumerHealth health,
    ILogger<BaselineIngestionConsumer> logger)
    : FlatChunkIngestionConsumer(FlatChunkVariant.Baseline, connectionFactory, scopeFactory, health, logger);

/// <summary>Consumes <c>documents_to_process_recursive</c> and runs the recursive flat-chunk variant.</summary>
public sealed class RecursiveIngestionConsumer(
    IConnectionFactory connectionFactory,
    IServiceScopeFactory scopeFactory,
    RabbitMqConsumerHealth health,
    ILogger<RecursiveIngestionConsumer> logger)
    : FlatChunkIngestionConsumer(FlatChunkVariant.Recursive, connectionFactory, scopeFactory, health, logger);

/// <summary>Consumes <c>documents_to_process_semantic</c> and runs the semantic flat-chunk variant.</summary>
public sealed class SemanticIngestionConsumer(
    IConnectionFactory connectionFactory,
    IServiceScopeFactory scopeFactory,
    RabbitMqConsumerHealth health,
    ILogger<SemanticIngestionConsumer> logger)
    : FlatChunkIngestionConsumer(FlatChunkVariant.Semantic, connectionFactory, scopeFactory, health, logger);
