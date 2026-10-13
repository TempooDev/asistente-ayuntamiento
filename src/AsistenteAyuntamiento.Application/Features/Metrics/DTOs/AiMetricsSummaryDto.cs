namespace AsistenteAyuntamiento.Application.Features.Metrics.DTOs;

/// <summary>Persisted chat usage of the current tenant (<c>AiCallLogs</c>).</summary>
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
