namespace AsistenteAyuntamiento.Application.Features.Metrics.DTOs;

/// <summary>
/// Persisted chat usage of the current tenant (<c>AiCallLogs</c>). Call counts, success rate, average duration and
/// tokens per call cover answers only; the token totals also include query expansion and history analysis.
/// </summary>
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
