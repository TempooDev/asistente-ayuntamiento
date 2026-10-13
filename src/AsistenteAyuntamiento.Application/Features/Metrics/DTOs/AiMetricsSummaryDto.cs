namespace AsistenteAyuntamiento.Application.Features.Metrics.DTOs;

/// <summary>Persisted AI usage of the current tenant (chat) plus platform ingestion.</summary>
public sealed record AiMetricsSummaryDto(
    DateTime GeneratedAtUtc,
    long TotalCalls,
    long SucceededCalls,
    long FailedCalls,
    double SuccessRate,
    double AverageDurationMs,
    long TotalInputTokens,
    long TotalOutputTokens,
    long TotalTokens,
    double AverageTokensPerCall,
    IReadOnlyList<ModelConsumptionDto> Consumptions);
