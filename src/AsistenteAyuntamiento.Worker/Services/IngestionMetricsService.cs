using System.Diagnostics;
using AsistenteAyuntamiento.Domain.Features.Ingestion;
using AsistenteAyuntamiento.Infrastructure.Data;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace AsistenteAyuntamiento.Worker.Services;

public class IngestionMetricsService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<IngestionMetricsService> _logger;

    public IngestionMetricsService(IServiceProvider serviceProvider, ILogger<IngestionMetricsService> logger)
    {
        _serviceProvider = serviceProvider;
        _logger = logger;
    }

    public async Task TrackIngestionAsync(
        string pipeline, 
        string bulletin, 
        string documentId, 
        int tokensEmbedded, 
        int llmCalls, 
        int llmTokens, 
        int chunksGenerated, 
        long processingDurationMs, 
        CancellationToken cancellationToken = default)
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
            
            _logger.LogInformation("Guardada métrica de ingesta para {DocumentId} en pipeline {Pipeline} ({Duration}ms).", documentId, pipeline, processingDurationMs);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error al guardar la métrica de ingesta para el documento {DocumentId}.", documentId);
        }
    }
}
