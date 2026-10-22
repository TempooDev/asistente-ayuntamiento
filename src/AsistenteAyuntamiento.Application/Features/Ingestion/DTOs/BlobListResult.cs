namespace AsistenteAyuntamiento.Application.Features.Ingestion.DTOs;

public class BlobListResult
{
    public int Total { get; set; }
    public int Page { get; set; }
    public int PageSize { get; set; }
    public List<BlobItemDto> Items { get; set; } = new();
    public BlobListStats Stats { get; set; } = new();
    public List<PipelineBlobStats> PipelineStats { get; set; } = new();
}
