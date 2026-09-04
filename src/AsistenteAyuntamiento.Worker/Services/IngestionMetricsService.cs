using System.Diagnostics;
using AsistenteAyuntamiento.Domain.Common.Enums;
using AsistenteAyuntamiento.Domain.Features.Ingestion;
using AsistenteAyuntamiento.Infrastructure.Data;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace AsistenteAyuntamiento.Worker.Services;

public class IngestionMetricsService(IServiceProvider serviceProvider, ILogger<IngestionMetricsService> logger) : IIngestionMetricsService
{
    private readonly IServiceProvider _serviceProvider = serviceProvider;
    private readonly ILogger<IngestionMetricsService> _logger = logger;

    public async Task TrackIngestionAsync(PipelineType pipeline, BulletinType bulletin, string documentId, int tokensEmbedded, int llmCalls, int llmTokens, int chunksGenerated, long processingDurationMs, CancellationToken cancellationToken)
    {
        try
        {
            using var scope = _serviceProvider.CreateScope();
            var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();

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
