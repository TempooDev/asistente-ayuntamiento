namespace AsistenteAyuntamiento.Application.Features.Metrics.DTOs;

/// <summary>
/// Platform-wide ingestion cost from <c>IngestionMetrics</c>: LLM enrichment calls and tokens (hierarchical pipeline)
/// and embedded tokens (both pipelines). Bulletins are shared, so this is platform data, never shown to organizations.
/// </summary>
public sealed record IngestionUsageDto(
    DateTime GeneratedAtUtc,
    long Documents,
    long EnrichmentCalls,
    long EnrichmentTokens,
    long EmbeddedTokens,
    double AverageProcessingDurationMs);
