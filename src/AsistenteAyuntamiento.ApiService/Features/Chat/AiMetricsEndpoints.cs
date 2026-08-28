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
        .WithSummary("Returns AI model invocation metrics and recent call history");

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
        .WithSummary("Returns a lightweight AI metrics summary without recent call details");

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
        .WithSummary("Returns AI model health status based on recent call success rate");

        // GET /api/ai/metrics/history — Fetch paginated historical call logs from the database
        group.MapGet("/metrics/history", async (
            AsistenteAyuntamiento.ApiService.Infrastructure.Data.AppDbContext dbContext,
            System.Security.Claims.ClaimsPrincipal user,
            int page = 1, 
            int pageSize = 50) =>
        {
            // Note: Since this is an admin panel or for user history, you'd typically filter by TenantId
            // which is handled via AppDbContext QueryFilters automatically, but we can also filter by UserId if needed.
            if (page < 1) page = 1;
            if (pageSize < 1 || pageSize > 100) pageSize = 50;

            var query = dbContext.AiCallLogs.AsQueryable();

            var totalItems = await Microsoft.EntityFrameworkCore.EntityFrameworkQueryableExtensions.CountAsync(query);
            var items = await Microsoft.EntityFrameworkCore.EntityFrameworkQueryableExtensions.ToListAsync(
                query.OrderByDescending(l => l.CreatedAt)
                     .Skip((page - 1) * pageSize)
                     .Take(pageSize)
            );

            return Results.Ok(new { TotalItems = totalItems, Page = page, PageSize = pageSize, Items = items });
        })
        .WithName("GetAiMetricsHistory")
        .WithSummary("Returns paginated historical AI metrics from the database");
    }
}
