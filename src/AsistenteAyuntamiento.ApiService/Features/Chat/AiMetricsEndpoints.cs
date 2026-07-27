using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace AsistenteAyuntamiento.ApiService.Features.Chat;

public static class AiMetricsEndpoints
{
    public static void MapAiMetricsEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/ai");

        // GET /api/ai/metrics — Full metrics snapshot (aggregates + recent calls)
        group.MapGet("/metrics", (AiMetricsService metricsService) =>
        {
            var snapshot = metricsService.GetSnapshot();
            return Results.Ok(snapshot);
        })
        .WithName("GetAiMetrics")
        .WithSummary("Returns AI model invocation metrics and recent call history")
        .WithOpenApi();

        // GET /api/ai/metrics/summary — Lightweight summary (no recent calls list)
        group.MapGet("/metrics/summary", (AiMetricsService metricsService) =>
        {
            var snapshot = metricsService.GetSnapshot();
            return Results.Ok(new
            {
                snapshot.GeneratedAtUtc,
                snapshot.TotalCalls,
                snapshot.SucceededCalls,
                snapshot.FailedCalls,
                snapshot.SuccessRate,
                snapshot.AverageDurationMs,
                snapshot.TotalInputTokens,
                snapshot.TotalOutputTokens,
                snapshot.TotalTokens,
                snapshot.AverageTokensPerCall
            });
        })
        .WithName("GetAiMetricsSummary")
        .WithSummary("Returns a lightweight AI metrics summary without recent call details")
        .WithOpenApi();

        // GET /api/ai/health — Quick health check: is the AI model reachable?
        group.MapGet("/health", (AiMetricsService metricsService) =>
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
        .WithSummary("Returns AI model health status based on recent call success rate")
        .WithOpenApi();
    }
}
