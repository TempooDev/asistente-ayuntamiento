using AsistenteAyuntamiento.Application.Features.Chat;
using AsistenteAyuntamiento.Application.Features.Metrics;
using AsistenteAyuntamiento.ApiService.Security;

namespace AsistenteAyuntamiento.ApiService.Features.Chat;

public static class AiMetricsEndpoints
{
    public static void MapAiMetricsEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/ai").RequireAuthorization(Policies.Admin);

        // GET /api/ai/metrics — Full metrics snapshot (aggregates + recent calls)
        group.MapGet("/metrics", (IAiMetricsService metricsService) =>
        {
            var snapshot = metricsService.GetSnapshot();
            return Results.Ok(snapshot);
        })
        .WithName("GetAiMetrics")
        .WithSummary("Returns AI model invocation metrics and recent call history");

        // GET /api/ai/metrics/summary — Persisted usage of the tenant (chat) and of ingestion, aggregated in SQL
        group.MapGet("/metrics/summary", async (IAiMetricsQueryService metricsQuery, CancellationToken cancellationToken) =>
            Results.Ok(await metricsQuery.GetSummaryAsync(cancellationToken)))
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

        // GET /api/ai/metrics/history — Paginated call log of the tenant
        group.MapGet("/metrics/history", async (IAiMetricsQueryService metricsQuery, CancellationToken cancellationToken, int page = 1, int pageSize = AiMetricsQueryService.DefaultPageSize) =>
            Results.Ok(await metricsQuery.GetHistoryAsync(page, pageSize, cancellationToken)))
        .WithName("GetAiMetricsHistory")
        .WithSummary("Returns paginated historical AI metrics from the database");
    }
}
