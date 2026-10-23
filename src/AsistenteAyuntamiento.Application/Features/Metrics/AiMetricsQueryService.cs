using AsistenteAyuntamiento.Application.Common.Interfaces;
using AsistenteAyuntamiento.Application.Features.Chat.Telemetry;
using AsistenteAyuntamiento.Application.Features.Metrics.DTOs;
using AsistenteAyuntamiento.Domain.Common.Enums;
using AsistenteAyuntamiento.Domain.Features.Ingestion;
using Microsoft.EntityFrameworkCore;

namespace AsistenteAyuntamiento.Application.Features.Metrics;

/// <summary>
/// Counts and sums are aggregated in the database. Percentiles are computed here from the numeric columns of the
/// period only (durations), which the period limit (<see cref="MetricsPeriod.MaxDays"/>) keeps small.
/// </summary>
public sealed class AiMetricsQueryService(IAppDbContext dbContext, ILlmUsageMetrics usageMetrics) : IAiMetricsQueryService
{
    public const int DefaultPageSize = 50;
    public const int MaxPageSize = 100;

    public async Task<AiMetricsSummaryDto> GetSummaryAsync(MetricsPeriod period, CancellationToken cancellationToken = default)
    {
        var calls = dbContext.AiCallLogs.AsNoTracking()
            .Where(c => c.CreatedAt >= period.From && c.CreatedAt < period.To && c.Pipeline != LlmPipelines.Evaluation);

        // Grouped by model, step, provider and pipeline: enough to price every call and to split by step and pipeline.
        var groups = await calls
            .GroupBy(c => new { c.ModelId, c.Operation, c.Provider, c.Pipeline })
            .Select(g => new CallGroup(
                g.Key.ModelId,
                g.Key.Operation,
                g.Key.Provider,
                g.Key.Pipeline,
                g.Count(),
                g.Count(x => x.Success),
                g.Sum(x => x.DurationMs),
                g.Sum(x => (long)x.InputTokens),
                g.Sum(x => (long)x.OutputTokens),
                g.Sum(x => (long)x.TotalTokens)))
            .ToListAsync(cancellationToken);

        // Successful answers only: a failure's duration is the time to the error, not an answer latency.
        var answers = await calls
            .Where(c => c.Operation == LlmOperations.Answer)
            .Select(c => new AnswerSample(c.CreatedAt, c.Pipeline, c.Success, c.DurationMs, c.TimeToFirstTokenMs))
            .ToListAsync(cancellationToken);

        var unpriced = new HashSet<string>();
        var priced = groups.Select(g =>
        {
            var cost = usageMetrics.TryEstimateCost(g.Provider, g.ModelId, g.InputTokens, g.OutputTokens);
            if (cost is null)
            {
                unpriced.Add(string.IsNullOrEmpty(g.ModelId) ? "Unknown" : g.ModelId);
            }
            return (Group: g, Cost: cost ?? 0m);
        }).ToList();

        var consumptions = priced
            .GroupBy(p => new { p.Group.ModelId, p.Group.Operation })
            .Select(m =>
            {
                var calls = m.Sum(p => p.Group.Calls);
                return new ModelConsumptionDto(
                    string.IsNullOrEmpty(m.Key.ModelId) ? "Unknown" : m.Key.ModelId, OperationLabel(m.Key.Operation), calls,
                    m.Sum(p => p.Group.Succeeded), Math.Round(m.Sum(p => p.Group.DurationMs) / calls, 2), m.Sum(p => p.Group.TotalTokens),
                    Math.Round(m.Sum(p => p.Cost), 6));
            })
            .OrderByDescending(c => c.TotalTokens)
            .ToList();

        var answerGroups = priced.Where(p => p.Group.Operation == LlmOperations.Answer).ToList();
        var totalCalls = answerGroups.Sum(p => (long)p.Group.Calls);
        var succeededCalls = answerGroups.Sum(p => (long)p.Group.Succeeded);
        var inputTokens = groups.Sum(g => g.InputTokens);
        var outputTokens = groups.Sum(g => g.OutputTokens);
        var successful = answers.Where(a => a.Success).ToList();

        return new AiMetricsSummaryDto(
            GeneratedAtUtc: DateTime.UtcNow,
            From: DateOnly.FromDateTime(period.From),
            To: DateOnly.FromDateTime(period.To),
            TotalCalls: totalCalls,
            SucceededCalls: succeededCalls,
            FailedCalls: totalCalls - succeededCalls,
            SuccessRate: Rate(succeededCalls, totalCalls),
            AverageDurationMs: totalCalls > 0 ? Math.Round(answerGroups.Sum(p => p.Group.DurationMs) / totalCalls, 2) : 0,
            LatencyP50Ms: Percentiles.Of(successful.Select(a => a.DurationMs), 0.5),
            LatencyP95Ms: Percentiles.Of(successful.Select(a => a.DurationMs), 0.95),
            TtftP50Ms: Percentiles.Of(successful.Where(a => a.TtftMs is not null).Select(a => a.TtftMs!.Value), 0.5),
            TotalInputTokens: inputTokens,
            TotalOutputTokens: outputTokens,
            TotalTokens: inputTokens + outputTokens,
            AverageTokensPerCall: succeededCalls > 0 ? Math.Round((double)answerGroups.Sum(p => p.Group.TotalTokens) / succeededCalls, 1) : 0,
            EstimatedCostEur: Math.Round(priced.Sum(p => p.Cost), 6),
            UnpricedModels: [.. unpriced.Order()],
            CostByStep: [.. priced
                .GroupBy(p => p.Group.Operation)
                .Select(s => new StepCostDto(OperationLabel(s.Key), s.Sum(p => p.Group.TotalTokens), Math.Round(s.Sum(p => p.Cost), 6)))
                .OrderByDescending(s => s.Tokens)],
            Daily: Daily(period, answers),
            Pipelines: PipelineRows(answerGroups, answers),
            Consumptions: consumptions);
    }

