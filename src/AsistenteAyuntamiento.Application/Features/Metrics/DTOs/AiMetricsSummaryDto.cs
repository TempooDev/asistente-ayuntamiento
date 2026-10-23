namespace AsistenteAyuntamiento.Application.Features.Metrics.DTOs;

/// <summary>
/// Persisted chat usage of the current tenant (<c>AiCallLogs</c>) in a period. Call counts, success rate, latency and
/// tokens per call cover answers only; token totals and cost also include query expansion and history analysis.
/// Calls of the evaluation runner are left out.
/// </summary>
public sealed record AiMetricsSummaryDto(
    DateTime GeneratedAtUtc,
    DateOnly From,
    DateOnly To,
    long TotalCalls,
    long SucceededCalls,
    long FailedCalls,
    double SuccessRate,
    double AverageDurationMs,
    double? LatencyP50Ms,
    double? LatencyP95Ms,
    double? TtftP50Ms,
    long TotalInputTokens,
    long TotalOutputTokens,
    long TotalTokens,
    double AverageTokensPerCall,
    decimal EstimatedCostEur,
    IReadOnlyList<string> UnpricedModels,
    IReadOnlyList<StepCostDto> CostByStep,
    IReadOnlyList<DailyUsageDto> Daily,
    IReadOnlyList<PipelineUsageDto> Pipelines,
    IReadOnlyList<ModelConsumptionDto> Consumptions);

public sealed record StepCostDto(string Step, long Tokens, decimal CostEur);

/// <summary>One UTC day of the period; <see cref="LatencyP95Ms"/> is null on days without successful answers.</summary>
public sealed record DailyUsageDto(DateOnly Date, int Answers, int Failed, double? LatencyP95Ms);

/// <summary>Answers of one pipeline (<c>baseline</c>, <c>hierarchical</c>, <c>arena</c>, <c>shadow</c>).</summary>
public sealed record PipelineUsageDto(
    string Pipeline,
    int Answers,
    double SuccessRate,
    double? LatencyP50Ms,
    double? LatencyP95Ms,
    double? TtftP50Ms,
    double AverageTokensPerAnswer,
    decimal CostPer1000AnswersEur);
