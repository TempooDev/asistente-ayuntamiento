using AsistenteAyuntamiento.Application.Features.Ingestion.DTOs;

namespace AsistenteAyuntamiento.Application.Features.Ingestion;

public interface IIngestionAdminService
{
    /// <param name="pipeline"><c>BASELINE</c> or <c>HIERARCHICAL</c> to filter <paramref name="status"/> on that pipeline's status; null for the summary status.</param>
    Task<BlobListResult> ListBlobsAsync(int? page, int? pageSize, string? status, string? search, DateTime? dateFrom, DateTime? dateTo, int? minSizeKb, int? maxSizeKb,
        string? pipeline = null, CancellationToken cancellationToken = default);
    Task ResetDocumentStatusAsync(string documentId, CancellationToken cancellationToken = default);
    Task<(int CompletedCount, int PendingCount)> ResetStuckProcessingDocumentsAsync(CancellationToken cancellationToken = default);
    Task ResetIngestionAsync(CancellationToken cancellationToken = default);
    Task<EnqueueResult> EnqueueBulkAsync(List<ProcessBlobRequest> requests, string? pipelineMode, CancellationToken cancellationToken = default);
    Task<EnqueueResult> ReprocessAllAsync(string? pipelineMode, CancellationToken cancellationToken = default);
}