    /// <summary>One entry per day of the period, so days without answers show as gaps instead of disappearing.</summary>
    private static List<DailyUsageDto> Daily(MetricsPeriod period, List<AnswerSample> answers)
    {
        var byDay = answers.ToLookup(a => DateOnly.FromDateTime(a.CreatedAt));
        return Enumerable.Range(0, period.Days)
            .Select(offset =>
            {
                var day = DateOnly.FromDateTime(period.From).AddDays(offset);
                var samples = byDay[day].ToList();
                return new DailyUsageDto(day, samples.Count, samples.Count(a => !a.Success),
                    Percentiles.Of(samples.Where(a => a.Success).Select(a => a.DurationMs), 0.95));
            })
            .ToList();
    }

    private static List<PipelineUsageDto> PipelineRows(List<(CallGroup Group, decimal Cost)> answerGroups, List<AnswerSample> answers)
    {
        var samples = answers.Where(a => a.Success).ToLookup(a => PipelineLabel(a.Pipeline));
        return answerGroups
            .GroupBy(p => PipelineLabel(p.Group.Pipeline))
            .Select(g =>
            {
                var calls = g.Sum(p => p.Group.Calls);
                var succeeded = g.Sum(p => p.Group.Succeeded);
                var durations = samples[g.Key].Select(a => a.DurationMs).Order().ToList();
                return new PipelineUsageDto(
                    g.Key,
                    calls,
                    Rate(succeeded, calls),
                    Percentiles.Of(durations, 0.5),
                    Percentiles.Of(durations, 0.95),
                    Percentiles.Of(samples[g.Key].Where(a => a.TtftMs is not null).Select(a => a.TtftMs!.Value), 0.5),
                    succeeded > 0 ? Math.Round((double)g.Sum(p => p.Group.TotalTokens) / succeeded, 1) : 0,
                    calls > 0 ? Math.Round(g.Sum(p => p.Cost) / calls * 1000, 4) : 0);
            })
            .OrderByDescending(r => r.Answers)
            .ToList();
    }

    /// <summary>
    /// The pipeline a call is shown under. Arena sides are merged: a side is a random slot, not a RAG pipeline. Rows
    /// written before the pipeline was recorded were baseline chat answers.
    /// </summary>
    internal static string PipelineLabel(string? pipeline) => pipeline switch
    {
        null or "" or LlmPipelines.Baseline => LlmPipelines.Baseline,
        LlmPipelines.ArenaAlfa or LlmPipelines.ArenaBeta => "arena",
        _ => pipeline
    };

    /// <summary>Spanish label of an <see cref="LlmOperations"/> value, shown as the consumption type.</summary>
    internal static string OperationLabel(string operation) => operation switch
    {
        LlmOperations.Answer => "Respuesta",
        LlmOperations.QueryExpansion => "Expansión de consulta",
        LlmOperations.HistoryAnalysis => "Análisis de historial",
        _ => operation
    };

