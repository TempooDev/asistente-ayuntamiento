namespace AsistenteAyuntamiento.Application.Features.Ingestion.DTOs;

public class BlobItemDto
{
    public string Name { get; set; } = string.Empty;
    public long? Size { get; set; }
    public DateTime? LastModified { get; set; }
    public bool IsProcessed { get; set; }
    public string Status { get; set; } = string.Empty;

    /// <summary>Status of each pipeline; null when that pipeline never received the document.</summary>
    public string? BaselineStatus { get; set; }
    public string? HierarchicalStatus { get; set; }
    public string? RecursiveStatus { get; set; }
    public string? SemanticStatus { get; set; }

    /// <summary>Last error, prefixed with the pipeline that reported it.</summary>
    public string? ErrorMessage { get; set; }

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
