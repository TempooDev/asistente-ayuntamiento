using AsistenteAyuntamiento.Application.Features.Chat;
using AsistenteAyuntamiento.Application.Common.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace AsistenteAyuntamiento.ApiService.Features.Chat;

public static class AiMetricsEndpoints
{
    public static void MapAiMetricsEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/ai").RequireAuthorization();

        // GET /api/ai/metrics — Full metrics snapshot (aggregates + recent calls)
        group.MapGet("/metrics", (IAiMetricsService metricsService) =>
        {
            var snapshot = metricsService.GetSnapshot();
            return Results.Ok(snapshot);
        })
        .WithName("GetAiMetrics")
        .WithSummary("Returns AI model invocation metrics and recent call history");

        // GET /api/ai/metrics/summary — Lightweight summary (no recent calls list)
        group.MapGet("/metrics/summary", async (IAppDbContext dbContext) =>
        {
            var chatLogs = await Microsoft.EntityFrameworkCore.EntityFrameworkQueryableExtensions.ToListAsync(dbContext.AiCallLogs.AsNoTracking());
            var ingestionLogs = await Microsoft.EntityFrameworkCore.EntityFrameworkQueryableExtensions.ToListAsync(dbContext.IngestionMetrics.AsNoTracking());

            var chatConsumptions = chatLogs.GroupBy(c => c.ModelId).Select(g => new
            {
                ModelId = string.IsNullOrEmpty(g.Key) ? "Unknown" : g.Key,
                Type = "Chat",
                TotalCalls = g.Count(),
                SucceededCalls = g.Count(x => x.Success),
                AverageDurationMs = g.Any() ? Math.Round(g.Average(x => x.DurationMs), 2) : 0,
                TotalTokens = g.Sum(x => x.TotalTokens)
            });

            var embeddingsConsumptions = ingestionLogs.GroupBy(c => "Ingestion Worker").Select(g => new
            {
                ModelId = g.Key,
                Type = "Embeddings",
                TotalCalls = g.Sum(x => x.TotalLlmCalls),
                SucceededCalls = g.Sum(x => x.TotalLlmCalls),
                AverageDurationMs = g.Any() ? Math.Round(g.Average(x => x.ProcessingDurationMs), 2) : 0,
                TotalTokens = g.Sum(x => x.TotalTokensEmbedded + x.TotalLlmTokens)
            });

            var consumptions = chatConsumptions.Concat(embeddingsConsumptions).OrderByDescending(c => c.TotalTokens).ToList();

            var totalCalls = chatLogs.Count + ingestionLogs.Sum(i => i.TotalLlmCalls);
            var succeededCalls = chatLogs.Count(x => x.Success) + ingestionLogs.Sum(i => i.TotalLlmCalls);
            var failedCalls = chatLogs.Count(x => !x.Success);
            
            var totalInputTokens = chatLogs.Sum(x => x.InputTokens) + ingestionLogs.Sum(i => i.TotalTokensEmbedded);
            var totalOutputTokens = chatLogs.Sum(x => x.OutputTokens) + ingestionLogs.Sum(i => i.TotalLlmTokens);
            var totalTokens = totalInputTokens + totalOutputTokens;

            return Results.Ok(new
            {
                GeneratedAtUtc = DateTime.UtcNow,
                TotalCalls = totalCalls,
                SucceededCalls = succeededCalls,
                FailedCalls = failedCalls,
                SuccessRate = totalCalls > 0 ? Math.Round((double)succeededCalls / totalCalls * 100, 2) : 0,
                AverageDurationMs = consumptions.Any() ? Math.Round(consumptions.Average(c => c.AverageDurationMs), 2) : 0,
                TotalInputTokens = totalInputTokens,
                TotalOutputTokens = totalOutputTokens,
                TotalTokens = totalTokens,
                AverageTokensPerCall = succeededCalls > 0 ? Math.Round((double)totalTokens / succeededCalls, 1) : 0,
                Consumptions = consumptions
            });
        })
        .WithName("GetAiMetricsSummary")
        .WithSummary("Returns a lightweight AI metrics summary without recent call details");

        // GET /api/ai/health — Quick health check: is the AI model reachable?
        group.MapGet("/health", (IAiMetricsService metricsService) =>
        {
            var snapshot = metricsService.GetSnapshot();

            // Consider unhealthy if more than 50% of recent calls failed (min 3 calls)
            var isHealthy = snapshot.TotalCalls < 3 || snapshot.SuccessRate >= 50;
            var status = isHealthy ? "healthy" : "degraded";

            return Results.Ok(new
            {
                Status = status,
                snapshot.TotalCalls,
                snapshot.SuccessRate,
                snapshot.AverageDurationMs,
                LastCallAt = snapshot.RecentCalls.FirstOrDefault()?.Timestamp
            });
        })
        .WithName("GetAiHealth")
        .WithSummary("Returns AI model health status based on recent call success rate");

        // GET /api/ai/metrics/history — Fetch paginated historical call logs from the database
        group.MapGet("/metrics/history", async (
            IAppDbContext dbContext,
            System.Security.Claims.ClaimsPrincipal user,
            int page = 1,
            int pageSize = 50) =>
        {
            // Note: Since this is an admin panel or for user history, you'd typically filter by TenantId
            // which is handled via IAppDbContext QueryFilters automatically, but we can also filter by UserId if needed.
            if (page < 1) page = 1;
            if (pageSize < 1 || pageSize > 100) pageSize = 50;

            var query = dbContext.AiCallLogs.AsQueryable();

            var totalItems = await Microsoft.EntityFrameworkCore.EntityFrameworkQueryableExtensions.CountAsync(query);
            var items = await Microsoft.EntityFrameworkCore.EntityFrameworkQueryableExtensions.ToListAsync(
                query.AsNoTracking().OrderByDescending(l => l.CreatedAt)
                     .Skip((page - 1) * pageSize)
                     .Take(pageSize)
            );

            return Results.Ok(new { TotalItems = totalItems, Page = page, PageSize = pageSize, Items = items });
        })
        .WithName("GetAiMetricsHistory")
        .WithSummary("Returns paginated historical AI metrics from the database");
    }
}
