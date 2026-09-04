using AsistenteAyuntamiento.Domain.Common.Enums;

namespace AsistenteAyuntamiento.Worker.Services;

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
