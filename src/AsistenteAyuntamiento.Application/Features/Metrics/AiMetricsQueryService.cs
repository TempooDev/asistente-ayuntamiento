using AsistenteAyuntamiento.Application.Common.Interfaces;
using AsistenteAyuntamiento.Application.Features.Chat.Telemetry;
using AsistenteAyuntamiento.Application.Features.Metrics.DTOs;
using Microsoft.EntityFrameworkCore;

namespace AsistenteAyuntamiento.Application.Features.Metrics;

/// <summary>Aggregates in the database: no table is loaded into memory.</summary>
public sealed class AiMetricsQueryService(IAppDbContext dbContext) : IAiMetricsQueryService
{
    public const int DefaultPageSize = 50;
    public const int MaxPageSize = 100;

    public async Task<AiMetricsSummaryDto> GetSummaryAsync(CancellationToken cancellationToken = default)
    {
        // Grouped by model and step: answers are the calls users see; query expansion and history analysis are the
        // tenant's extra cost (their tokens count, but not as calls).
        var byModel = await dbContext.AiCallLogs
            .AsNoTracking()
            .GroupBy(c => new { c.ModelId, c.Operation })
            .Select(g => new
            {
                g.Key.ModelId,
                g.Key.Operation,
                Calls = g.Count(),
                Succeeded = g.Count(x => x.Success),
                AverageDurationMs = g.Average(x => x.DurationMs),
                InputTokens = g.Sum(x => (long)x.InputTokens),
                OutputTokens = g.Sum(x => (long)x.OutputTokens),
                TotalTokens = g.Sum(x => (long)x.TotalTokens)
            })
            .ToListAsync(cancellationToken);

        // Only the tenant's own calls: IngestionMetrics is platform-wide and is served by GetIngestionUsageAsync.
        var consumptions = byModel
            .Select(c => new ModelConsumptionDto(
                string.IsNullOrEmpty(c.ModelId) ? "Unknown" : c.ModelId, OperationLabel(c.Operation), c.Calls, c.Succeeded,
                Math.Round(c.AverageDurationMs, 2), c.TotalTokens))
            .OrderByDescending(c => c.TotalTokens)
            .ToList();

        var answers = byModel.Where(c => c.Operation == LlmOperations.Answer).ToList();
        var totalCalls = answers.Sum(c => (long)c.Calls);
        var succeededCalls = answers.Sum(c => (long)c.Succeeded);
        var failedCalls = totalCalls - succeededCalls;
        var inputTokens = byModel.Sum(c => c.InputTokens);
        var outputTokens = byModel.Sum(c => c.OutputTokens);
        var totalTokens = inputTokens + outputTokens;
        var answerDurationMs = answers.Sum(c => c.AverageDurationMs * c.Calls);

        return new AiMetricsSummaryDto(
            GeneratedAtUtc: DateTime.UtcNow,
            TotalCalls: totalCalls,
            SucceededCalls: succeededCalls,
            FailedCalls: failedCalls,
            SuccessRate: totalCalls > 0 ? Math.Round((double)succeededCalls / totalCalls * 100, 2) : 0,
            AverageDurationMs: totalCalls > 0 ? Math.Round(answerDurationMs / totalCalls, 2) : 0,
            TotalInputTokens: inputTokens,
            TotalOutputTokens: outputTokens,
            TotalTokens: totalTokens,
            AverageTokensPerCall: succeededCalls > 0 ? Math.Round((double)answers.Sum(c => c.TotalTokens) / succeededCalls, 1) : 0,
            Consumptions: consumptions);
    }

    /// <summary>Spanish label of an <see cref="LlmOperations"/> value, shown as the consumption type.</summary>
    internal static string OperationLabel(string operation) => operation switch
    {
        LlmOperations.Answer => "Respuesta",
        LlmOperations.QueryExpansion => "Expansión de consulta",
        LlmOperations.HistoryAnalysis => "Análisis de historial",
        _ => operation
    };

    public async Task<IngestionUsageDto> GetIngestionUsageAsync(CancellationToken cancellationToken = default)
    {
        var ingestion = await dbContext.IngestionMetrics
            .AsNoTracking()
            .GroupBy(_ => 1)
            .Select(g => new
            {
                Documents = g.LongCount(),
                EnrichmentCalls = g.Sum(x => (long)x.TotalLlmCalls),
                EnrichmentTokens = g.Sum(x => (long)x.TotalLlmTokens),
                EmbeddedTokens = g.Sum(x => (long)x.TotalTokensEmbedded),
                AverageDurationMs = g.Average(x => (double)x.ProcessingDurationMs)
            })
            .SingleOrDefaultAsync(cancellationToken);

        return ingestion is null
            ? new IngestionUsageDto(DateTime.UtcNow, 0, 0, 0, 0, 0)
            : new IngestionUsageDto(DateTime.UtcNow, ingestion.Documents, ingestion.EnrichmentCalls, ingestion.EnrichmentTokens,
                ingestion.EmbeddedTokens, Math.Round(ingestion.AverageDurationMs, 2));
    }

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
