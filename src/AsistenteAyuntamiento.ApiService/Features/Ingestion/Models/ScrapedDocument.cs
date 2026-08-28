namespace AsistenteAyuntamiento.ApiService.Features.Ingestion.Models;

public class ScrapedDocument
{
    [System.Text.Json.Serialization.JsonPropertyName("document_id")]
    public string DocumentId { get; set; } = string.Empty;
    
    [System.Text.Json.Serialization.JsonPropertyName("text")]
    public string Content { get; set; } = string.Empty;
    
    [System.Text.Json.Serialization.JsonPropertyName("metadata")]
    public DocumentMetadata? Metadata { get; set; }
}
