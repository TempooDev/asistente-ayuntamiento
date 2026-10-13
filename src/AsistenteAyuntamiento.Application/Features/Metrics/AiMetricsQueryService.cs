using AsistenteAyuntamiento.Application.Common.Interfaces;
using AsistenteAyuntamiento.Application.Features.Metrics.DTOs;
using Microsoft.EntityFrameworkCore;

namespace AsistenteAyuntamiento.Application.Features.Metrics;

/// <summary>Aggregates in the database: no table is loaded into memory.</summary>
public sealed class AiMetricsQueryService(IAppDbContext dbContext) : IAiMetricsQueryService
{
    public const int DefaultPageSize = 50;
    public const int MaxPageSize = 100;
    private const string IngestionModelId = "Ingestion Worker";

    public async Task<AiMetricsSummaryDto> GetSummaryAsync(CancellationToken cancellationToken = default)
    {
        var chatByModel = await dbContext.AiCallLogs
            .AsNoTracking()
            .GroupBy(c => c.ModelId)
            .Select(g => new
            {
                ModelId = g.Key,
                Calls = g.Count(),
                Succeeded = g.Count(x => x.Success),
                AverageDurationMs = g.Average(x => x.DurationMs),
                InputTokens = g.Sum(x => (long)x.InputTokens),
                OutputTokens = g.Sum(x => (long)x.OutputTokens),
                TotalTokens = g.Sum(x => (long)x.TotalTokens)
            })
            .ToListAsync(cancellationToken);

        var ingestion = await dbContext.IngestionMetrics
            .AsNoTracking()
            .GroupBy(_ => 1)
            .Select(g => new
            {
                LlmCalls = g.Sum(x => (long)x.TotalLlmCalls),
                AverageDurationMs = g.Average(x => (double)x.ProcessingDurationMs),
                EmbeddedTokens = g.Sum(x => (long)x.TotalTokensEmbedded),
                LlmTokens = g.Sum(x => (long)x.TotalLlmTokens)
            })
            .SingleOrDefaultAsync(cancellationToken);

        var consumptions = chatByModel
            .Select(c => new ModelConsumptionDto(
                string.IsNullOrEmpty(c.ModelId) ? "Unknown" : c.ModelId, "Chat", c.Calls, c.Succeeded, Math.Round(c.AverageDurationMs, 2), c.TotalTokens))
            .ToList();
        if (ingestion is not null)
        {
            consumptions.Add(new ModelConsumptionDto(
                IngestionModelId, "Embeddings", (int)ingestion.LlmCalls, (int)ingestion.LlmCalls, Math.Round(ingestion.AverageDurationMs, 2), ingestion.EmbeddedTokens + ingestion.LlmTokens));
        }
        consumptions = consumptions.OrderByDescending(c => c.TotalTokens).ToList();

        // Ingestion only records successful LLM calls.
        var ingestionCalls = ingestion?.LlmCalls ?? 0;
        var totalCalls = chatByModel.Sum(c => (long)c.Calls) + ingestionCalls;
        var succeededCalls = chatByModel.Sum(c => (long)c.Succeeded) + ingestionCalls;
        var failedCalls = chatByModel.Sum(c => (long)(c.Calls - c.Succeeded));
        var inputTokens = chatByModel.Sum(c => c.InputTokens) + (ingestion?.EmbeddedTokens ?? 0);
        var outputTokens = chatByModel.Sum(c => c.OutputTokens) + (ingestion?.LlmTokens ?? 0);
        var totalTokens = inputTokens + outputTokens;

        return new AiMetricsSummaryDto(
            GeneratedAtUtc: DateTime.UtcNow,
            TotalCalls: totalCalls,
            SucceededCalls: succeededCalls,
            FailedCalls: failedCalls,
            SuccessRate: totalCalls > 0 ? Math.Round((double)succeededCalls / totalCalls * 100, 2) : 0,
            AverageDurationMs: consumptions.Count > 0 ? Math.Round(consumptions.Average(c => c.AverageDurationMs), 2) : 0,
            TotalInputTokens: inputTokens,
            TotalOutputTokens: outputTokens,
            TotalTokens: totalTokens,
            AverageTokensPerCall: succeededCalls > 0 ? Math.Round((double)totalTokens / succeededCalls, 1) : 0,
            Consumptions: consumptions);
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
