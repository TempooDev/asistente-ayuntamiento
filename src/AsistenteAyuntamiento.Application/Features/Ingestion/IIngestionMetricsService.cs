using AsistenteAyuntamiento.Domain.Common.Enums;

namespace AsistenteAyuntamiento.Application.Features.Ingestion;

public interface IIngestionMetricsService
{
    Task TrackIngestionAsync(
        PipelineType pipeline, 
        BulletinType bulletin, 
        string documentId, 
        int tokensEmbedded, 
        int llmCalls, 
        int llmTokens, 
        int chunksGenerated, 
        long processingDurationMs, 
        CancellationToken cancellationToken = default);
}

