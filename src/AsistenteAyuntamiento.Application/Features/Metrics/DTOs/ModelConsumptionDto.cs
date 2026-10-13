namespace AsistenteAyuntamiento.Application.Features.Metrics.DTOs;

/// <summary>Usage of one chat model, or of the ingestion worker's embeddings and enrichment.</summary>
public sealed record ModelConsumptionDto(
    string ModelId,
    string Type,
    int TotalCalls,
    int SucceededCalls,
    double AverageDurationMs,
    long TotalTokens);
