using AsistenteAyuntamiento.Application.Features.Ingestion;

namespace AsistenteAyuntamiento.Application.Common.Messaging;

/// <summary>Which ingestion pipelines a document is queued for.</summary>
[Flags]
public enum IngestionPipelines
{
    None = 0,
    Baseline = 1,
    Hierarchical = 2,
    Both = Baseline | Hierarchical,
}

public static class IngestionPipelinesParser
{
    /// <summary>Parses the API's <c>pipelineMode</c> (BASELINE | HIERARCHICAL | BOTH, default BOTH).</summary>
    public static IngestionPipelines Parse(string? pipelineMode) => pipelineMode?.Trim().ToUpperInvariant() switch
    {
        "BASELINE" => IngestionPipelines.Baseline,
        "HIERARCHICAL" => IngestionPipelines.Hierarchical,
        _ => IngestionPipelines.Both,
    };
}

/// <summary>
/// Durable publishing of ingestion work (port; RabbitMQ implementation in Infrastructure).
/// A call only returns after the broker confirmed every message; otherwise it throws.
/// </summary>
public interface IIngestionQueuePublisher
{
    Task PublishAsync(DocumentMessage message, IngestionPipelines pipelines, CancellationToken cancellationToken = default);
}
