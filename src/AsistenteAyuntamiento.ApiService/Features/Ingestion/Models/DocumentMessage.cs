namespace AsistenteAyuntamiento.ApiService.Features.Ingestion.Models;

public class DocumentMessage
{
    [System.Text.Json.Serialization.JsonPropertyName("source")]
    public string Source { get; set; } = string.Empty;
    
    [System.Text.Json.Serialization.JsonPropertyName("document_id")]
    public string DocumentId { get; set; } = string.Empty;
    
    [System.Text.Json.Serialization.JsonPropertyName("blob_path")]
    public string BlobPath { get; set; } = string.Empty;
}
