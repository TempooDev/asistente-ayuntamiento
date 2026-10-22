namespace AsistenteAyuntamiento.Application.Features.Metrics.DTOs;

/// <summary>
/// Platform-wide ingestion in a period from <c>IngestionMetrics</c>, counting each document once per pipeline (its
/// latest ingestion): LLM enrichment (hierarchical pipeline) and embedded tokens. Bulletins are shared, so this is
/// platform data, never shown to organizations.
/// </summary>
public sealed record IngestionUsageDto(
    DateTime GeneratedAtUtc,
    DateOnly From,
    DateOnly To,
    long Documents,
    long EnrichmentCalls,
    long EnrichmentTokens,
    long EmbeddedTokens,
    double AverageProcessingDurationMs,
    IReadOnlyList<PipelineIngestionDto> Pipelines);

/// <summary>One pipeline's ingestion; <see cref="FailedDocuments"/> comes from the job states updated in the period.</summary>
public sealed record PipelineIngestionDto(
    string Pipeline,
    long Documents,
    double? ProcessingP50Ms,
    double? ProcessingP95Ms,
    double AverageChunks,
    long EmbeddedTokens,
    long EnrichmentCalls,
    long EnrichmentTokens,
    int FailedDocuments);
