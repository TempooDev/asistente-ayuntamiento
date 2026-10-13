using AsistenteAyuntamiento.Application.Features.Chat;
using AsistenteAyuntamiento.Application.Features.Metrics;
using AsistenteAyuntamiento.ApiService.Security;

namespace AsistenteAyuntamiento.ApiService.Features.Chat;

public static class AiMetricsEndpoints
{
    public static void MapAiMetricsEndpoints(this IEndpointRouteBuilder app)
    {
        // The in-memory snapshot and health cover every tenant's calls: platform administrators only. The persisted
        // summary and history are filtered by tenant: organization administrators.
        var platform = app.MapGroup("/api/ai").RequireAuthorization(Policies.PlatformAdmin);
        var organization = app.MapGroup("/api/ai").RequireAuthorization(Policies.OrganizationAdmin);

        // GET /api/ai/metrics — Full metrics snapshot of all tenants (aggregates + recent calls)
        platform.MapGet("/metrics", (IAiMetricsService metricsService) =>
        {
            var snapshot = metricsService.GetSnapshot();
            return Results.Ok(snapshot);
        })
        .WithName("GetAiMetrics")
        .WithSummary("Returns AI model invocation metrics and recent call history");

        // GET /api/ai/metrics/summary — Persisted chat usage of the tenant, aggregated in SQL
        organization.MapGet("/metrics/summary", async (IAiMetricsQueryService metricsQuery, CancellationToken cancellationToken) =>
            Results.Ok(await metricsQuery.GetSummaryAsync(cancellationToken)))
        .WithName("GetAiMetricsSummary")
        .WithSummary("Returns a lightweight AI metrics summary without recent call details");

        // GET /api/ai/metrics/ingestion — Platform-wide ingestion cost (enrichment and embeddings), not tenant data
        platform.MapGet("/metrics/ingestion", async (IAiMetricsQueryService metricsQuery, CancellationToken cancellationToken) =>
            Results.Ok(await metricsQuery.GetIngestionUsageAsync(cancellationToken)))
        .WithName("GetIngestionUsage")
        .WithSummary("Returns the platform-wide ingestion cost: enrichment LLM calls and tokens, and embedded tokens");

        // GET /api/ai/health — Quick health check: is the AI model reachable?
        platform.MapGet("/health", (IAiMetricsService metricsService) =>
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
        organization.MapGet("/metrics/history", async (IAiMetricsQueryService metricsQuery, CancellationToken cancellationToken, int page = 1, int pageSize = AiMetricsQueryService.DefaultPageSize) =>
            Results.Ok(await metricsQuery.GetHistoryAsync(page, pageSize, cancellationToken)))
        .WithName("GetAiMetricsHistory")
        .WithSummary("Returns paginated historical AI metrics from the database");
    }
}
