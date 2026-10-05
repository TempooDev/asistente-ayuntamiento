using AsistenteAyuntamiento.Application.Features.Ingestion.DTOs;

namespace AsistenteAyuntamiento.Application.Features.Ingestion;

public class BlobItemDto
{
    public string Name { get; set; } = string.Empty;
    public long? Size { get; set; }
    public DateTime? LastModified { get; set; }
    public bool IsProcessed { get; set; }
    public string Status { get; set; } = string.Empty;

    public BlobItemDto() { }
    public BlobItemDto(string name, long? size, DateTime? lastModified, bool isProcessed, string status)
    {
        Name = name;
        Size = size;
        LastModified = lastModified;
        IsProcessed = isProcessed;
        Status = status;
    }
}

public class BlobListResult
{
    public int Total { get; set; }
    public int Page { get; set; }
    public int PageSize { get; set; }
    public List<BlobItemDto> Items { get; set; } = new();
    public BlobListStats Stats { get; set; } = new();
}

public class BlobListStats
{
    public int Total { get; set; }
    public int Pending { get; set; }
    public int Queued { get; set; }
    public int Completed { get; set; }
    public int Processing { get; set; }
}

public interface IIngestionAdminService
{
    Task<BlobListResult> ListBlobsAsync(int? page, int? pageSize, string? status, string? search, DateTime? dateFrom, DateTime? dateTo, int? minSizeKb, int? maxSizeKb, CancellationToken cancellationToken = default);
    Task ResetDocumentStatusAsync(string documentId, CancellationToken cancellationToken = default);
    Task<(int CompletedCount, int PendingCount)> ResetStuckProcessingDocumentsAsync(CancellationToken cancellationToken = default);
    Task ResetIngestionAsync(CancellationToken cancellationToken = default);
    Task<int> EnqueueBulkAsync(List<ProcessBlobRequest> requests, string? pipelineMode, CancellationToken cancellationToken = default);
    Task<int> ReprocessAllAsync(string? pipelineMode, CancellationToken cancellationToken = default);
}
