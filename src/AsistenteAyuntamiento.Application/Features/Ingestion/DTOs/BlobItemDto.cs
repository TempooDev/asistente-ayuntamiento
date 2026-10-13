namespace AsistenteAyuntamiento.Application.Features.Ingestion.DTOs;

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
