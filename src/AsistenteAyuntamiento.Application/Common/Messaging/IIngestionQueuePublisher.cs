using AsistenteAyuntamiento.Application.Features.Ingestion;

namespace AsistenteAyuntamiento.Application.Common.Messaging;

/// <summary>
/// Durable publishing of ingestion work (port; RabbitMQ implementation in Infrastructure).
/// A call only returns after the broker confirmed every message; otherwise it throws.
/// </summary>
public interface IIngestionQueuePublisher
{
    Task PublishAsync(DocumentMessage message, IngestionPipelines pipelines, CancellationToken cancellationToken = default);
}
