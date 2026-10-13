using AsistenteAyuntamiento.Application.Common.Interfaces;
using AsistenteAyuntamiento.Domain.Common.Enums;
using AsistenteAyuntamiento.Domain.Features.Ingestion;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace AsistenteAyuntamiento.Application.Features.Ingestion;

/// <summary>
/// Saves each metric in its own scope (its own <see cref="IAppDbContext"/>), so a failed save never leaves a
/// tracked entity behind in the ingestion's unit of work, and the ingestion's pending changes are never flushed here.
/// </summary>
public class IngestionMetricsService(IServiceScopeFactory scopeFactory, ILogger<IngestionMetricsService> logger) : IIngestionMetricsService
{
    private readonly IServiceScopeFactory _scopeFactory = scopeFactory;
    private readonly ILogger<IngestionMetricsService> _logger = logger;

    public async Task TrackIngestionAsync(PipelineType pipeline, BulletinType bulletin, string documentId, int tokensEmbedded, int llmCalls, int llmTokens, int chunksGenerated, long processingDurationMs, CancellationToken cancellationToken)
    {
        try
        {
            await using var scope = _scopeFactory.CreateAsyncScope();
            var dbContext = scope.ServiceProvider.GetRequiredService<IAppDbContext>();

            var metric = new IngestionMetric
            {
                Pipeline = pipeline,
                Bulletin = bulletin,
                DocumentId = documentId,
                TotalTokensEmbedded = tokensEmbedded,
                TotalLlmCalls = llmCalls,
                TotalLlmTokens = llmTokens,
                ChunksGenerated = chunksGenerated,
                ProcessingDurationMs = processingDurationMs,
                CreatedAt = DateTime.UtcNow
            };

            dbContext.IngestionMetrics.Add(metric);
            await dbContext.SaveChangesAsync(cancellationToken);

            _logger.LogInformation("Guardada métrica de ingesta para {DocumentId} en pipeline {Pipeline} ({Duration}ms).", documentId, pipeline, metric.ProcessingDurationMs);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error al guardar la métrica de ingesta para el documento {DocumentId}.", documentId);
        }
    }
}


