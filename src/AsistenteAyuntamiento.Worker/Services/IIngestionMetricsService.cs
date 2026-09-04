namespace AsistenteAyuntamiento.Worker.Services;

public interface IIngestionMetricsService
{
    Task TrackIngestionAsync(
        string pipeline, 
        string bulletin, 
        string documentId, 
        int tokensEmbedded, 
        int llmCalls, 
        int llmTokens, 
        int chunksGenerated, 
        long processingDurationMs, 
        CancellationToken cancellationToken = default);
}