    public async Task<IngestionUsageDto> GetIngestionUsageAsync(MetricsPeriod period, CancellationToken cancellationToken = default)
    {
        // The latest ingestion of each document per pipeline: a re-ingested document counts once.
        var inPeriod = dbContext.IngestionMetrics.AsNoTracking()
            .Where(m => m.CreatedAt >= period.From && m.CreatedAt < period.To);
        var latestIds = inPeriod.GroupBy(m => new { m.DocumentId, m.Pipeline }).Select(g => g.Max(m => m.Id));
        var latest = await inPeriod
            .Where(m => latestIds.Contains(m.Id))
            .Select(m => new { m.Pipeline, m.ProcessingDurationMs, m.ChunksGenerated, m.TotalTokensEmbedded, m.TotalLlmCalls, m.TotalLlmTokens })
            .ToListAsync(cancellationToken);

        var failed = await dbContext.DocumentJobStates.AsNoTracking()
            .Where(j => j.LastUpdatedAt >= period.From && j.LastUpdatedAt < period.To)
            .GroupBy(_ => 1)
            .Select(g => new
            {
                Baseline = g.Count(j => j.BaselineStatus == JobStatus.Failed || j.BaselineStatus == JobStatus.VectorSyncFailed),
                Hierarchical = g.Count(j => j.HierarchicalStatus == JobStatus.Failed || j.HierarchicalStatus == JobStatus.VectorSyncFailed),
                Recursive = g.Count(j => j.RecursiveStatus == JobStatus.Failed || j.RecursiveStatus == JobStatus.VectorSyncFailed),
                Semantic = g.Count(j => j.SemanticStatus == JobStatus.Failed || j.SemanticStatus == JobStatus.VectorSyncFailed)
            })
            .SingleOrDefaultAsync(cancellationToken);

        int FailedOf(PipelineType pipeline) => pipeline switch
        {
            PipelineType.Hierarchical => failed?.Hierarchical ?? 0,
            PipelineType.Recursive => failed?.Recursive ?? 0,
            PipelineType.Semantic => failed?.Semantic ?? 0,
            _ => failed?.Baseline ?? 0,
        };

        // The four variants of the chunking study, always in the same order (the page hides the empty study rows).
        var pipelines = Enum.GetValues<PipelineType>()
            .Select(pipeline =>
            {
                var rows = latest.Where(m => m.Pipeline == pipeline).ToList();
                var durations = rows.Select(m => (double)m.ProcessingDurationMs).ToList();
                return new PipelineIngestionDto(
                    pipeline.ToString(),
                    rows.Count,
                    Percentiles.Of(durations, 0.5),
                    Percentiles.Of(durations, 0.95),
                    rows.Count > 0 ? Math.Round(rows.Average(m => m.ChunksGenerated), 2) : 0,
                    rows.Sum(m => (long)m.TotalTokensEmbedded),
                    rows.Sum(m => (long)m.TotalLlmCalls),
                    rows.Sum(m => (long)m.TotalLlmTokens),
                    FailedOf(pipeline));
            })
            .ToList();

        return new IngestionUsageDto(
            DateTime.UtcNow,
            DateOnly.FromDateTime(period.From),
            DateOnly.FromDateTime(period.To),
            latest.Count,
            latest.Sum(m => (long)m.TotalLlmCalls),
            latest.Sum(m => (long)m.TotalLlmTokens),
            latest.Sum(m => (long)m.TotalTokensEmbedded),
            latest.Count > 0 ? Math.Round(latest.Average(m => (double)m.ProcessingDurationMs), 2) : 0,
            pipelines);
    }

    private static double Rate(long part, long total) => total > 0 ? Math.Round((double)part / total * 100, 2) : 0;

    private sealed record CallGroup(string ModelId, string Operation, string? Provider, string? Pipeline, int Calls, int Succeeded,
        double DurationMs, long InputTokens, long OutputTokens, long TotalTokens);

    private sealed record AnswerSample(DateTime CreatedAt, string? Pipeline, bool Success, double DurationMs, double? TtftMs);

    public async Task<AiCallLogPageDto> GetHistoryAsync(int page, int pageSize, CancellationToken cancellationToken = default)
    {
        page = Math.Max(page, 1);
        pageSize = pageSize is < 1 or > MaxPageSize ? DefaultPageSize : pageSize;

        var query = dbContext.AiCallLogs.AsNoTracking();
        var totalItems = await query.CountAsync(cancellationToken);
        var items = await query
            .OrderByDescending(l => l.CreatedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(cancellationToken);

        return new AiCallLogPageDto(totalItems, page, pageSize, items);
    }
}
