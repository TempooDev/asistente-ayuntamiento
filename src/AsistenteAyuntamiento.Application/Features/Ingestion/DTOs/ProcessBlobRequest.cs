namespace AsistenteAyuntamiento.Application.Features.Ingestion.DTOs;

public class ProcessBlobRequest
{
    public string BlobPath { get; set; } = string.Empty;
    public string Source { get; set; } = "Unknown";
}
